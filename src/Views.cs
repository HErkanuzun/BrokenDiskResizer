using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BrokenDiskResizer
{
    // ------------------------------------------------------------------ Görünümlerin ortak durumu
    // Harita, plan, seçim ve okuma kafasının yeri burada tutulur; plaka ve şerit aynı veriyi gösterir.
    class ViewState
    {
        public ScanMap Map;
        public Plan Plan;
        public long SelA = -1, SelB = -1;
        public long Head = -1;               // tarama sırasında okunan yer
        public int Version;                  // veri değişince artar (önbellekli çizimler yenilenir)
        public event Action SelectionChanged;

        public bool HasSel { get { return Map != null && SelA >= 0 && SelB > SelA; } }

        public void SetSel(long a, long b)
        {
            if (Map == null) return;
            if (b < a) { long t = a; a = b; b = t; }
            a = Math.Max(0, a / Plan.MiB * Plan.MiB);
            b = Math.Min(Map.Size, (b + Plan.MiB - 1) / Plan.MiB * Plan.MiB);
            SelA = a;
            SelB = b;
            var h = SelectionChanged;
            if (h != null) h();
        }

        public void ClearSel()
        {
            SelA = SelB = -1;
            var h = SelectionChanged;
            if (h != null) h();
        }

        public bool IsMarked(long a, long b)
        {
            foreach (var z in Map.Marks) if (z.Kind != ZoneKind.Scan && z.Start < b && z.End > a) return true;
            return false;
        }

        // Bir aralığın rengi. hatch = güvenlik payı nedeniyle kullanılmayacak (kırmızı çizgili)
        public Color CellColor(long a, long b, out bool hatch)
        {
            hatch = false;
            if (Map == null) return Theme.Field;
            var st = Map.Worst(a, b);
            bool marked = IsMarked(a, b);
            if (marked || st == BlockState.Bad) return Theme.Danger;
            if (st == BlockState.VerySlow) return Color.FromArgb(240, 128, 52);
            bool ex = Plan != null && Plan.IsExcluded(a, b);
            hatch = ex;
            if (st == BlockState.Slow) return Theme.Warn;
            if (st == BlockState.Ok) return ex ? Theme.Mix(Theme.Accent, Theme.Bg, 0.62f) : Theme.Mix(Theme.Accent, Theme.Bg, 0.38f);
            return ex ? Theme.Mix(Theme.Field, Theme.Danger, 0.12f) : Theme.Field;   // taranmamış
        }

        public static string StateName(BlockState s)
        {
            switch (s)
            {
                case BlockState.Ok: return L.T("Sağlam", "Healthy");
                case BlockState.Slow: return L.T("Yavaş", "Slow");
                case BlockState.VerySlow: return L.T("Çok yavaş", "Very slow");
                case BlockState.Bad: return L.T("Okunamıyor", "Unreadable");
                default: return L.T("Taranmadı", "Not scanned");
            }
        }

        public static HatchBrush Stripes()
        {
            return new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(200, Theme.Danger), Color.FromArgb(0, 0, 0, 0));
        }
    }

    // ------------------------------------------------------------------ Dairesel disk görünümü
    // Disk bir plaka gibi çizilir. Dış halka diskin başı (ilk sektörler), içe doğru sonu.
    // Her halka saat yönünde ilerler; bir halka bitince bir içtekine geçilir.
    // Not: bu bir şemadır. Gerçek diskte sektörler birden fazla plakaya ve yüzeye dağılmıştır.
    class PlatterView : Ctl
    {
        public const int Rings = 32, Segs = 96;
        readonly ViewState vs;
        Bitmap cache;
        int cacheVersion = -1;
        Size cacheSize;
        long hoverOff = -1;
        long dragFrom = -1;
        Point downAt;
        bool dragging;

        public PlatterView(ViewState vs)
        {
            this.vs = vs;
            Cursor = Cursors.Cross;
        }

        float Side { get { return Math.Min(Width, Height); } }
        PointF Center { get { return new PointF(Width / 2f, Height / 2f); } }
        float R1 { get { return Side * 0.43f; } }
        float R0 { get { return R1 * 0.30f; } }
        float RingW { get { return (R1 - R0) / Rings; } }

        long CellStart(int i)
        {
            if (vs.Map == null) return 0;
            return (long)(vs.Map.Size * (decimal)i / (Rings * Segs));
        }

        int CellOf(long off)
        {
            if (vs.Map == null) return 0;
            int n = Rings * Segs;
            return (int)Math.Max(0, Math.Min(n - 1, (long)(off * (decimal)n / vs.Map.Size)));
        }

        GraphicsPath CellPath(int i, float gapDeg, float gapPx)
        {
            int ring = i / Segs, seg = i % Segs;
            var c = Center;
            float ro = R1 - ring * RingW, ri = ro - RingW;
            ro -= gapPx / 2;
            ri += gapPx / 2;
            float a0 = -90 + seg * 360f / Segs + gapDeg / 2, sweep = 360f / Segs - gapDeg;
            var p = new GraphicsPath();
            p.AddArc(c.X - ro, c.Y - ro, ro * 2, ro * 2, a0, sweep);
            p.AddArc(c.X - ri, c.Y - ri, ri * 2, ri * 2, a0 + sweep, -sweep);
            p.CloseFigure();
            return p;
        }

        PointF CellPoint(long off)
        {
            int n = Rings * Segs;
            double t = vs.Map == null ? 0 : off / (double)vs.Map.Size * n;
            int ring = (int)Math.Min(Rings - 1, t / Segs);
            double frac = (t - ring * Segs) / Segs;
            double ang = (-90 + frac * 360) * Math.PI / 180;
            float r = R1 - (ring + 0.5f) * RingW;
            var c = Center;
            return new PointF(c.X + (float)(Math.Cos(ang) * r), c.Y + (float)(Math.Sin(ang) * r));
        }

        // Ekrandaki noktaya karşılık gelen bayt konumu (plakanın dışındaysa -1)
        long OffsetAt(Point pt, bool clamp)
        {
            if (vs.Map == null) return -1;
            var c = Center;
            double dx = pt.X - c.X, dy = pt.Y - c.Y;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (!clamp && (r > R1 || r < R0)) return -1;
            r = Math.Max(R0 + 0.01, Math.Min(R1 - 0.01, r));
            int ring = (int)Math.Max(0, Math.Min(Rings - 1, (R1 - r) / RingW));
            double ang = Math.Atan2(dy, dx) * 180 / Math.PI + 90;
            if (ang < 0) ang += 360;
            double t = (ring + ang / 360.0) / Rings;
            return (long)(vs.Map.Size * Math.Max(0, Math.Min(1, t)));
        }

        void RenderCache()
        {
            if (cache != null && cacheVersion == vs.Version && cacheSize == Size) return;
            if (cache != null) cache.Dispose();
            cache = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            cacheSize = Size;
            cacheVersion = vs.Version;
            using (var g = Graphics.FromImage(cache))
            {
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var c = Center;
                float side = Side;

                // Disk gövdesi
                var body = new RectangleF(c.X - side / 2 + Theme.P(2), c.Y - side / 2 + Theme.P(2), side - Theme.P(4), side - Theme.P(4));
                Theme.Fill(g, body, Theme.P(18), Theme.Card);
                Theme.Stroke(g, body, Theme.P(18), Theme.Border, Theme.P(1.5f));
                float sr = Theme.P(5);
                foreach (var pt in new[] { new PointF(body.Left + Theme.P(14), body.Top + Theme.P(14)), new PointF(body.Right - Theme.P(14), body.Top + Theme.P(14)),
                                           new PointF(body.Left + Theme.P(14), body.Bottom - Theme.P(14)), new PointF(body.Right - Theme.P(14), body.Bottom - Theme.P(14)) })
                    using (var b = new SolidBrush(Theme.Field)) g.FillEllipse(b, pt.X - sr, pt.Y - sr, sr * 2, sr * 2);

                // Plaka zemini
                float rr = R1 + Theme.P(6);
                using (var b = new SolidBrush(Theme.Mix(Theme.Bg, Color.Black, 0.25f))) g.FillEllipse(b, c.X - rr, c.Y - rr, rr * 2, rr * 2);
                using (var pen = new Pen(Theme.Border, Theme.P(1.5f))) g.DrawEllipse(pen, c.X - rr, c.Y - rr, rr * 2, rr * 2);

                if (vs.Map != null)
                {
                    float gapDeg = 0.9f, gapPx = Math.Max(1f, Theme.P(1.2f));
                    using (var stripes = ViewState.Stripes())
                        for (int i = 0; i < Rings * Segs; i++)
                        {
                            bool hatch;
                            var col = vs.CellColor(CellStart(i), CellStart(i + 1), out hatch);
                            using (var p = CellPath(i, gapDeg, gapPx))
                            {
                                using (var b = new SolidBrush(col)) g.FillPath(b, p);
                                if (hatch) g.FillPath(stripes, p);
                            }
                        }
                }

                // Mil (ortadaki göbek)
                float hub = R0 - Theme.P(4);
                using (var b = new SolidBrush(Theme.Card)) g.FillEllipse(b, c.X - hub, c.Y - hub, hub * 2, hub * 2);
                using (var pen = new Pen(Theme.Border, Theme.P(1.5f))) g.DrawEllipse(pen, c.X - hub, c.Y - hub, hub * 2, hub * 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            RenderCache();
            g.DrawImageUnscaled(cache, 0, 0);
            var c = Center;

            // Seçim
            if (vs.HasSel)
            {
                int a = CellOf(vs.SelA), b = CellOf(Math.Max(vs.SelA, vs.SelB - 1));
                using (var fill = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                using (var pen = new Pen(Color.White, Theme.P(1.2f)))
                    for (int i = a; i <= b; i++)
                        using (var p = CellPath(i, 0.9f, Theme.P(1.2f)))
                        {
                            g.FillPath(fill, p);
                            g.DrawPath(pen, p);
                        }
            }

            // Okuma kafası ve kolu
            float side = Side;
            var pivot = new PointF(c.X + side * 0.40f, c.Y + side * 0.38f);
            PointF head = vs.Head >= 0 && vs.Map != null ? CellPoint(vs.Head) : new PointF(c.X + R1 * 0.78f, c.Y - R1 * 0.95f);
            using (var pen = new Pen(Theme.Mix(Theme.Sub, Theme.Bg, 0.2f), Theme.P(5)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLine(pen, pivot, head);
            }
            float hs = Theme.P(7);
            Theme.Fill(g, new RectangleF(head.X - hs, head.Y - hs, hs * 2, hs * 2), Theme.P(3), vs.Head >= 0 ? Theme.Accent : Theme.Sub);
            float pr = Theme.P(14);
            using (var b = new SolidBrush(Theme.Field)) g.FillEllipse(b, pivot.X - pr, pivot.Y - pr, pr * 2, pr * 2);
            using (var pen = new Pen(Theme.Border, Theme.P(1.5f))) g.DrawEllipse(pen, pivot.X - pr, pivot.Y - pr, pr * 2, pr * 2);

            // Ortadaki yazı: fare üzerindeyse o konum, değilse kullanılabilir alan
            float hub = R0 - Theme.P(10);
            var tr = new RectangleF(c.X - hub, c.Y - hub, hub * 2, hub * 2);
            using (var big = new Font(Font.FontFamily, Font.Size + 3f, FontStyle.Bold))
            using (var small = new Font(Font.FontFamily, Font.Size - 1.5f))
            {
                string top, bottom;
                Color topCol = Theme.Text;
                if (vs.Map == null) { top = "—"; bottom = L.T("disk seç", "pick a disk"); }
                else if (hoverOff >= 0)
                {
                    long cs = CellStart(CellOf(hoverOff)), ce = CellStart(CellOf(hoverOff) + 1);
                    var st = vs.Map.Worst(cs, ce);
                    top = Fmt.GB(hoverOff);
                    bottom = vs.IsMarked(cs, ce) ? L.T("Bozuk (işaretli)", "Bad (marked)") : ViewState.StateName(st);
                    if (st == BlockState.Bad || vs.IsMarked(cs, ce)) topCol = Theme.Danger;
                }
                else if (vs.Plan != null)
                {
                    top = Fmt.Bytes(vs.Plan.UsableBytes);
                    bottom = L.T("kullanılabilir", "usable");
                    topCol = vs.Plan.UsableBytes > 0 ? Theme.Accent : Theme.Danger;
                }
                else { top = Fmt.Bytes(vs.Map.Size); bottom = ""; }
                var h1 = g.MeasureString(top, big).Height;
                Theme.Draw(g, top, big, new RectangleF(tr.X - Theme.P(20), c.Y - h1 + Theme.P(2), tr.Width + Theme.P(40), h1), topCol, StringAlignment.Center, StringAlignment.Far, false);
                Theme.Draw(g, bottom, small, new RectangleF(tr.X - Theme.P(10), c.Y + Theme.P(2), tr.Width + Theme.P(20), h1), Theme.Sub, StringAlignment.Center, StringAlignment.Near, false);
            }

            // Başlangıç işareti (dış halkanın tepesi = diskin başı)
            using (var small = new Font(Font.FontFamily, Font.Size - 2f))
            {
                float y = c.Y - R1 - Theme.P(18);
                Theme.Draw(g, L.T("▼ başlangıç (0 GB)", "▼ start (0 GB)"), small, new RectangleF(c.X - Theme.P(80), y, Theme.P(160), Theme.P(14)), Theme.Sub, StringAlignment.Center, StringAlignment.Center, false);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            long off = OffsetAt(e.Location, false);
            if (dragFrom >= 0)
            {
                if (!dragging && (Math.Abs(e.X - downAt.X) > 3 || Math.Abs(e.Y - downAt.Y) > 3)) dragging = true;
                if (dragging)
                {
                    long cur = OffsetAt(e.Location, true);
                    vs.SetSel(Math.Min(dragFrom, cur), Math.Max(dragFrom, cur));
                    hoverOff = cur;
                    Invalidate();
                    return;
                }
            }
            if (off != hoverOff) { hoverOff = off; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hoverOff = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || vs.Map == null) return;
            dragFrom = OffsetAt(e.Location, false);
            downAt = e.Location;
            dragging = false;
            if (dragFrom < 0) { vs.ClearSel(); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragFrom >= 0 && !dragging)
            {
                // Tek tıklama: o hücre seçilir
                int i = CellOf(dragFrom);
                vs.SetSel(CellStart(i), CellStart(i + 1));
            }
            dragFrom = -1;
            dragging = false;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && cache != null) cache.Dispose();
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ Doğrusal şerit (diskin baştan sona düz hali)
    class StripView : Ctl
    {
        readonly ViewState vs;
        long dragFrom = -1;
        long hoverOff = -1;

        public StripView(ViewState vs) { this.vs = vs; Cursor = Cursors.IBeam; }

        float BarL { get { return Theme.P(2); } }
        float BarR { get { return Width - Theme.P(2); } }
        float BarH { get { return Height - Theme.P(22); } }

        long OffAt(int x)
        {
            if (vs.Map == null) return -1;
            double t = (x - BarL) / Math.Max(1.0, BarR - BarL);
            return (long)(vs.Map.Size * Math.Max(0, Math.Min(1, t)));
        }

        float XOf(long off) { return vs.Map == null ? BarL : BarL + (float)(off / (double)vs.Map.Size) * (BarR - BarL); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var bar = new RectangleF(BarL, 0, BarR - BarL, BarH);
            Theme.Fill(g, bar, Theme.P(6), Theme.Field);
            if (vs.Map != null)
            {
                var st = g.Save();
                using (var clip = Theme.Round(bar, Theme.P(6))) g.SetClip(clip);
                g.SmoothingMode = SmoothingMode.None;
                int w = (int)bar.Width;
                using (var stripes = ViewState.Stripes())
                    for (int x = 0; x < w; x++)
                    {
                        long a = (long)(vs.Map.Size * (double)x / w), b = (long)(vs.Map.Size * (double)(x + 1) / w);
                        bool hatch;
                        var col = vs.CellColor(a, b, out hatch);
                        using (var br = new SolidBrush(col)) g.FillRectangle(br, bar.X + x, 0, 1, bar.Height);
                    }
                if (vs.Plan != null)
                    using (var stripes = ViewState.Stripes())
                        foreach (var s in vs.Plan.Excluded)
                        {
                            float x0 = XOf(s.Start), x1 = Math.Max(x0 + 1, XOf(s.End));
                            g.FillRectangle(stripes, x0, 0, x1 - x0, bar.Height);
                        }
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (vs.HasSel)
                {
                    float x0 = XOf(vs.SelA), x1 = Math.Max(x0 + Theme.P(2), XOf(vs.SelB));
                    using (var b = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) g.FillRectangle(b, x0, 0, x1 - x0, bar.Height);
                    using (var p = new Pen(Color.White, Theme.P(1.5f))) g.DrawRectangle(p, x0, 0.5f, x1 - x0, bar.Height - 1);
                }
                if (vs.Head >= 0)
                    using (var p = new Pen(Theme.Accent, Theme.P(2))) g.DrawLine(p, XOf(vs.Head), 0, XOf(vs.Head), bar.Height);
                g.Restore(st);

                // Ölçek
                using (var small = new Font(Font.FontFamily, Font.Size - 2f))
                    for (int i = 0; i <= 4; i++)
                    {
                        long off = vs.Map.Size * i / 4;
                        float x = XOf(off);
                        var al = i == 0 ? StringAlignment.Near : i == 4 ? StringAlignment.Far : StringAlignment.Center;
                        float bw = Theme.P(90);
                        float bx = i == 0 ? x : i == 4 ? x - bw : x - bw / 2;
                        Theme.Draw(g, Fmt.Bytes(off).Replace(",0 ", " ").Replace(".0 ", " "), small, new RectangleF(bx, bar.Bottom + Theme.P(4), bw, Theme.P(16)), Theme.Sub, al, StringAlignment.Near, false);
                    }
                if (hoverOff >= 0)
                    using (var p = new Pen(Color.FromArgb(160, 255, 255, 255), 1)) g.DrawLine(p, XOf(hoverOff), 0, XOf(hoverOff), bar.Height);
            }
            Theme.Stroke(g, bar, Theme.P(6), Theme.Border, 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || vs.Map == null) return;
            dragFrom = OffAt(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (vs.Map == null) return;
            hoverOff = OffAt(e.X);
            if (dragFrom >= 0) vs.SetSel(Math.Min(dragFrom, hoverOff), Math.Max(dragFrom, hoverOff));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragFrom >= 0 && vs.Map != null && Math.Abs(OffAt(e.X) - dragFrom) < vs.Map.Size / 2000)
            {
                long cell = vs.Map.Size / (PlatterView.Rings * PlatterView.Segs);
                vs.SetSel(dragFrom - cell / 2, dragFrom + cell / 2);
            }
            dragFrom = -1;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { hoverOff = -1; base.OnMouseLeave(e); }

        public string HoverText()
        {
            if (hoverOff < 0 || vs.Map == null) return "";
            return Fmt.GB(hoverOff);
        }
    }

    // ------------------------------------------------------------------ Satır listesi (bozuk bölgeler / bölümler)
    class RowList : Ctl
    {
        public class Row
        {
            public Color Dot;
            public string Left = "", Mid = "", Right = "";
            public bool Removable;
            public object Tag;
        }

        public readonly List<Row> Rows = new List<Row>();
        public string Empty = "";
        public int MaxRows = 8;
        public int MidW = 90;
        public event Action<object> Removed;
        public event Action<object> Picked;
        int hot = -1;

        public RowList() { Cursor = Cursors.Default; }

        int RowH { get { return Theme.P(30); } }
        int Shown { get { return Math.Min(Rows.Count, MaxRows); } }

        public int Measure()
        {
            int n = Math.Max(1, Shown) + (Rows.Count > MaxRows ? 1 : 0);
            return n * RowH + Theme.P(8);
        }

        RectangleF XRect(int i) { return new RectangleF(Width - Theme.P(30), Theme.P(4) + i * RowH + Theme.P(5), Theme.P(22), RowH - Theme.P(10)); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = (e.Y - Theme.P(4)) / RowH;
            if (h < 0 || h >= Shown) h = -1;
            if (h != hot) { hot = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = (e.Y - Theme.P(4)) / RowH;
            if (i < 0 || i >= Shown) return;
            if (Rows[i].Removable && XRect(i).Contains(e.Location))
            {
                var h = Removed;
                if (h != null) h(Rows[i].Tag);
            }
            else
            {
                var h = Picked;
                if (h != null) h(Rows[i].Tag);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Glass.Card(g, r, Theme.P(10), Color.Empty);
            if (Rows.Count == 0)
            {
                Theme.Draw(g, Empty, Font, new RectangleF(Theme.P(14), 0, Width - Theme.P(28), Height), Theme.Sub, StringAlignment.Near, StringAlignment.Center, false);
                return;
            }
            using (var small = new Font(Font.FontFamily, Font.Size - 1f))
            using (var bold = new Font(Font, FontStyle.Bold))
            using (var stripes = ViewState.Stripes())
            {
                for (int i = 0; i < Shown; i++)
                {
                    var row = Rows[i];
                    float y = Theme.P(4) + i * RowH;
                    if (i == hot) Theme.Fill(g, new RectangleF(Theme.P(4), y, Width - Theme.P(8), RowH), Theme.P(7), Theme.Field);
                    float d = Theme.P(12);
                    var dr = new RectangleF(Theme.P(14), y + (RowH - d) / 2, d, d);
                    Theme.Fill(g, dr, Theme.P(3), row.Dot);
                    float x = Theme.P(36);
                    float rightW = Theme.P(row.Removable ? 118 : 92);
                    float mw = Theme.P(MidW);
                    Theme.Draw(g, row.Left, bold, new RectangleF(x, y, Width - x - rightW - mw, RowH), Theme.Text, StringAlignment.Near, StringAlignment.Center, false);
                    Theme.Draw(g, row.Mid, small, new RectangleF(Width - rightW - mw, y, mw - Theme.P(4), RowH), Theme.Sub, StringAlignment.Far, StringAlignment.Center, false);
                    Theme.Draw(g, row.Right, small, new RectangleF(Width - rightW, y, rightW - Theme.P(row.Removable ? 36 : 12), RowH), Theme.Sub, StringAlignment.Far, StringAlignment.Center, false);
                    if (row.Removable)
                    {
                        var xr = XRect(i);
                        bool xh = i == hot;
                        Theme.Draw(g, "✕", Font, xr, xh ? Theme.Danger : Theme.Sub, StringAlignment.Center, StringAlignment.Center, false);
                    }
                }
                if (Rows.Count > MaxRows)
                {
                    float y = Theme.P(4) + Shown * RowH;
                    Theme.Draw(g, "+" + (Rows.Count - MaxRows) + L.T(" bölge daha", " more"), small, new RectangleF(Theme.P(36), y, Width - Theme.P(50), RowH), Theme.Sub, StringAlignment.Near, StringAlignment.Center, false);
                }
            }
        }
    }

    // ------------------------------------------------------------------ Renk açıklaması
    class Legend : Ctl
    {
        public Legend() { Cursor = Cursors.Default; }

        static readonly object[][] Items =
        {
            new object[] { "ok" }, new object[] { "slow" }, new object[] { "vslow" }, new object[] { "bad" }, new object[] { "pad" }, new object[] { "none" },
        };

        public int Measure(int width)
        {
            int cols = width >= Theme.P(420) ? 3 : 2;
            int rows = (Items.Length + cols - 1) / cols;
            return rows * Theme.P(26) + Theme.P(4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            int cols = Width >= Theme.P(420) ? 3 : 2;
            float cw = Width / (float)cols;
            using (var stripes = ViewState.Stripes())
                for (int i = 0; i < Items.Length; i++)
                {
                    string k = (string)Items[i][0];
                    float x = (i % cols) * cw, y = (i / cols) * Theme.P(26);
                    Color c;
                    string t;
                    bool hatch = false;
                    switch (k)
                    {
                        case "ok": c = Theme.Mix(Theme.Accent, Theme.Bg, 0.38f); t = L.T("Sağlam", "Healthy"); break;
                        case "slow": c = Theme.Warn; t = L.T("Yavaş", "Slow"); break;
                        case "vslow": c = Color.FromArgb(240, 128, 52); t = L.T("Çok yavaş", "Very slow"); break;
                        case "bad": c = Theme.Danger; t = L.T("Bozuk", "Bad"); break;
                        case "pad": c = Theme.Mix(Theme.Accent, Theme.Bg, 0.62f); hatch = true; t = L.T("Güvenlik payı", "Safety margin"); break;
                        default: c = Theme.Field; t = L.T("Taranmadı", "Not scanned"); break;
                    }
                    float d = Theme.P(14);
                    var r = new RectangleF(x, y + Theme.P(5), d, d);
                    Theme.Fill(g, r, Theme.P(3), c);
                    if (hatch) using (var p = Theme.Round(r, Theme.P(3))) g.FillPath(stripes, p);
                    Theme.Draw(g, t, Font, new RectangleF(x + d + Theme.P(8), y, cw - d - Theme.P(10), Theme.P(24)), Theme.Sub, StringAlignment.Near, StringAlignment.Center, false);
                }
        }
    }
    // ------------------------------------------------------------------ Sağlık kartı (S.M.A.R.T.)
    class HealthCard : Ctl
    {
        public SmartInfo Info;
        public bool Loading;

        public HealthCard() { Cursor = Cursors.Default; }

        int Cols { get { return Width >= Theme.P(600) ? 4 : 2; } }

        public int Measure(int width)
        {
            if (Info == null || !Info.Ok) return Theme.P(64);
            int cols = width >= Theme.P(600) ? 4 : 2;
            int rows = 4 / cols;
            return rows * Theme.P(78) + Theme.P(54);
        }

        public static string Verdict(SmartInfo s)
        {
            if (s == null || !s.Ok) return "";
            long grow = s.First == null ? 0 :
                SmartInfo.Delta(s.Reallocated, s.First.Reallocated) + SmartInfo.Delta(s.Pending, s.First.Pending) + SmartInfo.Delta(s.Offline, s.First.Offline);
            if (grow > 0) return L.T("Bozuk sektör sayısı ilk kontrolden beri arttı: arıza ilerliyor. Önemli veri koyma.", "Bad sector counts grew since the first check: the failure is progressing. Don't store anything important.");
            if (s.Pending > 0 || s.Offline > 0) return L.T("Disk okunamayan sektörler bildiriyor. Sayılar artarsa diski emekliye ayır.", "The disk reports unreadable sectors. Retire it if the numbers grow.");
            if (s.Reallocated > 0) return L.T("Bazı sektörler yedek alana taşınmış. Sayıyı ara ara kontrol et.", "Some sectors were moved to the spare area. Check the count now and then.");
            return L.T("Diskin kendi sayaçlarında sorun görünmüyor.", "The disk's own counters show no problems.");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Glass.Card(g, r, Theme.P(10), Color.Empty);
            float pad = Theme.P(14);
            if (Loading || Info == null || !Info.Ok)
            {
                string t = Loading ? L.T("Sağlık bilgisi okunuyor…", "Reading health data…")
                                   : L.T("Diskin SMART sayaçları okunamadı (NVMe, USB kutusu ya da sürücü desteklemiyor). CrystalDiskInfo ile bakabilirsin.",
                                         "Couldn't read the disk's SMART counters (NVMe, USB enclosure or an unsupported driver). CrystalDiskInfo can show them.");
                Theme.Draw(g, t, Font, new RectangleF(pad, 0, Width - pad * 2, Height), Theme.Sub, StringAlignment.Near, StringAlignment.Center, true);
                return;
            }
            var s = Info;
            var f = s.First;
            int cols = Cols;
            float cw = (Width - pad * 2 - Theme.P(8) * (cols - 1)) / cols, ch = Theme.P(70);
            var tiles = new[]
            {
                new object[] { L.T("Yeniden atanmış", "Reallocated"), s.Reallocated, f == null ? -1 : f.Reallocated },
                new object[] { L.T("Bekleyen", "Pending"), s.Pending, f == null ? -1 : f.Pending },
                new object[] { L.T("Düzeltilemeyen", "Uncorrectable"), s.Offline, f == null ? -1 : f.Offline },
                new object[] { "temp", s.Temperature, s.Hours },
            };
            using (var small = new Font(Font.FontFamily, Font.Size - 1.5f))
            using (var big = new Font(Font.FontFamily, Font.Size + 5f, FontStyle.Bold))
                for (int i = 0; i < 4; i++)
                {
                    float x = pad + (i % cols) * (cw + Theme.P(8)), y = pad + (i / cols) * (ch + Theme.P(8));
                    var tr = new RectangleF(x, y, cw, ch);
                    Theme.Fill(g, tr, Theme.P(8), Theme.Field);
                    string label = (string)tiles[i][0];
                    long v = (long)tiles[i][1], prev = (long)tiles[i][2];
                    string value, sub;
                    Color vc = Theme.Text, sc = Theme.Sub;
                    if (label == "temp")
                    {
                        label = L.T("Sıcaklık · çalışma", "Temperature · hours");
                        value = v >= 0 ? v + " °C" : "—";
                        if (v >= 50) vc = Theme.Warn;
                        sub = prev >= 0 ? Fmt.N(prev, "#,0") + L.T(" saat", " h") + "  (" + Fmt.N(prev / 24.0 / 365.0, "0.0") + L.T(" yıl)", " yrs)") : "";
                    }
                    else
                    {
                        value = v >= 0 ? Fmt.N(v, "#,0") : "—";
                        if (v > 0) vc = i == 0 ? Theme.Warn : Theme.Danger;
                        long d = SmartInfo.Delta(v, prev);
                        if (f == null) sub = L.T("ilk kontrol", "first check");
                        else if (d > 0) { sub = "+" + Fmt.N(d, "#,0") + L.T(" ilk kontrolden beri", " since first check"); sc = Theme.Danger; }
                        else sub = L.T("değişmedi · ", "unchanged · ") + s.FirstWhen.ToString("d MMM", L.Lang == "tr" ? new System.Globalization.CultureInfo("tr-TR") : System.Globalization.CultureInfo.InvariantCulture);
                    }
                    Theme.Draw(g, label, small, new RectangleF(x + Theme.P(10), y + Theme.P(6), cw - Theme.P(16), Theme.P(16)), Theme.Sub, StringAlignment.Near, StringAlignment.Near, false);
                    Theme.Draw(g, value, big, new RectangleF(x + Theme.P(10), y + Theme.P(20), cw - Theme.P(16), Theme.P(28)), vc, StringAlignment.Near, StringAlignment.Center, false);
                    Theme.Draw(g, sub, small, new RectangleF(x + Theme.P(10), y + ch - Theme.P(22), cw - Theme.P(16), Theme.P(18)), sc, StringAlignment.Near, StringAlignment.Center, false);
                }
            int rows = 4 / cols;
            float vy = pad + rows * (ch + Theme.P(8)) - Theme.P(2);
            string verdict = Verdict(s);
            bool bad = s.Pending > 0 || s.Offline > 0 || (s.First != null && verdict.Length > 0 && (s.Reallocated > s.First.Reallocated || s.Pending > s.First.Pending));
            Theme.Draw(g, verdict, Font, new RectangleF(pad, vy, Width - pad * 2, Height - vy - Theme.P(6)), bad ? Theme.Danger : Theme.Sub, StringAlignment.Near, StringAlignment.Center, true);
        }
    }
}
