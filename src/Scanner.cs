using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BrokenDiskResizer
{
    enum ScanMode { Quick, Full }

    enum ScanEnd { Done, Cancelled, DeviceLost, OpenFailed }

    // Diske SADECE okuma erişimiyle bağlanır; hiçbir koşulda yazmaz.
    // Her blok için okuma süresi ölçülür; yavaş ya da hatalı bloklar haritaya işlenir.
    // Okuma takılırsa belirli bir süre sonra iptal edilir; disk tamamen yanıt vermezse tarama güvenle durur.
    class Scanner
    {
        public const int BlockSize = 1 << 20;             // 1 MB
        public const int SlowMs = 80, VerySlowMs = 300, BadMs = 2000;
        const uint TimeoutMs = 8000;                     // tek okuma en fazla bu kadar beklenir
        const int SkipAfterBad = 16;                     // art arda bu kadar hatalı blokta ileri atlanır

        readonly DiskInfo disk;
        readonly ScanMap map;
        readonly ScanMode mode;
        readonly long from, to;
        volatile bool stop;
        Thread thread;

        // İlerleme: (konum, hız MB/s, kalan süre, son blok durumu)
        public event Action<long, double, TimeSpan, BlockState> Progress;
        public event Action<string> Log;
        public event Action<ScanEnd, string> Finished;

        public long Position { get; private set; }
        public long DroppedAt = -1, DroppedLen;   // disk düştüyse yeri (arayüz bunu haritaya işaretler)
        public bool Running { get { return thread != null && thread.IsAlive; } }

        // Bilinen bozuk bölgeler (log/elle işaretli + 1 GB pay) okunmaz: o yerler diski yeniden düşürebilir
        readonly System.Collections.Generic.List<Span> skip = new System.Collections.Generic.List<Span>();
        bool skipSaid;

        public Scanner(DiskInfo disk, ScanMap map, ScanMode mode, long from, long to)
        {
            foreach (var z in map.Marks)
                if (z.Kind != ZoneKind.Scan)
                    skip.Add(new Span(Math.Max(0, z.Start - (1L << 30)), Math.Min(disk.Size, z.End + (1L << 30))));
            this.disk = disk;
            this.map = map;
            this.mode = mode;
            long align = Math.Max(disk.SectorSize, 4096);
            this.from = Math.Max(0, from / align * align);
            this.to = Math.Min(disk.Size, Math.Max(this.from, to));
            Position = this.from;
        }

        public void Start()
        {
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "DiskScan";
            thread.Start();
        }

        public void Stop() { stop = true; }

        void Say(string s) { var h = Log; if (h != null) h(s); }

        static bool DeviceGone(int err)
        {
            // 433: aygıt yok, 1167: aygıt bağlı değil, 21: hazır değil, 55: ağ/aygıt yok, 2: bulunamadı, 6: geçersiz tanıtıcı
            return err == 433 || err == 1167 || err == 21 || err == 55 || err == 2 || err == 6 || err == 15;
        }

        public static BlockState Classify(int ms)
        {
            if (ms >= BadMs) return BlockState.Bad;
            if (ms >= VerySlowMs) return BlockState.VerySlow;
            if (ms >= SlowMs) return BlockState.Slow;
            return BlockState.Ok;
        }

        void Run()
        {
            ScanEnd end = ScanEnd.Done;
            string detail = "";
            IntPtr buf = IntPtr.Zero, ov = IntPtr.Zero, ev = IntPtr.Zero;
            bool leak = false;   // takılı kalmış okuma varsa bellek serbest bırakılmaz
            SafeFileHandle h = null;
            try
            {
                h = Native.CreateFile(disk.Path, Native.GENERIC_READ, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
                    Native.OPEN_EXISTING, Native.FILE_FLAG_NO_BUFFERING | Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h.IsInvalid)
                {
                    int e = Marshal.GetLastWin32Error();
                    end = DeviceGone(e) ? ScanEnd.DeviceLost : ScanEnd.OpenFailed;
                    detail = Native.ErrText(e);
                    return;
                }
                buf = Native.VirtualAlloc(IntPtr.Zero, (UIntPtr)BlockSize, 0x3000, 0x04);
                ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
                ev = Native.CreateEvent(IntPtr.Zero, true, false, null);

                if (mode == ScanMode.Full) RunFull(h, buf, ov, ev, ref end, ref detail, ref leak);
                else RunQuick(h, buf, ov, ev, ref end, ref detail, ref leak);
            }
            catch (Exception ex)
            {
                end = ScanEnd.OpenFailed;
                detail = ex.Message;
            }
            finally
            {
                if (!leak)
                {
                    if (buf != IntPtr.Zero) Native.VirtualFree(buf, UIntPtr.Zero, 0x8000);
                    if (ov != IntPtr.Zero) Marshal.FreeHGlobal(ov);
                    if (ev != IntPtr.Zero) Native.CloseHandle(ev);
                    if (h != null) h.Dispose();
                }
                map.Save();
                var f = Finished;
                if (f != null) f(end, detail);
            }
        }

        // Sıralı okuma: başından sonuna her 1 MB okunur
        void RunFull(SafeFileHandle h, IntPtr buf, IntPtr ov, IntPtr ev, ref ScanEnd end, ref string detail, ref bool leak)
        {
            var sw = Stopwatch.StartNew();
            long startPos = from, lastSave = 0;
            int badRun = 0;
            map.ResumeEnd = to;
            for (long off = from; off < to;)
            {
                if (stop) { end = ScanEnd.Cancelled; map.ResumeAt = off; return; }
                long se = SkipEnd(off);
                if (se > off)
                {
                    long next = Math.Min(to, (se + Plan.MiB - 1) / Plan.MiB * Plan.MiB);
                    SaySkip(off, next);
                    off = next;
                    Position = off;
                    continue;
                }
                int len = (int)Math.Min(BlockSize, to - off);
                len = len / disk.SectorSize * disk.SectorSize;
                if (len <= 0) break;

                int err, ms;
                bool stuck;
                Read(h, buf, ov, ev, off, len, out err, out ms, out stuck);
                if (stuck) leak = true;

                if (err != 0 && DeviceGone(err) || stuck)
                {
                    map.Record(off, len, BlockState.Bad, 65535);
                    DroppedAt = off;
                    DroppedLen = len;
                    map.ResumeAt = Math.Min(to, off + SkipAhead());
                    end = ScanEnd.DeviceLost;
                    detail = stuck ? L.T("okuma iptal edilemedi", "the read could not be cancelled") : Native.ErrText(err);
                    Say(L.T("Disk ", "The disk stopped responding at ") + Fmt.Bytes(off) + L.T(" civarında yanıt vermeyi kesti: ", ": ") + detail);
                    return;
                }

                BlockState st = err != 0 ? BlockState.Bad : Classify(ms);
                map.Record(off, len, st, err != 0 ? 65535 : ms);
                if (st == BlockState.Bad)
                {
                    badRun++;
                    Say(Fmt.Bytes(off) + ": " + (err != 0 ? Native.ErrText(err) : ms + " ms"));
                }
                else badRun = 0;

                off += len;
                if (badRun >= SkipAfterBad)
                {
                    // Art arda okunamayan bölgede diski yormamak için ileri atlanır; atlanan kısım bozuk sayılır
                    long skip = Math.Min(to - off, 256L << 20);
                    if (skip > 0)
                    {
                        map.Record(off, skip, BlockState.Bad, 65535);
                        Say(L.T("Art arda hata: ", "Repeated errors: ") + Fmt.Bytes(skip) + L.T(" atlandı (bozuk sayıldı) → ", " skipped (marked bad) → ") + Fmt.Bytes(off + skip));
                        off += skip;
                    }
                    badRun = 0;
                }
                Position = off;
                if (off - lastSave > (4L << 30)) { map.ResumeAt = off; map.Save(); lastSave = off; }
                Report(off, off - startPos, to - off, sw, st);
            }
            map.ResumeAt = -1;
            map.ResumeEnd = -1;
        }

        // Örnekleme: haritanın her parçasından bir blok okunur (1 TB ≈ 16 bin okuma, birkaç dakika)
        void RunQuick(SafeFileHandle h, IntPtr buf, IntPtr ov, IntPtr ev, ref ScanEnd end, ref string detail, ref bool leak)
        {
            var sw = Stopwatch.StartNew();
            int a = map.BucketOf(from), b = map.BucketOf(Math.Max(from, to - 1));
            int total = b - a + 1;
            for (int i = a; i <= b; i++)
            {
                if (stop) { end = ScanEnd.Cancelled; return; }
                long bs = map.BucketStart(i), be = i + 1 >= ScanMap.N ? disk.Size : map.BucketStart(i + 1);
                long align = Math.Max(disk.SectorSize, 4096);
                long off = (bs + (be - bs) / 2) / align * align;   // parçanın ortası
                int len = (int)Math.Min(BlockSize, disk.Size - off);
                len = len / disk.SectorSize * disk.SectorSize;
                if (len <= 0) continue;
                // Daha önce okunamamış ya da işaretli yerler yeniden okunmaz
                if (map.State[i] == (byte)BlockState.Bad || SkipEnd(off) >= 0 || SkipEnd(off + len - 1) >= 0)
                {
                    if (!skipSaid && SkipEnd(off) >= 0) { skipSaid = true; SaySkip(off, SkipEnd(off)); }
                    continue;
                }

                int err, ms;
                bool stuck;
                Read(h, buf, ov, ev, off, len, out err, out ms, out stuck);
                if (stuck) leak = true;
                if (err != 0 && DeviceGone(err) || stuck)
                {
                    map.Record(off, len, BlockState.Bad, 65535);
                    DroppedAt = off;
                    DroppedLen = len;
                    end = ScanEnd.DeviceLost;
                    detail = stuck ? L.T("okuma iptal edilemedi", "the read could not be cancelled") : Native.ErrText(err);
                    Say(L.T("Disk ", "The disk stopped responding at ") + Fmt.Bytes(off) + L.T(" civarında yanıt vermeyi kesti: ", ": ") + detail);
                    return;
                }
                // Hızlı taramada her okumadan önce kafa yer değiştirir; ~15 ms arama süresi düşülür
                int net = Math.Max(0, ms - 15);
                BlockState st = err != 0 ? BlockState.Bad : Classify(net);
                map.Record(off, len, st, err != 0 ? 65535 : net);
                if (st >= BlockState.VerySlow) Say(Fmt.Bytes(off) + ": " + (err != 0 ? Native.ErrText(err) : net + " ms"));
                Position = off;
                int done = i - a + 1;
                double frac = done / (double)total;
                var p = Progress;
                if (p != null && (done % 16 == 0 || st >= BlockState.Slow))
                {
                    double el = sw.Elapsed.TotalSeconds;
                    var eta = TimeSpan.FromSeconds(frac > 0 ? el / frac - el : 0);
                    p(off, done / Math.Max(0.001, el), eta, st);   // hızlı taramada "hız" = okuma/sn
                }
            }
        }

        // off atlanacak bir bölgedeyse o bölgenin sonu, değilse -1
        long SkipEnd(long off)
        {
            foreach (var sp in skip) if (off >= sp.Start && off < sp.End) return sp.End;
            return -1;
        }

        void SaySkip(long a, long b)
        {
            Say(L.T("Bilinen bozuk bölge okunmadı (diski düşürmemek için): ", "Known bad area not read (to avoid dropping the disk): ") + Fmt.GB(a) + " – " + Fmt.GB(b));
        }

        long SkipAhead() { return Math.Max(1L << 30, disk.Size / 100); }

        DateTime lastReport;
        void Report(long off, long done, long left, Stopwatch sw, BlockState st)
        {
            var now = DateTime.UtcNow;
            if ((now - lastReport).TotalMilliseconds < 150 && st < BlockState.Slow) return;
            lastReport = now;
            double sec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
            double mbs = done / 1e6 / sec;
            var eta = TimeSpan.FromSeconds(mbs > 0 ? left / 1e6 / mbs : 0);
            var p = Progress;
            if (p != null) p(off, mbs, eta, st);
        }

        // Tek okuma. stuck = iptal edilmesine rağmen okuma bitmedi (sürücü kilitlendi)
        static void Read(SafeFileHandle h, IntPtr buf, IntPtr ov, IntPtr ev, long off, int len, out int err, out int ms, out bool stuck)
        {
            err = 0;
            stuck = false;
            var o = new NativeOverlapped();
            o.OffsetLow = (int)(off & 0xFFFFFFFF);
            o.OffsetHigh = (int)(off >> 32);
            o.EventHandle = ev;
            Native.ResetEvent(ev);
            Marshal.StructureToPtr(o, ov, false);

            var sw = Stopwatch.StartNew();
            uint got;
            if (!Native.ReadFile(h, buf, (uint)len, IntPtr.Zero, ov))
            {
                int e = Marshal.GetLastWin32Error();
                if (e != 997) { err = e; ms = (int)sw.ElapsedMilliseconds; return; }   // 997 = ERROR_IO_PENDING
                uint w = Native.WaitForSingleObject(ev, TimeoutMs);
                if (w != 0)
                {
                    // Zaman aşımı: okuma iptal edilir, bölge bozuk sayılır
                    Native.CancelIoEx(h, ov);
                    if (Native.WaitForSingleObject(ev, 30000) != 0) { stuck = true; err = 1460; ms = 65535; return; }
                    Native.GetOverlappedResult(h, ov, out got, false);
                    err = 1460;   // ERROR_TIMEOUT
                    ms = (int)sw.ElapsedMilliseconds;
                    return;
                }
            }
            if (!Native.GetOverlappedResult(h, ov, out got, false)) err = Marshal.GetLastWin32Error();
            else if (got < len) err = 23;   // eksik okuma: veri hatası sayılır
            ms = (int)sw.ElapsedMilliseconds;
        }
    }
}
