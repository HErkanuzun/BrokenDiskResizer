using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BrokenDiskResizer
{
    // ------------------------------------------------------------------ Tema
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(22, 24, 29);
        public static readonly Color Card = Color.FromArgb(31, 34, 40);
        public static readonly Color Field = Color.FromArgb(40, 44, 52);
        public static readonly Color FieldHover = Color.FromArgb(50, 55, 64);
        public static readonly Color Border = Color.FromArgb(52, 57, 66);
        public static readonly Color Text = Color.FromArgb(234, 237, 242);
        public static readonly Color Sub = Color.FromArgb(142, 150, 163);
        public static readonly Color Accent = Color.FromArgb(46, 196, 176);
        public static readonly Color AccentHover = Color.FromArgb(70, 214, 195);
        public static readonly Color OnAccent = Color.FromArgb(8, 30, 27);
        public static readonly Color Danger = Color.FromArgb(240, 86, 91);
        public static readonly Color Warn = Color.FromArgb(245, 165, 36);
        public static readonly Color Ok = Color.FromArgb(61, 214, 140);

        public static float S = 1f;   // DPI ölçeği
        public static int P(float v) { return (int)Math.Round(v * S); }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Dim(Color c) { return Mix(c, Bg, 0.55f); }

        // Küçük asma kilit simgesi (cx: sol kenar, cy: dikey orta, size: yükseklik)
        public static void Padlock(Graphics g, float x, float cy, float size, Color c)
        {
            float bw = size * 0.85f, bh = size * 0.6f, by = cy - size / 2 + size * 0.4f;
            using (var pen = new Pen(c, Math.Max(1.2f, size * 0.13f)))
                g.DrawArc(pen, x + bw * 0.2f, cy - size / 2, bw * 0.6f, size * 0.62f, 180, 180);
            using (var pen = new Pen(c, Math.Max(1.2f, size * 0.13f)))
            {
                g.DrawLine(pen, x + bw * 0.2f, cy - size / 2 + size * 0.31f, x + bw * 0.2f, by);
                g.DrawLine(pen, x + bw * 0.8f, cy - size / 2 + size * 0.31f, x + bw * 0.8f, by);
            }
            Fill(g, new RectangleF(x, by, bw, bh), size * 0.12f, c);
        }

        // "Sisli" kilit görünümü: ayar uygulanmışken değiştirilemeyen öğelerin üstüne çekilen buğu.
        // Sadece şeklin içine çizilir (köşelerde dikdörtgen leke kalmasın).
        public static void Fog(Graphics g, RectangleF r, float rad)
        {
            using (var path = Round(r, rad))
            {
                var st = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                using (var b = new LinearGradientBrush(r, Color.FromArgb(150, 58, 64, 76), Color.FromArgb(110, 40, 44, 54), 90f))
                    g.FillRectangle(b, r);
                using (var pen = new Pen(Color.FromArgb(22, 210, 220, 235), P(1.5f)))
                    for (float d = -r.Height; d < r.Width + r.Height; d += P(9))
                        g.DrawLine(pen, r.X + d, r.Bottom, r.X + d + r.Height, r.Y);
                g.Restore(st);
            }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }

        public static void Draw(Graphics g, string s, Font f, RectangleF r, Color c, StringAlignment h, StringAlignment v, bool wrap)
        {
            using (var sf = new StringFormat())
            using (var b = new SolidBrush(c))
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                if (!wrap) sf.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(s ?? "", f, b, r, sf);
            }
        }

        // Denetimin altını çizer: cam sayfadaysa zemin ve kartlar, değilse düz arka plan rengi
        public static Graphics Prep(PaintEventArgs e, Control c)
        {
            var self = c as Ctl;
            if (self != null && c.Parent is Ctl && !(c.Parent is ISurface)) self.PaintParentUnder(e.Graphics);   // ör. kart içindeki düğme
            else if (!Glass.PaintUnder(e.Graphics, c)) e.Graphics.Clear(c.BackColor);
            return Prep(e);
        }

        public static Graphics Prep(PaintEventArgs e, Color bg)
        {
            e.Graphics.Clear(bg);
            return Prep(e);
        }

        static Graphics Prep(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        // Windows 10/11 koyu başlık çubuğu
        public static void DarkTitle(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch { }
        }

        public static Image Logo()
        {
            try
            {
                using (var st = typeof(Theme).Assembly.GetManifestResourceStream("BrokenDiskResizer.logo.png"))
                    if (st != null) return new Bitmap(st);
            }
            catch { }
            return AppIcon(48).ToBitmap();
        }

        static Icon appIcon;
        public static Icon AppIcon(int size)
        {
            try
            {
                using (var s = typeof(Theme).Assembly.GetManifestResourceStream("BrokenDiskResizer.app.ico"))
                    if (s != null) return new Icon(s, size, size);
            }
            catch { }
            if (appIcon == null)
            {
                try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { appIcon = SystemIcons.Application; }
            }
            return appIcon;
        }
    }

    // ------------------------------------------------------------------ Ortak taban
    // Titremeden kaydırılabilen panel (ana pencerenin duyarlı sayfası için). Pencere cam zeminliyse zemini o çizer;
    // üstüne eklenen düz yazı ve resimler saydam olur (koyu kutu görünmez).
    class BufferedPanel : Panel, ISurface
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        public void PaintSurface(Graphics g, Rectangle r)
        {
            var f = FindForm();
            if (f is ISurface) Glass.Backdrop(g, f.ClientSize, f.PointToClient(PointToScreen(Point.Empty)), r);
            else using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, r);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { PaintSurface(e.Graphics, e.ClipRectangle); }
        // zemin pencereye sabit: kayınca her şey yeniden çizilir (Windows'un kaydırması zemini de kaydırırdı)
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(true); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(true); }
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); Glass.SeeThrough(e.Control); }
    }

    class Ctl : Control
    {
        protected bool hover, pressed;

        public Ctl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected Color C(Color c) { return Enabled ? c : Theme.Dim(c); }

        // Üst denetim de özel çiziliyorsa (ör. cam kart), bu denetimin altında kalan kısmını çizer
        internal void PaintParentUnder(Graphics g)
        {
            var p = Parent;
            var st = g.Save();
            g.TranslateTransform(-Left, -Top);
            var area = new Rectangle(Left, Top, Width, Height);
            using (var pe = new PaintEventArgs(g, area)) InvokePaint(p, pe);
            g.Restore(st);
        }

        // Ayar uygulanmışken kilitli (sisli) görünür
        bool fogged;
        public bool Fogged { get { return fogged; } set { if (fogged == value) return; fogged = value; Invalidate(); } }
        protected void FogOver(Graphics g, float rad)
        {
            if (fogged) Theme.Fog(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), rad);
        }
    }

    // ------------------------------------------------------------------ Düğme
    enum BtnKind { Primary, Secondary, Ghost, DangerGhost }

    class FlatButton : Ctl
    {
        public BtnKind Kind = BtnKind.Secondary;

        public FlatButton(string text, BtnKind kind) { Text = text; Kind = kind; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = Math.Min(Height / 2f, Theme.P(22));   // telefon uygulaması gibi yuvarlak uçlu
            Color fg = Theme.Text;
            switch (Kind)
            {
                case BtnKind.Primary:
                    Theme.Fill(g, r, rad, C(pressed ? Theme.Mix(Theme.Accent, Color.Black, 0.15f) : hover ? Theme.AccentHover : Theme.Accent));
                    fg = Theme.OnAccent;
                    break;
                case BtnKind.Secondary:
                    if (Enabled) Glass.Field(g, r, rad, hover);
                    else Theme.Stroke(g, r, rad, Color.FromArgb(30, 255, 255, 255), 1);
                    break;
                case BtnKind.Ghost:
                    if (hover && Enabled) Theme.Fill(g, r, rad, Color.FromArgb(22, 255, 255, 255));
                    fg = Theme.Sub;
                    break;
                case BtnKind.DangerGhost:
                    if (hover && Enabled) Theme.Fill(g, r, rad, Color.FromArgb(40, Theme.Danger));
                    fg = Color.FromArgb(255, 138, 142);
                    break;
            }
            if (!Enabled) fg = Color.FromArgb(110, fg);
            var f = Kind == BtnKind.Primary ? new Font(Font, FontStyle.Bold) : Font;
            Theme.Draw(g, Text, f, new RectangleF(0, 0, Width, Height), fg, StringAlignment.Center, StringAlignment.Center, false);
            if (f != Font) f.Dispose();
            FogOver(g, rad);
        }
    }

    // ------------------------------------------------------------------ Segmentli seçici
    class Seg : Ctl
    {
        string[] items = new string[0];
        int selected = -1;
        int hot = -1;
        public event Action<int> Changed;

        public string[] Items { get { return items; } set { items = value ?? new string[0]; Invalidate(); } }
        public int Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        RectangleF Item(int i)
        {
            float w = (Width - Theme.P(6)) / (float)Math.Max(1, items.Length);
            return new RectangleF(Theme.P(3) + i * w, Theme.P(3), w, Height - Theme.P(6));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < items.Length; i++) if (Item(i).Contains(e.Location)) h = i;
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Enabled) return;
            for (int i = 0; i < items.Length; i++)
                if (Item(i).Contains(e.Location) && i != selected)
                {
                    selected = i;
                    Invalidate();
                    var h = Changed;
                    if (h != null) h(i);
                }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Glass.Field(g, r, Height / 2f, false);
            for (int i = 0; i < items.Length; i++)
            {
                var ir = Item(i);
                Color fg = C(Theme.Sub);
                if (i == selected)
                {
                    Theme.Fill(g, ir, ir.Height / 2f, C(Theme.Accent));
                    fg = C(Theme.OnAccent);
                }
                else if (i == hot && Enabled)
                {
                    Theme.Fill(g, ir, ir.Height / 2f, Color.FromArgb(24, 255, 255, 255));
                    fg = Theme.Text;
                }
                var tr = new RectangleF(ir.X + Theme.P(6), ir.Y, ir.Width - Theme.P(12), ir.Height);
                var f = i == selected ? new Font(Font, FontStyle.Bold) : Font;
                Theme.Draw(g, items[i], f, tr, fg, StringAlignment.Center, StringAlignment.Center, false);
                if (f != Font) f.Dispose();
            }
            FogOver(g, Theme.P(9));
        }
    }

    // ------------------------------------------------------------------ Kaydırıcı
    class FlatSlider : Ctl
    {
        int min, max = 100, val;
        bool drag;
        public event Action<int> ValueChanged;

        public FlatSlider()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = Math.Max(min + 1, value); Invalidate(); } }
        public int Value
        {
            get { return val; }
            set { val = Math.Max(min, Math.Min(max, value)); Invalidate(); }
        }

        float TrackL { get { return Theme.P(10); } }
        float TrackR { get { return Width - Theme.P(10); } }

        void SetFromX(int x)
        {
            float t = (x - TrackL) / Math.Max(1f, TrackR - TrackL);
            int v = min + (int)Math.Round(Math.Max(0, Math.Min(1, t)) * (max - min));
            Commit(v);
        }

        void Commit(int v)
        {
            v = Math.Max(min, Math.Min(max, v));
            if (v == val) return;
            val = v;
            Invalidate();
            var h = ValueChanged;
            if (h != null) h(v);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            Focus();
            drag = true;
            SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = false; }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            int step = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Commit(val - step);
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Commit(val + step);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            float cy = Height / 2f, th = Theme.P(6);
            float t = (val - min) / (float)Math.Max(1, max - min);
            float x = TrackL + t * (TrackR - TrackL);
            Theme.Fill(g, new RectangleF(TrackL, cy - th / 2, TrackR - TrackL, th), th / 2, Theme.Field);
            Theme.Fill(g, new RectangleF(TrackL, cy - th / 2, Math.Max(th, x - TrackL), th), th / 2, C(Theme.Accent));
            float k = Theme.P(focusedOrHover ? 20 : 18);
            if (Focused && Enabled)
                using (var b = new SolidBrush(Color.FromArgb(60, Theme.Accent)))
                    g.FillEllipse(b, x - k / 2 - Theme.P(5), cy - k / 2 - Theme.P(5), k + Theme.P(10), k + Theme.P(10));
            using (var b = new SolidBrush(C(Color.White))) g.FillEllipse(b, x - k / 2, cy - k / 2, k, k);
            using (var p = new Pen(C(Theme.Accent), Theme.P(3))) g.DrawEllipse(p, x - k / 2, cy - k / 2, k, k);
            if (Fogged) Theme.Fog(g, new RectangleF(1, cy - Theme.P(13), Width - 2, Theme.P(26)), Theme.P(13));
        }

        bool focusedOrHover { get { return Enabled && (hover || drag); } }
    }

    // ------------------------------------------------------------------ Açma/kapama anahtarı
    class Toggle : Ctl
    {
        bool on;
        public event Action<bool> Changed;

        public Toggle(string text) { Text = text; }

        public bool Checked
        {
            get { return on; }
            set { on = value; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Enabled) return;
            on = !on;
            Invalidate();
            var h = Changed;
            if (h != null) h(on);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            float w = Theme.P(38), h = Theme.P(22), y = (Height - h) / 2f;
            var tr = new RectangleF(0.5f, y, w, h);
            if (on) Theme.Fill(g, tr, h / 2, C(hover ? Theme.AccentHover : Theme.Accent));
            else
            {
                Theme.Fill(g, tr, h / 2, Color.FromArgb(hover ? 110 : 85, 255, 255, 255));
            }
            float k = h - Theme.P(6);
            float kx = on ? tr.Right - k - Theme.P(3) : tr.X + Theme.P(3);
            using (var b = new SolidBrush(C(on ? Color.White : Color.FromArgb(0xB0, 0xB7, 0xC3)))) g.FillEllipse(b, kx, y + Theme.P(3), k, k);
            Theme.Draw(g, Text, Font, new RectangleF(w + Theme.P(10), 0, Width - w - Theme.P(10), Height), C(Theme.Text),
                StringAlignment.Near, StringAlignment.Center, false);
            if (Fogged) Theme.Fog(g, tr, h / 2);
        }
    }

    // ------------------------------------------------------------------ Yöntem kartı
    class ModeCard : Ctl
    {
        public string Title = "", Desc = "", Badge = "";
        public Color BadgeColor = Theme.Accent;
        bool sel;
        public event Action Picked;

        public bool Selected { get { return sel; } set { sel = value; Invalidate(); } }

        float BadgeRow { get { return Badge != "" ? Theme.P(24) : 0; } }

        // Başlık dar kartta iki satıra inebilir
        float TitleH(Graphics g, int width)
        {
            float tx = Theme.P(14) + Theme.P(18) + Theme.P(10);
            using (var bold = new Font(Font.FontFamily, Font.Size + 0.5f, FontStyle.Bold))
                return Math.Max(Theme.P(18), g.MeasureString(Title, bold, (int)(width - tx - Theme.P(14))).Height);
        }

        public int Measure(int width)
        {
            using (var g = CreateGraphics())
            {
                float pad = Theme.P(14);
                var d = g.MeasureString(Desc, Font, (int)(width - pad * 2));
                return (int)Math.Ceiling(pad + TitleH(g, width) + BadgeRow + Theme.P(10) + d.Height + Theme.P(16));
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Enabled || sel) return;
            var h = Picked;
            if (h != null) h();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            Theme.Fill(g, r, Theme.P(12), sel ? Theme.Mix(Theme.Card, Theme.Accent, 0.08f) : hover && Enabled ? Theme.Field : Theme.Card);
            Theme.Stroke(g, r, Theme.P(12), C(sel ? Theme.Accent : Theme.Border), sel ? Theme.P(2) : 1);

            float pad = Theme.P(14), rd = Theme.P(18);
            float rx = pad, ry = pad;
            using (var p = new Pen(C(sel ? Theme.Accent : Theme.Sub), Theme.P(2))) g.DrawEllipse(p, rx, ry, rd, rd);
            if (sel) using (var b = new SolidBrush(C(Theme.Accent))) g.FillEllipse(b, rx + Theme.P(4.5f), ry + Theme.P(4.5f), rd - Theme.P(9), rd - Theme.P(9));

            using (var bold = new Font(Font.FontFamily, Font.Size + 0.5f, FontStyle.Bold))
            {
                float tx = rx + rd + Theme.P(10);
                float th = TitleH(g, Width);
                Theme.Draw(g, Title, bold, new RectangleF(tx, ry - Theme.P(1), Width - tx - pad, th + Theme.P(2)), C(Theme.Text),
                    StringAlignment.Near, StringAlignment.Near, true);
                float top = ry + Math.Max(rd, th);
                float dy = top + Theme.P(10);
                if (Badge != "")
                {
                    using (var small = new Font(Font.FontFamily, Font.Size - 1.5f, FontStyle.Bold))
                    {
                        var bs = g.MeasureString(Badge, small);
                        var br = new RectangleF(pad, top + Theme.P(7), Math.Min(bs.Width + Theme.P(10), Width - pad * 2), bs.Height + Theme.P(2));
                        Theme.Fill(g, br, br.Height / 2, C(Theme.Mix(BadgeColor, Theme.Card, 0.75f)));
                        Theme.Draw(g, Badge, small, br, C(BadgeColor), StringAlignment.Center, StringAlignment.Center, false);
                    }
                    dy += BadgeRow;
                }
                Theme.Draw(g, Desc, Font, new RectangleF(pad, dy, Width - pad * 2, Height - dy - Theme.P(8)),
                    C(Theme.Sub), StringAlignment.Near, StringAlignment.Near, true);
            }
            if (Fogged) Theme.Fog(g, r, Theme.P(12));
        }
    }

    // ------------------------------------------------------------------ Durum kutusu
    enum StatusKind { Idle, Active, Warn }

    class StatusBox : Ctl
    {
        public StatusKind Kind;

        public StatusBox() { Cursor = Cursors.Default; }

        public int Measure(int width)
        {
            using (var g = CreateGraphics())
            {
                var s = g.MeasureString(Text, Font, (int)(width - Theme.P(52)));
                return (int)Math.Ceiling(s.Height) + Theme.P(26);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            Color dot = Kind == StatusKind.Active ? Theme.Ok : Kind == StatusKind.Warn ? Theme.Warn : Theme.Sub;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Glass.Card(g, r, Theme.P(14), Kind == StatusKind.Idle ? Color.Empty : dot);
            float d = Theme.P(10), cy = Theme.P(13) + Font.Height / 2f;
            using (var b = new SolidBrush(Color.FromArgb(60, dot))) g.FillEllipse(b, Theme.P(16) - d / 2 - Theme.P(4), cy - d / 2 - Theme.P(4), d + Theme.P(8), d + Theme.P(8));
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, Theme.P(16) - d / 2, cy - d / 2, d, d);
            Theme.Draw(g, Text, Font, new RectangleF(Theme.P(34), Theme.P(12), Width - Theme.P(46), Height - Theme.P(20)), Theme.Text,
                StringAlignment.Near, StringAlignment.Near, true);
        }
    }
}
