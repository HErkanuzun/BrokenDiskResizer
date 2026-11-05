using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BrokenUi
{
    /// <summary>
    /// Koyu, buzlu cam kaydırma çubuğu. Windows'un beyaz çubuğu sayfanın kenarından dışarı itilip gizlenir;
    /// yerine ince, yarı saydam, yuvarlak bir tutamak çizilir. Fareyle üstüne gelince ya da sürüklerken belirginleşir.
    /// Tekerlek, klavye ve odak kaydırması Windows'ta olduğu gibi çalışır.
    /// Broken ailesinde ortak: aynı dosya her projede.
    /// </summary>
    sealed class GlassScroll : Control
    {
        readonly ScrollableControl page;
        readonly Panel host;
        readonly Color bg, accent;
        readonly Timer poll = new Timer();
        float scale = 1f;
        bool hover, drag;
        int dragY0, dragV0, lastV = -1, lastMax = -1;

        /// <summary>page: AutoScroll açık, ebeveyninde Dock=Fill duran sayfa. bg: sayfa rengi.</summary>
        public static GlassScroll Attach(ScrollableControl page, Color bg, Color accent)
        {
            return new GlassScroll(page, bg, accent);
        }

        GlassScroll(ScrollableControl page, Color bg, Color accent)
        {
            this.page = page;
            this.bg = bg;
            this.accent = accent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = bg;
            Visible = false;

            // sayfayı bir kabın içine al: kap pencere boyunda, sayfa çubuk kadar daha geniş → çubuk görünmez
            Control parent = page.Parent;
            host = new Panel { BackColor = bg, Dock = page.Dock, Margin = Padding.Empty };
            if (parent != null)
            {
                int idx = parent.Controls.GetChildIndex(page);
                parent.Controls.Remove(page);
                parent.Controls.Add(host);
                parent.Controls.SetChildIndex(host, idx);
            }
            page.Dock = DockStyle.None;
            page.Location = Point.Empty;
            host.Controls.Add(page);
            host.Controls.Add(this);
            BringToFront();

            host.Resize += delegate { Fit(); };
            page.Layout += delegate { if (!fitting && host.IsHandleCreated) host.BeginInvoke((MethodInvoker)Fit); };
            page.Scroll += delegate { Sync(); };
            page.MouseWheel += delegate { Sync(); };
            host.HandleCreated += delegate
            {
                using (var g = host.CreateGraphics()) scale = g.DpiX / 96f;
                Fit();
            };
            poll.Interval = 50;         // odakla kayan sayfa vb. için hafif yoklama
            poll.Tick += delegate { Sync(); };
            poll.Start();
            Disposed += delegate { poll.Dispose(); };
            Fit();
        }

        int P(float v) { return (int)Math.Round(v * scale); }

        bool fitting;

        /// <summary>Sayfanın iç alanı kap ile aynı boyda olsun; Windows çubukları kabın dışında kalsın.</summary>
        void Fit()
        {
            if (fitting || host.Width <= 0 || host.Height <= 0) return;
            fitting = true;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    int w = host.Width + (page.Width - page.ClientSize.Width);
                    int h = host.Height + (page.Height - page.ClientSize.Height);
                    if (page.Width == w && page.Height == h && page.Left == 0 && page.Top == 0) break;
                    page.SetBounds(0, 0, w, h);
                }
                SetBounds(host.Width - P(14), 0, P(14), host.Height);
            }
            finally { fitting = false; }
            lastV = -1;
            Sync();
        }

        int Max { get { return Math.Max(0, page.DisplayRectangle.Height - page.ClientSize.Height); } }
        int Value { get { return -page.AutoScrollPosition.Y; } }

        void Sync()
        {
            int max = Max, v = Value;
            bool show = max > 0;
            if (Visible != show) { Visible = show; if (show) BringToFront(); }
            if (v != lastV || max != lastMax) { lastV = v; lastMax = max; Invalidate(); }
        }

        RectangleF ThumbRect()
        {
            float pad = P(6), track = Height - pad * 2;
            float content = Math.Max(1, page.DisplayRectangle.Height);
            float th = Math.Max(P(36), track * page.ClientSize.Height / content);
            float ty = pad + (track - th) * Value / Math.Max(1, Max);
            float w = hover || drag ? P(6) : P(4);
            return new RectangleF(Width - P(4) - w, ty, w, th);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(bg);
            if (Max <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = ThumbRect();
            // buzlu cam: arka plana karışan beyaz; etkileşimde vurgu rengine doğru hafif ısınır
            Color fill = drag ? Mix(Color.FromArgb(150, 255, 255, 255), accent, .35f)
                       : hover ? Color.FromArgb(105, 255, 255, 255)
                       : Color.FromArgb(48, 255, 255, 255);
            using (var path = Round(r, r.Width / 2f))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(hover || drag ? 40 : 22, 255, 255, 255), 1f)) g.DrawPath(pen, path);
            }
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static GraphicsPath Round(RectangleF r, float rad)
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

        void ScrollTo(int v)
        {
            v = Math.Max(0, Math.Min(Max, v));
            page.AutoScrollPosition = new Point(-page.AutoScrollPosition.X, v);
            Sync();
            page.Update();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Max <= 0) return;
            var r = ThumbRect();
            if (e.Y < r.Top || e.Y > r.Bottom)
            {
                // boş alana tıklama: tutamağın ortası oraya gelsin
                float track = Height - P(12) - r.Height;
                ScrollTo((int)((e.Y - P(6) - r.Height / 2f) * Max / Math.Max(1f, track)));
            }
            drag = true;
            dragY0 = e.Y;
            dragV0 = Value;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!drag) return;
            var r = ThumbRect();
            float track = Height - P(12) - r.Height;
            ScrollTo(dragV0 + (int)((e.Y - dragY0) * Max / Math.Max(1f, track)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(Value - e.Delta * SystemInformation.MouseWheelScrollLines * P(16) / 120);
        }

        // ---------------------------------------------------------------- Windows'un kendi çubukları (metin kutusu, liste)

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

        /// <summary>Kendi çizemediğimiz denetimlerin (çok satırlı metin kutusu vb.) çubuklarını koyu Windows temasına alır.</summary>
        public static void DarkNative(Control c)
        {
            MethodInvoker apply = delegate
            {
                try
                {
                    // Windows 10 1809 öncesinde bu işlevler yok; hata yakalanır, çubuk açık renkte kalır
                    SetPreferredAppMode(1);
                    AllowDarkModeForWindow(c.Handle, true);
                    SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                }
                catch { }
            };
            if (c.IsHandleCreated) apply(); else c.HandleCreated += delegate { apply(); };
        }
    }
}
