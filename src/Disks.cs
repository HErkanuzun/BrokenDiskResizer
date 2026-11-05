using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BrokenDiskResizer
{
    // Bilgisayardaki fiziksel disklerden biri
    class DiskInfo
    {
        public int Number;              // \\.\PhysicalDriveN
        public string Model = "";
        public string Serial = "";
        public long Size;               // bayt (tam)
        public int SectorSize = 512;
        public bool IsSystem, IsBoot, IsOffline, IsReadOnly;
        public int Health = -1;         // 0 sağlıklı, 1 uyarı, 2 sağlıksız, -1 bilinmiyor
        public int PartitionStyle;      // 0 boş, 1 MBR, 2 GPT
        public List<char> Letters = new List<char>();

        public string Path { get { return @"\\.\PhysicalDrive" + Number; } }

        // Haritanın kaydedildiği anahtar: disk numarası değişse de aynı disk bulunur
        public string Key
        {
            get
            {
                string s = Serial.Trim();
                if (s == "") s = Model.Trim() + "_" + Size;
                var chars = s.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                    if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
                return new string(chars);
            }
        }

        // Windows'un çalıştığı disk: bunu bölümlemek bilgisayarı açılmaz hale getirir
        public bool Protected
        {
            get
            {
                if (IsSystem || IsBoot) return true;
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (win.Length > 0 && Letters.Contains(char.ToUpperInvariant(win[0]))) return true;
                string me = AppDomain.CurrentDomain.BaseDirectory;
                if (me.Length > 0 && Letters.Contains(char.ToUpperInvariant(me[0]))) return true;
                return false;
            }
        }
    }

    static class Disks
    {
        // Windows 8+ Storage WMI (MSFT_Disk) sistem/önyükleme diskini kendisi bildirir; yoksa Win32_DiskDrive'a düşülür
        public static List<DiskInfo> List()
        {
            var list = new List<DiskInfo>();
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_Disk")))
                    foreach (ManagementObject o in s.Get())
                    {
                        var d = new DiskInfo();
                        d.Number = ToInt(o["Number"], -1);
                        if (d.Number < 0) continue;
                        d.Model = Str(o["Model"]);
                        if (d.Model == "") d.Model = Str(o["FriendlyName"]);
                        d.Serial = Str(o["SerialNumber"]);
                        d.Size = ToLong(o["Size"]);
                        d.SectorSize = Math.Max(512, ToInt(o["LogicalSectorSize"], 512));
                        d.IsSystem = ToBool(o["IsSystem"]);
                        d.IsBoot = ToBool(o["IsBoot"]);
                        d.IsOffline = ToBool(o["IsOffline"]);
                        d.IsReadOnly = ToBool(o["IsReadOnly"]);
                        d.Health = ToInt(o["HealthStatus"], -1);
                        d.PartitionStyle = ToInt(o["PartitionStyle"], 0);
                        list.Add(d);
                    }
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition")))
                    foreach (ManagementObject o in s.Get())
                    {
                        int n = ToInt(o["DiskNumber"], -1);
                        int c = ToInt(o["DriveLetter"], 0);
                        if (c < 'A' || c > 'z') continue;
                        foreach (var d in list)
                            if (d.Number == n && !d.Letters.Contains(char.ToUpperInvariant((char)c))) d.Letters.Add(char.ToUpperInvariant((char)c));
                    }
            }
            catch { list.Clear(); }

            if (list.Count == 0) list = Legacy();

            // WMI boyutu yuvarlanmış olabilir: diskin kendisine sorulur
            foreach (var d in list)
            {
                long exact = ExactSize(d.Path);
                if (exact > 0) d.Size = exact;
            }
            list.Sort((a, b) => a.Number.CompareTo(b.Number));
            return list;
        }

        static List<DiskInfo> Legacy()
        {
            var list = new List<DiskInfo>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Index, Model, SerialNumber, Size, BytesPerSector FROM Win32_DiskDrive"))
                    foreach (ManagementObject o in s.Get())
                    {
                        var d = new DiskInfo();
                        d.Number = ToInt(o["Index"], -1);
                        if (d.Number < 0) continue;
                        d.Model = Str(o["Model"]);
                        d.Serial = Str(o["SerialNumber"]);
                        d.Size = ToLong(o["Size"]);
                        d.SectorSize = Math.Max(512, ToInt(o["BytesPerSector"], 512));
                        list.Add(d);
                    }
                // Sürücü harfleri: her birimin hangi diskte olduğu sorulur
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed) continue;
                    foreach (int n in VolumeDisks(drive.Name[0]))
                        foreach (var d in list)
                            if (d.Number == n && !d.Letters.Contains(char.ToUpperInvariant(drive.Name[0]))) d.Letters.Add(char.ToUpperInvariant(drive.Name[0]));
                }
            }
            catch { }
            return list;
        }

        // Aynı disk hâlâ aynı numarada mı? (bölümlemeden hemen önce yeniden doğrulanır)
        public static DiskInfo Find(string key)
        {
            foreach (var d in List()) if (d.Key == key) return d;
            return null;
        }

        static string Str(object o) { return o == null ? "" : o.ToString().Trim(); }
        static int ToInt(object o, int def) { try { return o == null ? def : Convert.ToInt32(o); } catch { return def; } }
        static long ToLong(object o) { try { return o == null ? 0 : Convert.ToInt64(o); } catch { return 0; } }
        static bool ToBool(object o) { try { return o != null && Convert.ToBoolean(o); } catch { return false; } }

        // ------------------------------------------------------------------ Win32
        const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
        const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;

        public static long ExactSize(string path)
        {
            using (var h = Native.Open(path, Native.GENERIC_READ))
            {
                if (h.IsInvalid) return 0;
                long len;
                uint got;
                if (!Native.DeviceIoControl(h, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, out len, 8, out got, IntPtr.Zero)) return 0;
                return len;
            }
        }

        static List<int> VolumeDisks(char letter)
        {
            var r = new List<int>();
            using (var h = Native.Open(@"\\.\" + letter + ":", 0))
            {
                if (h.IsInvalid) return r;
                var buf = Marshal.AllocHGlobal(1024);
                try
                {
                    uint got;
                    if (Native.DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, buf, 1024, out got, IntPtr.Zero))
                    {
                        int count = Marshal.ReadInt32(buf);
                        for (int i = 0; i < count; i++) r.Add(Marshal.ReadInt32(buf, 8 + i * 24));
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return r;
        }
    }

    static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_NO_BUFFERING = 0x20000000, FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        public static SafeFileHandle Open(string path, uint access)
        {
            return CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, uint inSize, out long outVal, uint outSize, out uint got, IntPtr ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, uint inSize, IntPtr outBuf, uint outSize, out uint got, IntPtr ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buf, uint n, IntPtr read, IntPtr ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out uint n, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr h, uint ms);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateEvent(IntPtr sec, bool manualReset, bool initial, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ResetEvent(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFree(IntPtr addr, UIntPtr size, uint type);

        public static string ErrText(int code)
        {
            try { return new Win32Exception(code).Message + " (" + code + ")"; }
            catch { return "Win32 " + code; }
        }
    }
}
