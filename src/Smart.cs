using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;

namespace BrokenDiskResizer
{
    // Diskin kendi tuttuğu sağlık sayaçları (S.M.A.R.T.)
    // Windows'un standart SATA sürücüsü bunları root\wmi altında MSStorageDriver_FailurePredictData olarak verir.
    // NVMe, USB kutuları ve bazı RAID sürücüleri vermez; o durumda "okunamadı" gösterilir.
    class SmartInfo
    {
        public bool Ok;
        public long Reallocated = -1;     // 5   yedek alana taşınmış sektör
        public long Pending = -1;         // 197 okunamayıp bekleyen sektör
        public long Offline = -1;         // 198 düzeltilemeyen sektör
        public long Reported = -1;        // 187 bildirilen düzeltilemeyen hata
        public long Temperature = -1;     // 194 °C
        public long Hours = -1;           // 9   çalışma saati
        public DateTime When = DateTime.Now;

        // Bu diskte ilk kontrolde görülen değerler (artış var mı diye karşılaştırılır)
        public SmartInfo First;
        public DateTime FirstWhen;

        public long BadTotal
        {
            get { return Math.Max(0, Reallocated) + Math.Max(0, Pending) + Math.Max(0, Offline) + Math.Max(0, Reported); }
        }

        public static long Delta(long now, long then)
        {
            if (now < 0 || then < 0) return 0;
            return now - then;
        }
    }

    static class Smart
    {
        public static SmartInfo Read(DiskInfo d)
        {
            var s = new SmartInfo();
            try
            {
                string pnp = "";
                using (var q = new ManagementObjectSearcher("SELECT Index, PNPDeviceID FROM Win32_DiskDrive"))
                    foreach (ManagementObject o in q.Get())
                        if (o["Index"] != null && Convert.ToInt32(o["Index"]) == d.Number && o["PNPDeviceID"] != null)
                            pnp = o["PNPDeviceID"].ToString();
                if (pnp == "") return s;

                var scope = new ManagementScope(@"\\.\root\wmi");
                using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData")))
                    foreach (ManagementObject o in q.Get())
                    {
                        string inst = o["InstanceName"] == null ? "" : o["InstanceName"].ToString();
                        if (!inst.StartsWith(pnp, StringComparison.OrdinalIgnoreCase)) continue;
                        var vs = o["VendorSpecific"] as byte[];
                        if (vs == null || vs.Length < 362) continue;
                        Parse(vs, s);
                        s.Ok = true;
                        break;
                    }
            }
            catch { s.Ok = false; }
            if (s.Ok) History(d, s);
            return s;
        }

        // 2 baytlık başlıktan sonra 30 adet 12 baytlık kayıt: kimlik, bayraklar(2), değer, en kötü, ham değer(6), ayrılmış
        static void Parse(byte[] v, SmartInfo s)
        {
            for (int i = 0; i < 30; i++)
            {
                int off = 2 + i * 12;
                int id = v[off];
                if (id == 0) continue;
                long raw = 0;
                for (int b = 5; b >= 0; b--) raw = (raw << 8) | v[off + 5 + b];
                switch (id)
                {
                    case 5: s.Reallocated = raw & 0xFFFFFFFF; break;
                    case 197: s.Pending = raw & 0xFFFFFFFF; break;
                    case 198: s.Offline = raw & 0xFFFFFFFF; break;
                    case 187: s.Reported = raw & 0xFFFF; break;
                    case 194: s.Temperature = raw & 0xFF; break;
                    case 9: s.Hours = raw & 0xFFFFFFFF; break;
                }
            }
        }

        // ------------------------------------------------------------------ Geçmiş
        // %AppData%\BrokenDiskResizer\smart\<disk>.csv  —  tarih;5;197;198;187
        static string FilePath(DiskInfo d)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrokenDiskResizer", "smart");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, d.Key + ".csv");
        }

        static void History(DiskInfo d, SmartInfo s)
        {
            try
            {
                string path = FilePath(d);
                var lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();
                SmartInfo first = null, last = null;
                DateTime firstWhen = DateTime.MinValue, lastWhen = DateTime.MinValue;
                foreach (var l in lines)
                {
                    var p = l.Split(';');
                    if (p.Length < 5) continue;
                    DateTime t;
                    if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                    var x = new SmartInfo { Reallocated = L2(p[1]), Pending = L2(p[2]), Offline = L2(p[3]), Reported = L2(p[4]), Ok = true };
                    if (first == null) { first = x; firstWhen = t; }
                    last = x;
                    lastWhen = t;
                }
                s.First = first;
                s.FirstWhen = firstWhen;
                bool changed = last == null || last.Reallocated != s.Reallocated || last.Pending != s.Pending || last.Offline != s.Offline || last.Reported != s.Reported;
                if (changed || (DateTime.Now - lastWhen).TotalDays >= 7)
                    File.AppendAllText(path, string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm};{1};{2};{3};{4}\r\n",
                        DateTime.Now, s.Reallocated, s.Pending, s.Offline, s.Reported));
            }
            catch { }
        }

        static long L2(string v) { long r; return long.TryParse(v, out r) ? r : -1; }
    }
}
