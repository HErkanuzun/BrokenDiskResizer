using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BrokenDiskResizer
{
    /// <summary>
    /// Pencere çerçevesi de uygulamanın tasarımında: Windows'un başlık çubuğu kaldırılır, zemin en üste kadar uzanır,
    /// küçült / büyüt / kapat düğmelerini uygulama çizer. Çerçeve stili korunduğu için Windows'un gölgesi,
    /// yuvarlak köşeleri, kenardan boyutlandırma, sürükleyip ekrana yaslama (snap) ve çift tıkla büyütme çalışmaya devam eder.
    /// </summary>
    static class Frame
    {
        const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1, HTCAPTION = 2, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct NCCALCSIZE_PARAMS { public RECT r0, r1, r2; public IntPtr pos; }

        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>Pencere tanıtıcısı oluşunca çağrılır: yeni çerçeve hesabı hemen uygulansın.</summary>
        public static void Apply(Form f)
        {
            Theme.DarkTitle(f);
            SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);   // NOSIZE | NOMOVE | NOZORDER | FRAMECHANGED
        }

        /// <summary>Formun WndProc'undan çağrılır. İşlediyse true.</summary>
        public static bool Handle(Form f, ref Message m, int captionH)
        {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                var p = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
                int top = p.r0.T, left = p.r0.L;
                DefWindowProc(m.HWnd, m.Msg, m.WParam, m.LParam);   // yan ve alt kenarları Windows hesaplasın
                p = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
                // büyütülmüş pencere ekranın dışına taşar: üstten de yan kenar kadar içeri al
                p.r0.T = top + (f.WindowState == FormWindowState.Maximized ? p.r0.L - left : 0);
                Marshal.StructureToPtr(p, m.LParam, false);
                m.Result = IntPtr.Zero;
                return true;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = DefWindowProc(m.HWnd, m.Msg, m.WParam, m.LParam);
                if ((int)m.Result != HTCLIENT) return true;
                long lp = m.LParam.ToInt64();
                var pt = f.PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                bool sizable = f.FormBorderStyle == FormBorderStyle.Sizable && f.WindowState == FormWindowState.Normal;
                int edge = Theme.P(6), corner = Theme.P(14);
                if (sizable && pt.Y < edge)
                    m.Result = (IntPtr)(pt.X < corner ? HTTOPLEFT : pt.X >= f.ClientSize.Width - corner ? HTTOPRIGHT : HTTOP);
                else if (pt.Y < captionH)
                    m.Result = (IntPtr)HTCAPTION;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Uygulamanın kendi başlık çubuğu: solda simge ve ad, sağda cam düğmeler. Düğmelerin dışındaki yer
    /// fareyi pencereye bırakır (HTTRANSPARENT), böylece sürükleme ve çift tık Windows'ta olduğu gibi çalışır.
    /// </summary>
    class TitleBar : Ctl
    {
        const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
        public bool MinMax = true;      // küçük pencerelerde sadece kapat
        public bool ShowTitle = true;
        int hot = -1, down = -1;

        public TitleBar() { Cursor = Cursors.Default; Height = Theme.P(40); }

        int Count { get { return MinMax ? 3 : 1; } }
        int BtnW { get { return Theme.P(46); } }

        // 0 küçült, 1 büyüt, 2 kapat (MinMax kapalıyken tek düğme = kapat)
        Rectangle Btn(int i) { return new Rectangle(Width - (Count - i) * BtnW, 0, BtnW, Theme.P(34)); }
        int Kind(int i) { return MinMax ? i : 2; }

        int Hit(Point p) { for (int i = 0; i < Count; i++) if (Btn(i).Contains(p)) return i; return -1; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                var f = FindForm();
                bool topEdge = f != null && f.FormBorderStyle == FormBorderStyle.Sizable && f.WindowState == FormWindowState.Normal && p.Y < Theme.P(4);
                if (Hit(p) < 0 || topEdge) { m.Result = (IntPtr)HTTRANSPARENT; return; }
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; down = -1; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) down = Hit(e.Location); base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = Hit(e.Location);
            bool fire = e.Button == MouseButtons.Left && i >= 0 && i == down;
            down = -1;
            var f = FindForm();
            if (!fire || f == null) return;
            switch (Kind(i))
            {
                case 0: f.WindowState = FormWindowState.Minimized; break;
                case 1: f.WindowState = f.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
                default: f.Close(); break;
            }
            hot = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            var f = FindForm();
            bool active = f != null && Form.ActiveForm == f;
            Color fg = active ? Theme.Text : Theme.Sub;
            if (ShowTitle && f != null)
            {
                float x = Theme.P(14);
                if (f.Icon != null)
                {
                    using (var ic = new Icon(f.Icon, Theme.P(16), Theme.P(16))) g.DrawIcon(ic, new Rectangle((int)x, Theme.P(10), Theme.P(16), Theme.P(16)));
                    x += Theme.P(26);
                }
                using (var font = new Font(Font.FontFamily, 9f))
                    Theme.Draw(g, f.Text, font, new RectangleF(x, 0, Width - x - Count * BtnW, Theme.P(36)), active ? Color.FromArgb(200, Theme.Text) : Theme.Sub,
                        StringAlignment.Near, StringAlignment.Center, false);
            }
            for (int i = 0; i < Count; i++)
            {
                var r = Btn(i);
                int k = Kind(i);
                bool h = i == hot, d = h && i == down;
                if (h)
                {
                    if (k == 2) using (var b = new SolidBrush(d ? Color.FromArgb(200, 196, 43, 28) : Color.FromArgb(232, 17, 35))) g.FillRectangle(b, r);
                    else using (var b = new SolidBrush(Color.FromArgb(d ? 16 : 26, 255, 255, 255))) g.FillRectangle(b, r);
                }
                Color c = h && k == 2 ? Color.White : fg;
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = Theme.P(5);
                g.SmoothingMode = k == 2 ? SmoothingMode.AntiAlias : SmoothingMode.None;
                using (var pen = new Pen(c, Math.Max(1f, Theme.S)))
                {
                    if (k == 0) g.DrawLine(pen, cx - s, cy, cx + s, cy);
                    else if (k == 1)
                    {
                        if (f != null && f.WindowState == FormWindowState.Maximized)
                        {
                            g.DrawRectangle(pen, cx - s, cy - s + Theme.P(2), s * 2 - Theme.P(2), s * 2 - Theme.P(2));
                            g.DrawLines(pen, new[] { new PointF(cx - s + Theme.P(2), cy - s), new PointF(cx + s, cy - s), new PointF(cx + s, cy + s - Theme.P(2)) });
                        }
                        else g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                    }
                    else
                    {
                        g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                        g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
                    }
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }
        }
    }
}
