using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrokenDiskResizer
{
    // Koyu temalı onay penceresi. word verilirse kullanıcının o kelimeyi yazması gerekir.
    class ConfirmDialog : Form, ISurface
    {
        readonly TitleBar titleBar = new TitleBar();
        protected override void WndProc(ref Message m) { if (!Frame.Handle(this, ref m, titleBar.Height)) base.WndProc(ref m); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); titleBar.Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); titleBar.Invalidate(); }
        public void PaintSurface(Graphics g, Rectangle r) { Glass.Backdrop(g, ClientSize, Point.Empty, r); }
        protected override void OnPaintBackground(PaintEventArgs e) { PaintSurface(e.Graphics, e.ClipRectangle); }
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); Glass.SeeThrough(e.Control); }
        readonly TextBox input;
        readonly FlatButton ok;

        public ConfirmDialog(string title, string body, string word, string okText, bool danger)
        {
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            int w = Theme.P(500), pad = Theme.P(24), y = titleBar.Height + Theme.P(4);
            titleBar.MinMax = false;
            titleBar.Dock = DockStyle.Top;
            Controls.Add(titleBar);
            DoubleBuffered = true;

            var t = new Label();
            t.Text = title;
            t.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);
            t.ForeColor = danger ? Theme.Danger : Theme.Text;
            t.SetBounds(pad, y, w - pad * 2, Theme.P(30));
            Controls.Add(t);
            y += Theme.P(38);

            var b = new Label();
            b.Text = body;
            b.ForeColor = Theme.Text;
            using (var g = CreateGraphics())
                b.SetBounds(pad, y, w - pad * 2, (int)Math.Ceiling(g.MeasureString(body, Font, w - pad * 2).Height) + Theme.P(4));
            Controls.Add(b);
            y = b.Bottom + Theme.P(14);

            if (!string.IsNullOrEmpty(word))
            {
                var hint = new Label();
                hint.Text = L.T("Onaylamak için büyük harflerle şunu yaz: ", "To confirm, type this in capitals: ") + word;
                hint.ForeColor = Theme.Sub;
                hint.SetBounds(pad, y, w - pad * 2, Theme.P(22));
                Controls.Add(hint);
                y += Theme.P(26);
                input = new TextBox();
                input.BorderStyle = BorderStyle.FixedSingle;
                input.BackColor = Theme.Field;
                input.ForeColor = Theme.Text;
                input.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
                input.SetBounds(pad, y, w - pad * 2, Theme.P(30));
                input.TextChanged += delegate { ok.Enabled = input.Text.Trim() == word; };
                Controls.Add(input);
                y = input.Bottom + Theme.P(18);
            }

            var cancel = new FlatButton(L.T("Vazgeç", "Cancel"), BtnKind.Secondary);
            cancel.Font = Font;
            cancel.SetBounds(w - pad - Theme.P(300) - Theme.P(10), y, Theme.P(150), Theme.P(40));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            ok = new FlatButton(okText, BtnKind.Primary);
            ok.Font = Font;
            ok.SetBounds(w - pad - Theme.P(150), y, Theme.P(150), Theme.P(40));
            ok.Enabled = string.IsNullOrEmpty(word);
            ok.Click += delegate { if (ok.Enabled) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(ok);
            y += Theme.P(40) + pad;

            ClientSize = new Size(w, y);
            CancelButton = null;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Frame.Apply(this); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (input != null) input.Focus();
        }

        protected override bool ProcessDialogKey(Keys k)
        {
            if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessDialogKey(k);
        }

        public static bool Ask(IWin32Window owner, string title, string body, string word, string okText, bool danger)
        {
            using (var d = new ConfirmDialog(title, body, word, okText, danger)) return d.ShowDialog(owner) == DialogResult.OK;
        }

        public static void Info(IWin32Window owner, string title, string body)
        {
            using (var d = new ConfirmDialog(title, body, null, L.T("Tamam", "OK"), false))
            {
                foreach (Control c in d.Controls)
                    if (c is FlatButton && ((FlatButton)c).Kind == BtnKind.Secondary) c.Visible = false;
                d.ShowDialog(owner);
            }
        }
    }
}

