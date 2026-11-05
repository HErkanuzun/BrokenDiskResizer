using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BrokenDiskResizer
{
    struct Span
    {
        public long Start, End;
        public Span(long s, long e) { Start = s; End = e; }
        public long Length { get { return End - Start; } }
    }

    class PlanOptions
    {
        public double BufferPct = 10;          // her bozuk bölgenin iki yanına, disk boyutunun yüzdesi kadar
        public bool CountVerySlow = true;      // çok yavaş okunan yerler de bozuk sayılır
        public bool ExcludeUnscanned = true;   // hiç okunmamış yerler de kullanılmaz
        public long MinPart = 1000000000L;     // bundan küçük sağlam parçalara bölüm açılmaz (1 GB)
    }

    class Plan
    {
        public long Size;
        public long Buffer;
        public readonly List<Zone> Bad = new List<Zone>();          // bozuk bölgeler (tarama + log + elle)
        public readonly List<Span> Excluded = new List<Span>();     // bozuk + güvenlik payı (kullanılmayacak)
        public readonly List<Span> Usable = new List<Span>();       // bölüm açılacak aralıklar
        public long UsableBytes;

        public const long MiB = 1L << 20;
        public const long HeadReserve = 136 * MiB;   // GPT başlığı ve Windows'un ayırdığı MSR bölümü için
        public const long TailReserve = 2 * MiB;     // diskin sonundaki yedek GPT için

        public bool IsExcluded(long a, long b)
        {
            foreach (var s in Excluded) if (s.Start < b && s.End > a) return true;
            return false;
        }

        public static Plan Build(ScanMap map, PlanOptions o)
        {
            var p = new Plan();
            p.Size = map.Size;
            p.Buffer = (long)(map.Size * Math.Max(0, o.BufferPct) / 100.0);

            p.Bad.AddRange(map.ScanZones(o.CountVerySlow ? BlockState.VerySlow : BlockState.Bad));
            foreach (var z in map.Marks) if (z.Kind != ZoneKind.Scan) p.Bad.Add(z);
            p.Bad.Sort((x, y) => x.Start.CompareTo(y.Start));

            var ex = new List<Span>();
            foreach (var z in p.Bad) ex.Add(new Span(Math.Max(0, z.Start - p.Buffer), Math.Min(map.Size, z.End + p.Buffer)));
            if (o.ExcludeUnscanned)
            {
                int i = 0;
                while (i < ScanMap.N)
                {
                    if (map.State[i] == 0)
                    {
                        int j = i;
                        while (j + 1 < ScanMap.N && map.State[j + 1] == 0) j++;
                        ex.Add(new Span(map.BucketStart(i), j + 1 >= ScanMap.N ? map.Size : map.BucketStart(j + 1)));
                        i = j + 1;
                    }
                    else i++;
                }
            }
            p.Excluded.AddRange(Merge(ex));

            // Kalan sağlam aralıklar
            long lo = HeadReserve, hi = map.Size - TailReserve;
            long cur = lo;
            foreach (var s in p.Excluded)
            {
                if (s.End <= cur) continue;
                if (s.Start > cur) p.AddUsable(cur, Math.Min(s.Start, hi), o.MinPart);
                cur = Math.Max(cur, s.End);
                if (cur >= hi) break;
            }
            if (cur < hi) p.AddUsable(cur, hi, o.MinPart);
            foreach (var u in p.Usable) p.UsableBytes += u.Length;
            return p;
        }

        void AddUsable(long a, long b, long min)
        {
            a = (a + MiB - 1) / MiB * MiB;   // 1 MB hizalı
            b = b / MiB * MiB;
            if (b - a >= Math.Max(MiB, min)) Usable.Add(new Span(a, b));
        }

        static List<Span> Merge(List<Span> list)
        {
            list.Sort((x, y) => x.Start.CompareTo(y.Start));
            var r = new List<Span>();
            foreach (var s in list)
            {
                if (s.End <= s.Start) continue;
                if (r.Count > 0 && s.Start <= r[r.Count - 1].End)
                {
                    var last = r[r.Count - 1];
                    last.End = Math.Max(last.End, s.End);
                    r[r.Count - 1] = last;
                }
                else r.Add(s);
            }
            return r;
        }
    }

    // ------------------------------------------------------------------ diskpart ile uygulama
    static class DiskPart
    {
        // Diski siler, GPT yapar ve sadece sağlam aralıklara bölüm açar. Aradaki yerler "ayrılmamış" kalır.
        public static string Script(DiskInfo d, Plan p, string labelPrefix)
        {
            var sb = new StringBuilder();
            sb.AppendLine("rem Broken Disk Resizer - " + d.Model + " (" + Fmt.Bytes(d.Size) + "), seri: " + d.Serial);
            sb.AppendLine("rem DIKKAT: bu betik diskteki TUM verileri siler.");
            sb.AppendLine("select disk " + d.Number);
            sb.AppendLine("online disk noerr");
            sb.AppendLine("attributes disk clear readonly noerr");
            sb.AppendLine("clean");
            sb.AppendLine("convert gpt");
            int n = 0;
            foreach (var u in p.Usable)
            {
                n++;
                long offKB = u.Start / 1024;
                long sizeMB = u.Length / Plan.MiB;
                sb.AppendLine("rem Bolum " + n + ": " + Fmt.GB(u.Start) + " - " + Fmt.GB(u.End));
                sb.AppendLine("create partition primary size=" + sizeMB + " offset=" + offKB);
                sb.AppendLine("format fs=ntfs quick label=\"" + labelPrefix + n + "\"");
                sb.AppendLine("assign");
            }
            sb.AppendLine("exit");
            return sb.ToString();
        }

        public static int Run(string script, out string output)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "BrokenDiskResizer_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(tmp, script, Encoding.ASCII);
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "diskpart.exe"), "/s \"" + tmp + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (var pr = Process.Start(psi))
                {
                    string o = pr.StandardOutput.ReadToEnd();
                    string e = pr.StandardError.ReadToEnd();
                    pr.WaitForExit();
                    output = (o + "\n" + e).Trim();
                    return pr.ExitCode;
                }
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}
