using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace BrokenDiskResizer
{
    // Bir bölgenin okuma sonucu. Büyük değer = daha kötü.
    enum BlockState : byte { Unknown = 0, Ok = 1, Slow = 2, VerySlow = 3, Bad = 4 }

    // Bozuk bölgenin nereden geldiği
    // Dropped: tarama sırasında diskin Windows'tan düştüğü yer
    enum ZoneKind { Scan = 0, Log = 1, Manual = 2, Dropped = 3 }

    class Zone
    {
        public long Start, End;     // [Start, End) bayt
        public ZoneKind Kind;
        public Zone(long s, long e, ZoneKind k) { Start = s; End = e; Kind = k; }
        public long Length { get { return End - Start; } }
    }

    // Diskin okuma haritası. Disk eşit büyüklükte N parçaya bölünür; her parça içindeki en kötü sonuç tutulur.
    // 1 TB'lık diskte bir parça ~61 MB eder.
    class ScanMap
    {
        public const int N = 16384;

        public string Key = "", Model = "";
        public long Size;
        public readonly byte[] State = new byte[N];
        public readonly ushort[] Ms = new ushort[N];   // parçadaki en uzun okuma süresi (ms, 65535 = zaman aşımı/hata)
        public long ResumeAt = -1;                     // tam tarama yarıda kaldıysa kalınan yer
        public long ResumeEnd = -1;
        public readonly List<Zone> Marks = new List<Zone>();   // log'dan gelen ve elle işaretlenen bölgeler

        public ScanMap(string key, string model, long size) { Key = key; Model = model; Size = Math.Max(1, size); }

        public long BucketStart(int i) { return (long)(Size * (decimal)i / N); }
        public int BucketOf(long off)
        {
            if (off <= 0) return 0;
            if (off >= Size) return N - 1;
            return (int)Math.Min(N - 1, (long)(off * (decimal)N / Size));
        }

        public void Record(long off, long len, BlockState s, int ms)
        {
            int a = BucketOf(off), b = BucketOf(off + Math.Max(1, len) - 1);
            for (int i = a; i <= b; i++)
            {
                if ((byte)s > State[i]) State[i] = (byte)s;
                ushort m = (ushort)Math.Min(65535, Math.Max(0, ms));
                if (m > Ms[i]) Ms[i] = m;
            }
        }

        // Aralıktaki en kötü sonuç
        public BlockState Worst(long a, long b)
        {
            if (b <= a) return BlockState.Unknown;
            int ia = BucketOf(a), ib = BucketOf(b - 1);
            byte w = 0;
            for (int i = ia; i <= ib; i++) if (State[i] > w) w = State[i];
            return (BlockState)w;
        }

        public int MaxMs(long a, long b)
        {
            int ia = BucketOf(a), ib = BucketOf(Math.Max(a, b - 1));
            int w = 0;
            for (int i = ia; i <= ib; i++) if (Ms[i] > w) w = Ms[i];
            return w;
        }

        public void ClearRange(long a, long b)
        {
            int ia = BucketOf(a), ib = BucketOf(Math.Max(a, b - 1));
            for (int i = ia; i <= ib; i++) { State[i] = 0; Ms[i] = 0; }
        }

        public void ClearAll()
        {
            Array.Clear(State, 0, N);
            Array.Clear(Ms, 0, N);
            ResumeAt = ResumeEnd = -1;
            Marks.RemoveAll(z => z.Kind == ZoneKind.Scan);
        }

        public double Coverage
        {
            get
            {
                int c = 0;
                for (int i = 0; i < N; i++) if (State[i] != 0) c++;
                return c / (double)N;
            }
        }

        public bool Any(BlockState s)
        {
            for (int i = 0; i < N; i++) if (State[i] == (byte)s) return true;
            return false;
        }

        // Taramadan çıkan bozuk bölgeler: eşiği aşan komşu parçalar birleştirilir
        // Aralarında 1 GB'tan az sağlam yer kalan bölgeler de tek bölge sayılır (dağınık hatalar tek kümede gösterilir)
        public List<Zone> ScanZones(BlockState threshold)
        {
            var r = new List<Zone>();
            long gap = Math.Max(1000000000L, Size / 1000);
            int i = 0;
            while (i < N)
            {
                if (State[i] >= (byte)threshold)
                {
                    int j = i;
                    while (j + 1 < N && State[j + 1] >= (byte)threshold) j++;
                    long a = BucketStart(i), b = j + 1 >= N ? Size : BucketStart(j + 1);
                    if (r.Count > 0 && a - r[r.Count - 1].End <= gap) r[r.Count - 1].End = b;
                    else r.Add(new Zone(a, b, ZoneKind.Scan));
                    i = j + 1;
                }
                else i++;
            }
            return r;
        }

        // Yeni bölge eklenirken aynı türden çakışan/bitişik bölgelerle birleştirilir
        public void AddMark(long a, long b, ZoneKind kind)
        {
            a = Math.Max(0, a);
            b = Math.Min(Size, b);
            if (b <= a) return;
            var keep = new List<Zone>();
            foreach (var z in Marks)
            {
                if (z.Kind == kind && z.Start <= b && z.End >= a)
                {
                    a = Math.Min(a, z.Start);
                    b = Math.Max(b, z.End);
                }
                else keep.Add(z);
            }
            keep.Add(new Zone(a, b, kind));
            keep.Sort((x, y) => x.Start.CompareTo(y.Start));
            Marks.Clear();
            Marks.AddRange(keep);
        }

        // ------------------------------------------------------------------ Kayıt
        public static string Folder
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrokenDiskResizer", "maps");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        string FilePath { get { return Path.Combine(Folder, Key + ".map"); } }

        const int Magic = 0x31524442; // "BDR1"

        public void Save()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    w.Write(Magic);
                    w.Write(Key);
                    w.Write(Model);
                    w.Write(Size);
                    w.Write(N);
                    w.Write(State);
                    for (int i = 0; i < N; i++) w.Write(Ms[i]);
                    w.Write(ResumeAt);
                    w.Write(ResumeEnd);
                    w.Write(Marks.Count);
                    foreach (var z in Marks) { w.Write(z.Start); w.Write(z.End); w.Write((int)z.Kind); }
                }
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { }
        }

        public static ScanMap Load(DiskInfo d)
        {
            var m = new ScanMap(d.Key, d.Model, d.Size);
            try
            {
                if (!File.Exists(m.FilePath)) return m;
                using (var r = new BinaryReader(File.OpenRead(m.FilePath)))
                {
                    if (r.ReadInt32() != Magic) return m;
                    r.ReadString();
                    r.ReadString();
                    long size = r.ReadInt64();
                    int n = r.ReadInt32();
                    if (size != d.Size || n != N) return m;   // farklı boyut: eski harita bu diske ait değil
                    var st = r.ReadBytes(N);
                    Array.Copy(st, m.State, N);
                    for (int i = 0; i < N; i++) m.Ms[i] = r.ReadUInt16();
                    m.ResumeAt = r.ReadInt64();
                    m.ResumeEnd = r.ReadInt64();
                    int c = r.ReadInt32();
                    for (int i = 0; i < c; i++)
                    {
                        long a = r.ReadInt64(), b = r.ReadInt64();
                        int k = r.ReadInt32();
                        m.Marks.Add(new Zone(a, b, (ZoneKind)k));
                    }
                }
            }
            catch { m = new ScanMap(d.Key, d.Model, d.Size); }
            return m;
        }
    }

    // ------------------------------------------------------------------ Format/tarama log'u içe aktarma
    // "HDD Low Level Format Tool" ve benzeri araçların log'larındaki "error at offset ... bytes" satırlarını okur.
    class LogImport
    {
        public readonly List<long> BadOffsets = new List<long>();
        public long BlockSize = 1L << 20;
        public long DeviceCapacity;
        public long Processed = -1;
        public string Device = "";

        static long Num(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            long v;
            return sb.Length > 0 && long.TryParse(sb.ToString(), out v) ? v : -1;
        }

        public static LogImport Parse(string text)
        {
            var r = new LogImport();
            var seen = new HashSet<long>();
            var errAt = new Regex(@"(?:error|skipping[^\n]*?block|bad|unreadable|failed)[^\n]*?offset\s+([\d.,\s]+?)\s*bytes", RegexOptions.IgnoreCase);
            var lba = new Regex(@"(?:error|bad|unreadable)[^\n]*?(?:sector|LBA)\s*[:#]?\s*([\d.,]+)", RegexOptions.IgnoreCase);
            var cap = new Regex(@"(?:Device capacity|Capacity)\s*:\s*([\d.,]+)\s*bytes", RegexOptions.IgnoreCase);
            var proc = new Regex(@"Bytes processed\s*:\s*([\d.,]+)", RegexOptions.IgnoreCase);
            var dev = new Regex(@"Device\s*:\s*(.+)$", RegexOptions.IgnoreCase);
            var blk = new Regex(@"(\d+)\s*MB block", RegexOptions.IgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                Match m;
                if ((m = cap.Match(line)).Success && r.DeviceCapacity == 0) r.DeviceCapacity = Num(m.Groups[1].Value);
                if ((m = proc.Match(line)).Success) r.Processed = Num(m.Groups[1].Value);
                if ((m = dev.Match(line)).Success && r.Device == "") r.Device = m.Groups[1].Value.Trim();
                if ((m = blk.Match(line)).Success)
                {
                    long mb = Num(m.Groups[1].Value);
                    if (mb > 0 && mb <= 64) r.BlockSize = Math.Max(r.BlockSize, mb << 20);
                }
                if ((m = errAt.Match(line)).Success)
                {
                    long off = Num(m.Groups[1].Value);
                    if (off >= 0 && seen.Add(off)) r.BadOffsets.Add(off);
                }
                else if ((m = lba.Match(line)).Success)
                {
                    long s = Num(m.Groups[1].Value);
                    if (s >= 0 && seen.Add(s * 512)) r.BadOffsets.Add(s * 512);
                }
            }
            r.BadOffsets.Sort();
            return r;
        }

        // Ardışık hatalı blokları tek bölge yapar
        public List<Zone> Zones()
        {
            var z = new List<Zone>();
            foreach (long o in BadOffsets)
            {
                long e = o + BlockSize;
                if (z.Count > 0 && o <= z[z.Count - 1].End) z[z.Count - 1].End = Math.Max(z[z.Count - 1].End, e);
                else z.Add(new Zone(o, e, ZoneKind.Log));
            }
            return z;
        }
    }

    // ------------------------------------------------------------------ Sayı biçimleri
    static class Fmt
    {
        static CultureInfo Cul { get { return L.Lang == "tr" ? new CultureInfo("tr-TR") : CultureInfo.InvariantCulture; } }

        // Disk üreticileri gibi 1 GB = 1.000.000.000 bayt
        public static string Bytes(long b)
        {
            double v = b;
            if (v >= 1e12) return (v / 1e12).ToString(v >= 1e13 ? "0.0" : "0.00", Cul) + " TB";
            if (v >= 1e9) return (v / 1e9).ToString(v >= 1e11 ? "0" : "0.0", Cul) + " GB";
            if (v >= 1e6) return (v / 1e6).ToString("0", Cul) + " MB";
            if (v >= 1e3) return (v / 1e3).ToString("0", Cul) + " KB";
            return b + " B";
        }

        public static string GB(long b) { return (b / 1e9).ToString("0.0", Cul) + " GB"; }
        public static string N(double v, string f) { return v.ToString(f, Cul); }

        public static string Time(TimeSpan t)
        {
            if (t.TotalHours >= 1) return string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
            return string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }
    }
}
