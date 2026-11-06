using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BrokenDiskResizer
{
    /// <summary>Altındakini çizebilen yüzey (pencere zemini, cam sayfa). r: yüzeyin kendi koordinatlarında.</summary>
    interface ISurface { void PaintSurface(Graphics g, Rectangle r); }

    /// <summary>Telefon uygulamasıyla aynı görünüm: koyu zemin, arkada yumuşak renk lekeleri, yarı saydam cam kartlar.</summary>
    static class Glass
    {
        public static readonly Color Bg = Color.FromArgb(12, 14, 19);
        public static readonly Color Line = Color.FromArgb(43, 255, 255, 255);
        public static readonly Color AccentHi = Color.FromArgb(95, 224, 205);
        public static readonly Color Muted = Color.FromArgb(138, 147, 163);

        // pencere boyutuna göre önbellek (ana pencere ve açık iletişim kutusu aynı anda)
        static readonly Dictionary<Size, Bitmap> cache = new Dictionary<Size, Bitmap>();
        static readonly List<Size> order = new List<Size>();

        static Bitmap Make(Size s)
        {
            // lekeler yumuşak olduğu için küçük çizilip büyütülür (hızlı)
            int sw = Math.Max(8, s.Width / 4), sh = Math.Max(8, s.Height / 4);
            var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.Clear(Bg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float m = Math.Max(sw, sh);
                Blob(g, sw * 0.10f, sh * 0.08f, m * 0.62f, Color.FromArgb(0x66, 0x2E, 0xC4, 0xB0));
                Blob(g, sw * 0.95f, sh * 0.38f, m * 0.55f, Color.FromArgb(0x55, 0x6C, 0x5C, 0xE7));
                Blob(g, sw * 0.20f, sh * 0.92f, m * 0.60f, Color.FromArgb(0x44, 0x2F, 0x80, 0xED));
            }
            var big = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);   // kenarlarda koyu çizgi kalmasın
                    g.DrawImage(small, new Rectangle(0, 0, s.Width, s.Height), 0, 0, sw, sh, GraphicsUnit.Pixel, ia);
                }
            }
            small.Dispose();
            return big;
        }

        static void Blob(Graphics g, float x, float y, float r, Color c)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x - r, y - r, r * 2, r * 2);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterPoint = new PointF(x, y);
                    b.CenterColor = c;
                    b.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillPath(b, path);
                }
            }
        }

        /// <summary>Pencere zemininin bir parçasını çizer. origin: denetimin pencere içindeki yeri; r: denetimin koordinatlarında.</summary>
        public static void Backdrop(Graphics g, Size client, Point origin, Rectangle r)
        {
            if (client.Width < 1 || client.Height < 1) { using (var b = new SolidBrush(Bg)) g.FillRectangle(b, r); return; }
            Bitmap bmp;
            if (!cache.TryGetValue(client, out bmp))
            {
                bmp = Make(client);
                cache[client] = bmp;
                order.Add(client);
                if (order.Count > 3) { cache[order[0]].Dispose(); cache.Remove(order[0]); order.RemoveAt(0); }
            }
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(bmp, r, new Rectangle(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height), GraphicsUnit.Pixel);
            g.Restore(st);
        }

        /// <summary>Cam kart: sol üstte biraz daha açık, ince ışık çizgisi.</summary>
        public static void Card(Graphics g, RectangleF r, float rad, Color tint)
        {
            Color top = Color.FromArgb(38, 255, 255, 255), bottom = Color.FromArgb(10, 255, 255, 255), line = Line;
            if (tint != Color.Empty)
            {
                top = Color.FromArgb(56, tint);
                bottom = Color.FromArgb(22, tint);
                line = Color.FromArgb(90, tint);
            }
            using (var p = Theme.Round(r, rad))
            {
                using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), top, bottom, 45f))
                    g.FillPath(b, p);
                using (var pen = new Pen(line, 1)) g.DrawPath(pen, p);
            }
        }

        public static void Field(Graphics g, RectangleF r, float rad, bool hover)
        {
            using (var p = Theme.Round(r, rad))
            {
                using (var b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2),
                    Color.FromArgb(hover ? 34 : 20, 255, 255, 255), Color.FromArgb(hover ? 16 : 8, 255, 255, 255), 45f))
                    g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(hover ? 52 : 36, 255, 255, 255), 1)) g.DrawPath(pen, p);
            }
        }

        /// <summary>Zemin rengindeki düz yazı ve resimleri (alt denetimleriyle) saydam yapar.</summary>
        public static void SeeThrough(Control c)
        {
            if ((c is Label || c is PictureBox) && c.BackColor.A == 255 && c.BackColor.ToArgb() == Theme.Bg.ToArgb())
                c.BackColor = Color.Transparent;
            foreach (Control k in c.Controls) SeeThrough(k);
        }

        /// <summary>Denetimin altında kalanı (zemin + kartlar) çizer. Yüzey yoksa false.</summary>
        public static bool PaintUnder(Graphics g, Control c)
        {
            for (Control p = c.Parent; p != null; p = p.Parent)
            {
                var s = p as ISurface;
                if (s == null) continue;
                Point off = p.PointToClient(c.PointToScreen(Point.Empty));
                var st = g.Save();
                g.TranslateTransform(-off.X, -off.Y);
                s.PaintSurface(g, new Rectangle(off, c.Size));
                g.Restore(st);
                return true;
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ Cam sayfa
    class GlassCard
    {
        public Rectangle R;
        public Color Tint = Color.Empty;
        public GlassCard(Rectangle r, Color tint) { R = r; Tint = tint; }
    }

    /// <summary>
    /// Kaydırılan sayfa. Zemin pencereye sabittir, kartlar ve denetimler kayar. Kaydırma kendi yapılır
    /// (Windows'un kaydırması zemini de kaydırırdı); sağda ince cam kaydırma çubuğu.
    /// </summary>
    class GlassPage : Panel, ISurface
    {
        public readonly List<GlassCard> Cards = new List<GlassCard>();
        public int ScrollY, ContentH;
        public event Action Scrolled;
        bool thumbHover, drag;
        int dragFrom, dragScroll;

        public GlassPage()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Glass.Bg;
        }

        public int MaxScroll { get { return Math.Max(0, ContentH - ClientSize.Height); } }

        public void ScrollTo(int y)
        {
            y = Math.Max(0, Math.Min(MaxScroll, y));
            if (y == ScrollY) return;
            ScrollY = y;
            var h = Scrolled;
            if (h != null) h();
            Invalidate(true);
        }

        public void PaintSurface(Graphics g, Rectangle r)
        {
            var f = FindForm();
            Glass.Backdrop(g, f == null ? ClientSize : f.ClientSize, f == null ? Point.Empty : f.PointToClient(PointToScreen(Point.Empty)), r);
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            foreach (var c in Cards)
                if (c.R.IntersectsWith(r)) Glass.Card(g, new RectangleF(c.R.X + 0.5f, c.R.Y + 0.5f, c.R.Width - 1, c.R.Height - 1), Theme.P(22), c.Tint);
            g.Restore(st);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { PaintSurface(e.Graphics, e.ClipRectangle); }

        Rectangle Thumb
        {
            get
            {
                int H = ClientSize.Height;
                if (MaxScroll <= 0 || H <= 0) return Rectangle.Empty;
                int th = Math.Max(Theme.P(40), H * H / Math.Max(1, ContentH));
                int ty = (int)((H - th - Theme.P(8)) * (ScrollY / (float)MaxScroll)) + Theme.P(4);
                int w = Theme.P(thumbHover || drag ? 8 : 6);
                return new Rectangle(ClientSize.Width - w - Theme.P(4), ty, w, th);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Thumb;
            if (t.IsEmpty) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Fill(e.Graphics, t, t.Width / 2f, Color.FromArgb(drag ? 130 : thumbHover ? 100 : 60, 255, 255, 255));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollTo(ScrollY - e.Delta * Theme.P(60) / 120);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var t = Thumb;
            if (!t.IsEmpty && e.X >= t.X - Theme.P(6))
            {
                drag = true;
                dragFrom = e.Y;
                dragScroll = ScrollY;
                if (e.Y < t.Y || e.Y > t.Bottom) ScrollTo(ScrollY + (e.Y < t.Y ? -1 : 1) * ClientSize.Height * 8 / 10);
                dragScroll = ScrollY;
                Capture = true;
                Invalidate(new Rectangle(ClientSize.Width - Theme.P(20), 0, Theme.P(20), ClientSize.Height));
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag)
            {
                var t = Thumb;
                int range = ClientSize.Height - t.Height - Theme.P(8);
                if (range > 0) ScrollTo(dragScroll + (int)((e.Y - dragFrom) * (MaxScroll / (float)range)));
            }
            else
            {
                bool h = MaxScroll > 0 && e.X >= ClientSize.Width - Theme.P(18);
                if (h != thumbHover) { thumbHover = h; Invalidate(new Rectangle(ClientSize.Width - Theme.P(20), 0, Theme.P(20), ClientSize.Height)); }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (drag) { drag = false; Capture = false; Invalidate(new Rectangle(ClientSize.Width - Theme.P(20), 0, Theme.P(20), ClientSize.Height)); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (thumbHover && !drag) { thumbHover = false; Invalidate(); }
            base.OnMouseLeave(e);
        }
    }

    // ------------------------------------------------------------------ Simgeler (24x24 çizim alanında)
    static class Icons
    {
        public const string Mic = "mic", Scan = "scan", Tune = "tune", Gear = "gear", Shield = "shield", Check = "check";

        public static void Draw(Graphics g, string kind, RectangleF box, Color c)
        {
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(box.Width, box.Height) / 24f;
            g.TranslateTransform(box.X + (box.Width - 24 * s) / 2, box.Y + (box.Height - 24 * s) / 2);
            g.ScaleTransform(s, s);
            using (var b = new SolidBrush(c))
            using (var pen = new Pen(c, 2f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case Mic:
                        using (var p = Theme.Round(new RectangleF(8.6f, 2f, 6.8f, 12.2f), 3.4f)) g.FillPath(b, p);
                        g.DrawArc(pen, 5.6f, 4.6f, 12.8f, 12.8f, 0, 180);
                        g.DrawLine(pen, 12, 17.6f, 12, 21);
                        break;
                    case Scan:
                        g.DrawLines(pen, new[] { new PointF(4, 9), new PointF(4, 4), new PointF(9, 4) });
                        g.DrawLines(pen, new[] { new PointF(15, 4), new PointF(20, 4), new PointF(20, 9) });
                        g.DrawLines(pen, new[] { new PointF(4, 15), new PointF(4, 20), new PointF(9, 20) });
                        g.DrawLines(pen, new[] { new PointF(20, 15), new PointF(20, 20), new PointF(15, 20) });
                        g.DrawLine(pen, 7.5f, 12, 16.5f, 12);
                        break;
                    case Tune:
                        g.DrawLine(pen, 3.5f, 6, 20.5f, 6);
                        g.DrawLine(pen, 3.5f, 12, 20.5f, 12);
                        g.DrawLine(pen, 3.5f, 18, 20.5f, 18);
                        foreach (var k in new[] { new PointF(15, 6), new PointF(8.5f, 12), new PointF(16, 18) })
                            g.FillEllipse(b, k.X - 2.8f, k.Y - 2.8f, 5.6f, 5.6f);
                        break;
                    case Gear:
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(4.6f, 4.6f, 14.8f, 14.8f);
                            using (var region = new Region(path))
                            {
                                for (int i = 0; i < 8; i++)
                                    using (var tooth = new GraphicsPath())
                                    {
                                        tooth.AddRectangle(new RectangleF(10.2f, 1.6f, 3.6f, 5f));
                                        using (var m = new Matrix()) { m.RotateAt(i * 45, new PointF(12, 12)); tooth.Transform(m); }
                                        region.Union(tooth);
                                    }
                                using (var hole = new GraphicsPath()) { hole.AddEllipse(8.6f, 8.6f, 6.8f, 6.8f); region.Exclude(hole); }
                                g.FillRegion(b, region);
                            }
                        }
                        break;
                    case Shield:
                        using (var p = new GraphicsPath())
                        {
                            p.AddLines(new[] { new PointF(12, 2.5f), new PointF(20, 5.5f), new PointF(20, 11) });
                            p.AddBezier(20, 11, 20, 16, 16.5f, 19.8f, 12, 21.5f);
                            p.AddBezier(12, 21.5f, 7.5f, 19.8f, 4, 16, 4, 11);
                            p.AddLines(new[] { new PointF(4, 11), new PointF(4, 5.5f) });
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        break;
                    case Check:
                        g.DrawLines(pen, new[] { new PointF(5, 12.5f), new PointF(10, 17.5f), new PointF(19, 7) });
                        break;
                }
            }
            g.Restore(st);
        }
    }

    // ------------------------------------------------------------------ Alt sekme çubuğu
    class TabBar : Ctl
    {
        public string[] Labels = new string[0], IconKinds = new string[0];
        int selected, hot = -1;
        public event Action<int> Changed;

        public TabBar() { }

        public int Selected { get { return selected; } set { selected = value; Invalidate(); } }

        RectangleF Pill
        {
            get
            {
                float w = Math.Min(Width - Theme.P(28), Theme.P(520));
                return new RectangleF((Width - w) / 2f, Theme.P(4), w, Height - Theme.P(14));
            }
        }

        RectangleF Item(int i)
        {
            var p = Pill;
            float iw = (p.Width - Theme.P(12)) / Math.Max(1, Labels.Length);
            return new RectangleF(p.X + Theme.P(6) + i * iw, p.Y + Theme.P(6), iw, p.Height - Theme.P(12));
        }

        int Hit(Point pt) { for (int i = 0; i < Labels.Length; i++) if (Item(i).Contains(pt)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = Hit(e.Location);
            if (i < 0 || i == selected) return;
            selected = i;
            Invalidate();
            var h = Changed;
            if (h != null) h(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var p = Pill;
            using (var path = Theme.Round(p, Theme.P(30)))
            {
                using (var b = new LinearGradientBrush(new RectangleF(p.X - 1, p.Y - 1, p.Width + 2, p.Height + 2),
                    Color.FromArgb(0xF0, 0x1D, 0x22, 0x2B), Color.FromArgb(0xF0, 0x15, 0x19, 0x20), 45f))
                    g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(0x33, 255, 255, 255), 1)) g.DrawPath(pen, path);
            }
            using (var f = new Font(Font.FontFamily, 8f, FontStyle.Bold))
                for (int i = 0; i < Labels.Length; i++)
                {
                    var r = Item(i);
                    bool on = i == selected, h = i == hot;
                    float iw = Theme.P(54), ih = Theme.P(32);
                    var ib = new RectangleF(r.X + (r.Width - iw) / 2, r.Y + Theme.P(2), iw, ih);
                    if (on) Theme.Fill(g, ib, Theme.P(16), Color.FromArgb(0x33, 0x2E, 0xC4, 0xB0));
                    else if (h) Theme.Fill(g, ib, Theme.P(16), Color.FromArgb(0x14, 255, 255, 255));
                    Color c = on ? Glass.AccentHi : h ? Theme.Text : Glass.Muted;
                    Icons.Draw(g, i < IconKinds.Length ? IconKinds[i] : "", new RectangleF(ib.X + (iw - Theme.P(22)) / 2, ib.Y + (ih - Theme.P(22)) / 2, Theme.P(22), Theme.P(22)), c);
                    Theme.Draw(g, Labels[i], f, new RectangleF(r.X, ib.Bottom + Theme.P(1), r.Width, f.Height + Theme.P(2)), on ? Theme.Text : c,
                        StringAlignment.Center, StringAlignment.Near, false);
                }
        }
    }

    // ------------------------------------------------------------------ Büyük mikrofon simgesi
    enum OrbMode { Idle, Waiting, Live, Muted, Warn }

    class MicOrb : Ctl
    {
        public OrbMode Mode;
        float level, shown;

        public MicOrb() { Cursor = Cursors.Default; }

        public void Push(float v)
        {
            level = Math.Max(0, Math.Min(1, v));
            float target = level <= 0.0005f ? 0 : (float)Math.Max(0, 1 + Math.Log10(level) / 2.6);
            shown = Math.Max(target, shown * 0.8f);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            float D = Math.Min(Width, Height);
            float core = D * 0.62f;
            float cx = Width / 2f, cy = Height / 2f;
            Color accent = Theme.Accent;
            Color body, ring, icon;
            switch (Mode)
            {
                case OrbMode.Live: body = accent; ring = accent; icon = Theme.OnAccent; break;
                case OrbMode.Muted: body = Color.FromArgb(60, Theme.Danger); ring = Theme.Danger; icon = Color.FromArgb(255, 255, 138, 142); break;
                case OrbMode.Warn: body = Color.FromArgb(46, Theme.Warn); ring = Theme.Warn; icon = Theme.Warn; break;
                case OrbMode.Waiting: body = Color.FromArgb(40, accent); ring = accent; icon = Glass.AccentHi; break;
                default: body = Color.FromArgb(26, 255, 255, 255); ring = Color.White; icon = Glass.Muted; break;
            }
            // dış halkalar: canlıyken sesle büyür
            float grow = Mode == OrbMode.Live ? shown : 0;
            for (int i = 2; i >= 1; i--)
            {
                float r = core / 2 + (D / 2 - core / 2) * (i / 2f) * (0.45f + 0.55f * grow);
                int a = Mode == OrbMode.Idle ? 10 : (int)((i == 1 ? 46 : 22) * (Mode == OrbMode.Live ? 0.5f + grow : 0.7f));
                using (var b = new SolidBrush(Color.FromArgb(Math.Min(255, a), ring))) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            }
            var cr = new RectangleF(cx - core / 2, cy - core / 2, core, core);
            using (var b = new SolidBrush(body)) g.FillEllipse(b, cr);
            using (var pen = new Pen(Mode == OrbMode.Live ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(Mode == OrbMode.Idle ? 46 : 110, ring), Theme.P(1.5f)))
                g.DrawEllipse(pen, cr);
            float isz = core * 0.42f;
            Icons.Draw(g, Icons.Mic, new RectangleF(cx - isz / 2, cy - isz / 2, isz, isz), icon);
            if (Mode == OrbMode.Muted)
                using (var pen = new Pen(icon, Math.Max(2f, isz / 11f)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, cx - isz * 0.42f, cy - isz * 0.42f, cx + isz * 0.42f, cy + isz * 0.42f);
                }
        }
    }

    // ------------------------------------------------------------------ Açılır seçici (koyu menü)
    class Picker : Ctl
    {
        public readonly List<object> Items = new List<object>();
        int index = -1;
        public event Action Changed;

        public Picker() { }

        public int SelectedIndex { get { return index; } set { index = value; Invalidate(); } }
        public object SelectedItem
        {
            get { return index >= 0 && index < Items.Count ? Items[index] : null; }
            set { index = Items.IndexOf(value); Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Enabled || Items.Count == 0) return;
            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false };
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.BackColor = Color.FromArgb(27, 31, 39);
            menu.Font = Font;
            menu.Padding = new Padding(Theme.P(4));
            for (int i = 0; i < Items.Count; i++)
            {
                int k = i;
                var it = new ToolStripMenuItem((i == index ? "✓  " : "     ") + Items[i]);
                it.ForeColor = i == index ? Glass.AccentHi : Theme.Text;
                it.Padding = new Padding(Theme.P(4), Theme.P(5), Theme.P(4), Theme.P(5));
                it.Click += delegate
                {
                    if (k == index) return;
                    index = k;
                    Invalidate();
                    var h = Changed;
                    if (h != null) h();
                };
                menu.Items.Add(it);
            }
            menu.MinimumSize = new Size(Width, 0);
            menu.Closed += delegate { BeginInvoke((MethodInvoker)delegate { menu.Dispose(); }); };
            menu.Show(this, new Point(0, Height + Theme.P(4)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Glass.Field(g, r, Theme.P(12), hover && Enabled);
            var item = SelectedItem;
            Theme.Draw(g, item == null ? "" : item.ToString(), Font, new RectangleF(Theme.P(14), 0, Width - Theme.P(44), Height), C(Theme.Text),
                StringAlignment.Near, StringAlignment.Center, false);
            float cx = Width - Theme.P(20), cy = Height / 2f, s = Theme.P(4);
            using (var pen = new Pen(C(Theme.Sub), Theme.P(1.6f)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
            }
        }
    }

    // ------------------------------------------------------------------ Metin kutusu (cam alan içinde)
    class TextField : Ctl
    {
        public readonly TextBox Box = new TextBox();
        static readonly Color Fill = Color.FromArgb(36, 40, 50);

        public TextField()
        {
            Cursor = Cursors.IBeam;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Fill;
            Box.ForeColor = Theme.Text;
            Controls.Add(Box);
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
        }

        protected override void OnFontChanged(EventArgs e) { Box.Font = Font; base.OnFontChanged(e); OnResize(e); }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Box.SetBounds(Theme.P(14), (Height - Box.PreferredHeight) / 2, Math.Max(10, Width - Theme.P(28)), Box.PreferredHeight);
        }
        protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, Theme.P(12), Fill);
            Theme.Stroke(g, r, Theme.P(12), Box.Focused ? Theme.Accent : Color.FromArgb(60, 255, 255, 255), Box.Focused ? Theme.P(1.5f) : 1);
        }
    }

    class DarkMenuColors : ProfessionalColorTable
    {
        static readonly Color Bg = Color.FromArgb(27, 31, 39), Sel = Color.FromArgb(48, 66, 70), Edge = Color.FromArgb(58, 64, 76);
        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Edge; } }
        public override Color MenuItemBorder { get { return Sel; } }
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color MenuItemPressedGradientBegin { get { return Sel; } }
        public override Color MenuItemPressedGradientEnd { get { return Sel; } }
        public override Color SeparatorDark { get { return Edge; } }
        public override Color SeparatorLight { get { return Edge; } }
    }
}