namespace BrokenDiskResizer
{
    // ------------------------------------------------------------------ Numaralı adımlar (rehber)
    class StepsView : Ctl
    {
        public string[][] Steps = new string[0][];   // { başlık, açıklama }

        public StepsView() { Cursor = Cursors.Default; }

        public int Measure(int width)
        {
            using (var g = CreateGraphics())
            {
                float h = 0;
                foreach (var s in Steps) h += Row(g, s, width) + Theme.P(10);
                return (int)Math.Ceiling(h);
            }
        }

        float Row(Graphics g, string[] s, int width)
        {
            float tx = Theme.P(44);
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                float a = g.MeasureString(s[0], bold, (int)(width - tx)).Height;
                float b = g.MeasureString(s[1], Font, (int)(width - tx)).Height;
                return Math.Max(Theme.P(30), a + b + Theme.P(2));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, this);
            float y = 0, tx = Theme.P(44), d = Theme.P(28);
            using (var bold = new Font(Font, FontStyle.Bold))
                for (int i = 0; i < Steps.Length; i++)
                {
                    var s = Steps[i];
                    float h = Row(g, s, Width);
                    using (var b = new SolidBrush(Theme.Mix(Theme.Accent, Theme.Bg, 0.75f))) g.FillEllipse(b, 0, y, d, d);
                    Theme.Draw(g, (i + 1).ToString(), bold, new RectangleF(0, y, d, d), Theme.Accent, StringAlignment.Center, StringAlignment.Center, false);
                    float th = g.MeasureString(s[0], bold, (int)(Width - tx)).Height;
                    Theme.Draw(g, s[0], bold, new RectangleF(tx, y + Theme.P(4), Width - tx, th), Theme.Text, StringAlignment.Near, StringAlignment.Near, true);
                    Theme.Draw(g, s[1], Font, new RectangleF(tx, y + Theme.P(4) + th, Width - tx, h - th), Theme.Sub, StringAlignment.Near, StringAlignment.Near, true);
                    y += h + Theme.P(10);
                }
        }
    }

    // ------------------------------------------------------------------ Koyu temalı küçük pencere tabanı
    class DarkDialog : Form, ISurface
    {
        protected readonly TitleBar titleBar = new TitleBar();
        protected override void WndProc(ref Message m) { if (!Frame.Handle(this, ref m, titleBar.Height)) base.WndProc(ref m); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); titleBar.Invalidate(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); titleBar.Invalidate(); }
        public void PaintSurface(Graphics g, Rectangle r) { Glass.Backdrop(g, ClientSize, Point.Empty, r); }
        protected override void OnPaintBackground(PaintEventArgs e) { PaintSurface(e.Graphics, e.ClipRectangle); }
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); Glass.SeeThrough(e.Control); }
        protected int W, Pad, Y;

        public DarkDialog(int width)
        {
            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            Icon = Theme.AppIcon(32);
            W = Theme.P(width);
            Pad = Theme.P(26);
            DoubleBuffered = true;
            titleBar.MinMax = false;
            titleBar.Dock = DockStyle.Top;
            Controls.Add(titleBar);
            Y = titleBar.Height + Theme.P(4);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Frame.Apply(this); }

        protected override bool ProcessDialogKey(Keys k)
        {
            if (k == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(k);
        }

        protected Label AddLabel(string text, Font f, Color c, int x, int width)
        {
            var l = new Label();
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.BackColor = Color.Transparent;
            l.UseCompatibleTextRendering = true;
            using (var g = CreateGraphics())
                l.SetBounds(x, Y, width, (int)Math.Ceiling(g.MeasureString(text, f, width).Height) + Theme.P(2));
            Controls.Add(l);
            Y = l.Bottom;
            return l;
        }

        protected void Header(string title, string sub)
        {
            var logo = new PictureBox();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Image = Theme.Logo();
            logo.SetBounds(Pad, Y, Theme.P(52), Theme.P(52));
            Controls.Add(logo);
            int tx = Pad + Theme.P(66);
            int y0 = Y;
            Y = y0 + Theme.P(2);
            AddLabel(title, new Font(Font.FontFamily, 14f, FontStyle.Bold), Theme.Text, tx, W - tx - Pad);
            AddLabel(sub, Font, Theme.Sub, tx, W - tx - Pad);
            Y = Math.Max(Y, y0 + Theme.P(52)) + Theme.P(18);
        }
    }

    // ------------------------------------------------------------------ İlk açılış rehberi
    class WelcomeDialog : DarkDialog
    {
        public WelcomeDialog() : base(560)
        {
            Header(L.T("Hoş geldin", "Welcome"), AppInfo.Name + " " + AppInfo.Version + L.T(" · diskin bozuk kısmını kullanım dışı bırakır", " · keeps a disk's broken part out of use"));

            var steps = new StepsView();
            steps.Font = Font;
            steps.Steps = new[]
            {
                new[] { L.T("Diski seç", "Pick the disk"), L.T("Üstte model ve seri numarasıyla listelenir. Windows'un kurulu olduğu disk korunur, bölümlenemez.", "Disks are listed with model and serial number. The Windows disk is protected and can't be partitioned.") },
                new[] { L.T("Tara ya da log ekle", "Scan or add a log"), L.T("Hızlı tarama birkaç dakika sürer ve diske hiçbir şey yazmaz. Elinde format log'u varsa onu da içe aktarabilirsin.", "A quick scan takes a few minutes and never writes to the disk. You can also import a format log you already have.") },
                new[] { L.T("Haritaya bak", "Look at the map"), L.T("Kırmızı yerler bozuk, çizgili yerler güvenlik payı. Plakada sürükleyerek alan seçip elle de işaretleyebilirsin.", "Red is bad, striped is the safety margin. Drag on the platter to select an area and mark it by hand.") },
                new[] { L.T("Uygula", "Apply"), L.T("Disk silinir ve sadece sağlam kısımlara bölüm açılır. Bozuk bölge ayrılmamış kalır, Windows oraya yazmaz.", "The disk is erased and partitions are created only on the healthy parts. The bad area stays unallocated.") },
            };
            steps.SetBounds(Pad, Y, W - Pad * 2, steps.Measure(W - Pad * 2));
            Controls.Add(steps);
            Y = steps.Bottom + Theme.P(8);

            var warn = new StatusBox();
            warn.Font = Font;
            warn.Kind = StatusKind.Warn;
            warn.Text = L.T("Bu araç diski onarmaz. Bozuk sektörleri artan bir disk genelde ölmektedir; üzerine önemli hiçbir şey koyma.",
                            "This tool doesn't repair the disk. A disk with growing bad sectors is usually dying; don't keep anything important on it.");
            warn.SetBounds(Pad, Y, W - Pad * 2, warn.Measure(W - Pad * 2));
            Controls.Add(warn);
            Y = warn.Bottom + Theme.P(20);

            var ok = new FlatButton(L.T("Başlayalım", "Let's start"), BtnKind.Primary);
            ok.Font = Font;
            ok.SetBounds(W - Pad - Theme.P(170), Y, Theme.P(170), Theme.P(42));
            ok.Click += delegate { Close(); };
            Controls.Add(ok);
            Y += Theme.P(42) + Pad;
            ClientSize = new Size(W, Y);
        }
    }

    // ------------------------------------------------------------------ Hakkında
    class AboutDialog : DarkDialog
    {
        readonly Label lblUpdate;
        readonly FlatButton btnUpdate;
        string newUrl;

        public AboutDialog() : base(500)
        {
            Header(AppInfo.Name, L.T("Sürüm ", "Version ") + AppInfo.Version);
            AddLabel(L.T("Bozuk sektörleri olan bir hard diskin hatalı bölgesini bulur, çevresine güvenlik payı bırakır ve diski sadece sağlam kalan kısımlarda bölümler.",
                         "Finds the broken area of a hard disk, leaves a safety margin around it and partitions only the healthy parts."), Font, Theme.Text, Pad, W - Pad * 2);
            Y += Theme.P(12);
            AddLabel("Copyright © 2026 HErkanuzun · MIT License", Font, Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(4);

            var link = new LinkLabel();
            link.Text = AppInfo.Repo.Replace("https://", "");
            link.LinkColor = Theme.Accent;
            link.ActiveLinkColor = Theme.AccentHover;
            link.VisitedLinkColor = Theme.Accent;
            link.BackColor = Color.Transparent;
            link.AutoSize = true;
            link.Location = new Point(Pad, Y);
            link.LinkClicked += delegate { Open(AppInfo.Repo); };
            Controls.Add(link);
            Y += Theme.P(32);

            AddLabel(L.T("Veriler: ", "Data: ") + System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrokenDiskResizer"),
                     new Font(Font.FontFamily, 8.5f), Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(18);

            lblUpdate = AddLabel(" ", Font, Theme.Sub, Pad, W - Pad * 2);
            Y += Theme.P(10);

            btnUpdate = new FlatButton(L.T("Güncellemeleri denetle", "Check for updates"), BtnKind.Secondary);
            btnUpdate.Font = Font;
            btnUpdate.SetBounds(Pad, Y, Theme.P(210), Theme.P(40));
            btnUpdate.Click += delegate { CheckUpdate(); };
            Controls.Add(btnUpdate);

            var ok = new FlatButton(L.T("Kapat", "Close"), BtnKind.Primary);
            ok.Font = Font;
            ok.SetBounds(W - Pad - Theme.P(130), Y, Theme.P(130), Theme.P(40));
            ok.Click += delegate { Close(); };
            Controls.Add(ok);
            Y += Theme.P(40) + Pad;
            ClientSize = new Size(W, Y);
        }

        static void Open(string url)
        {
            try { System.Diagnostics.Process.Start(url); } catch { }
        }

        // Sadece kullanıcı isteyince GitHub'daki son sürüme bakılır
        void CheckUpdate()
        {
            if (newUrl != null) { Open(newUrl); return; }
            btnUpdate.Enabled = false;
            lblUpdate.ForeColor = Theme.Sub;
            lblUpdate.Text = L.T("Denetleniyor…", "Checking…");
            var t = new System.Threading.Thread(delegate ()
            {
                string tag = null, err = null;
                try { tag = Updates.LatestTag(); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        btnUpdate.Enabled = true;
                        if (err != null || tag == null)
                        {
                            lblUpdate.Text = L.T("Denetlenemedi. İnternet bağlantısını kontrol et ya da GitHub sayfasına bak.", "Couldn't check. Check your connection or visit the GitHub page.");
                            return;
                        }
                        if (Updates.IsNewer(tag, AppInfo.Version))
                        {
                            lblUpdate.ForeColor = Theme.Accent;
                            lblUpdate.Text = L.T("Yeni sürüm var: ", "A new version is available: ") + tag;
                            newUrl = AppInfo.Repo + "/releases/latest";
                            btnUpdate.Text = L.T("İndirme sayfasını aç", "Open download page");
                        }
                        else lblUpdate.Text = L.T("En son sürümü kullanıyorsun.", "You're on the latest version.");
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    static class Updates
    {
        public static string LatestTag()
        {
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;   // TLS 1.2
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(AppInfo.Repo.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases/latest");
            req.UserAgent = "BrokenDiskResizer/" + AppInfo.Version;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            using (var resp = req.GetResponse())
            using (var rd = new System.IO.StreamReader(resp.GetResponseStream()))
            {
                var m = System.Text.RegularExpressions.Regex.Match(rd.ReadToEnd(), "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
        }

        public static bool IsNewer(string tag, string mine)
        {
            Version a, b;
            if (!Version.TryParse(Norm(tag), out a) || !Version.TryParse(Norm(mine), out b)) return false;
            return a > b;
        }

        static string Norm(string v)
        {
            v = v.Trim().TrimStart('v', 'V');
            int i = v.IndexOfAny(new[] { '-', ' ', '+' });
            if (i > 0) v = v.Substring(0, i);
            if (v.IndexOf('.') < 0) v += ".0";
            return v;
        }
    }
}
