using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BrokenDiskResizer
{
    class MainForm : Form
    {
        readonly Settings cfg;
        readonly ViewState vs = new ViewState();
        List<DiskInfo> disks = new List<DiskInfo>();
        DiskInfo cur;
        ScanMap map;
        Plan plan;
        Scanner scanner, lastScanner;
        volatile bool scanDirty;
        string progressText = "";
        bool busy;   // bölümleme sürüyor
        readonly StringBuilder logText = new StringBuilder();

        readonly BufferedPanel page = new BufferedPanel();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();

        PictureBox logo;
        Label lblTitle, lblSub, secDisk, secScan, secHealth, secLegend, secZones, secBuffer, secPlan, secRecord, lblHint, lblSel, lblProgress, lblBuf;
        FlatButton btnAbout, btnReport;
        HealthCard health;
        SmartInfo smart;
        int smartReq;
        double scanFrac;
        public bool ExitRequested;     // kurulum paketi ya da ikinci kopya kapanmasını istedi
        readonly System.Windows.Forms.Timer deviceTimer = new System.Windows.Forms.Timer();
        Seg segLang, segDisk, segMode;
        FlatButton btnRefresh, btnMark, btnScanSel, btnClearSel, btnScan, btnResume, btnImport, btnReset, btnMinus, btnPlus, btnSave, btnApply;
        StatusBox info, status;
        PlatterView platter;
        StripView strip;
        Legend legend;
        RowList zones, parts;
        FlatSlider slBuf;
        Toggle tglVerySlow, tglUnscanned;
        TextBox logBox;

        public MainForm(Settings cfg)
        {
            this.cfg = cfg;
            Text = AppInfo.Name + " " + AppInfo.Version;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            Icon = Theme.AppIcon(32);
            StartPosition = FormStartPosition.CenterScreen;
            using (var g = CreateGraphics()) Theme.S = Math.Max(1f, g.DpiX / 96f);

            page.Dock = DockStyle.Fill;
            page.AutoScroll = true;
            page.BackColor = Theme.Bg;
            page.Resize += delegate { DoLayout(); };
            page.Scroll += delegate { page.Invalidate(); };
            Controls.Add(page);

            vs.SelectionChanged += delegate { UpdateSel(); platter.Invalidate(); strip.Invalidate(); };

            BuildUi();
            InitialSize();

            tick.Interval = 250;
            tick.Tick += delegate { OnTick(); };
            tick.Start();

            // Disk takılıp çıkarılınca Windows birkaç bildirim yollar; son bildirimden 1,5 sn sonra liste yenilenir
            deviceTimer.Interval = 1500;
            deviceTimer.Tick += delegate
            {
                deviceTimer.Stop();
                if (scanner != null || busy) return;
                // Sadece disk listesi gerçekten değiştiyse yenilenir (seçim ve harita yerinde kalsın)
                List<DiskInfo> now;
                try { now = Disks.List(); } catch { return; }
                if (Signature(now) == Signature(disks)) return;
                bool lost = cur != null && !now.Exists(x => x.Key == cur.Key);
                if (lost) Log(L.T("Seçili disk Windows'tan kayboldu: ", "The selected disk disappeared from Windows: ") + cur.Model);
                else Log(L.T("Disk listesi değişti, yenilendi.", "The disk list changed and was refreshed."));
                RefreshDisks();
            };
            KeyPreview = true;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DEVICECHANGE = 0x0219;
            if (m.Msg == WM_DEVICECHANGE)
            {
                int ev = m.WParam.ToInt32();
                if (ev == 0x0007 || ev == 0x8000 || ev == 0x8004) { deviceTimer.Stop(); deviceTimer.Start(); }
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == Keys.Escape && vs.HasSel) { vs.ClearSel(); return true; }
            if (key == Keys.F5 && scanner == null && !busy) { RefreshDisks(); return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        // Diskler pencere tanıtıcısı oluştuktan sonra okunur (arka plandaki SMART okuması sonucu pencereye geri yollayabilsin)
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshDisks();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!cfg.Welcomed)
            {
                cfg.Welcomed = true;
                cfg.Save();
                BeginInvoke((MethodInvoker)delegate { using (var w = new WelcomeDialog()) w.ShowDialog(this); });
            }
        }

        // Kurulum paketi güncellemeden önce ya da --exit ile çağrılınca: tarama durdurulur, kaydedilir, kapanılır.
        // Bölümleme sürüyorsa kapanılmaz (yarım kalan bölümleme diski bozuk bırakır).
        public void RequestExit()
        {
            if (busy) return;
            ExitRequested = true;
            if (scanner != null) scanner.Stop();   // tarama bitince OnScanEnd pencereyi kapatır
            else Close();
        }

        // İkinci kez açılınca bu pencere öne gelir
        public void BringToFront2()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitle(this);
        }

        static int P(float v) { return Theme.P(v); }

        void InitialSize()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            var frame = SizeFromClientSize(Size.Empty);
            MinimumSize = SizeFromClientSize(new Size(P(420), P(420)));
            ClientSize = new Size(Math.Min(P(1180), wa.Width - frame.Width), Math.Min(P(920), wa.Height - frame.Height));

            // Son pencere konumu (hâlâ bir ekranın içindeyse)
            try
            {
                var p = cfg.Window.Split(',');
                if (p.Length == 5)
                {
                    var r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                    bool visible = false;
                    foreach (var sc in Screen.AllScreens)
                        if (sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(1, r.Width - 80), 40))) visible = true;
                    if (visible && r.Width >= MinimumSize.Width && r.Height >= MinimumSize.Height)
                    {
                        StartPosition = FormStartPosition.Manual;
                        Bounds = r;
                        if (p[4] == "1") WindowState = FormWindowState.Maximized;
                    }
                }
            }
            catch { }
        }

        void SavePlacement()
        {
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            cfg.Window = r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + (WindowState == FormWindowState.Maximized ? "1" : "0");
            cfg.Save();
        }

        // ------------------------------------------------------------------ Arayüz kurulumu
        Label Section(string text)
        {
            var l = new Label();
            l.Text = L.Upper(text);
            l.ForeColor = Theme.Sub;
            l.BackColor = Theme.Bg;
            l.Font = new Font(Font.FontFamily, 8f, FontStyle.Bold);
            l.AutoSize = true;
            page.Controls.Add(l);
            return l;
        }

        Label Text2(string text, Color c)
        {
            var l = new Label();
            l.Text = text;
            l.ForeColor = c;
            l.BackColor = Theme.Bg;
            l.Font = new Font(Font.FontFamily, 8.5f);
            page.Controls.Add(l);
            return l;
        }

        T Add<T>(T c) where T : Control
        {
            c.Font = Font;
            page.Controls.Add(c);
            return c;
        }

        FlatButton Btn(string text, BtnKind kind, EventHandler click)
        {
            var b = Add(new FlatButton(text, kind));
            b.Click += click;
            return b;
        }

        void BuildUi()
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9.5f);

            logo = new PictureBox();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Theme.Bg;
            logo.Image = Theme.Logo();
            page.Controls.Add(logo);

            lblTitle = new Label();
            lblTitle.Text = AppInfo.Name;
            lblTitle.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
            lblTitle.ForeColor = Theme.Text;
            lblTitle.AutoSize = true;
            page.Controls.Add(lblTitle);

            lblSub = new Label();
            lblSub.Text = L.T("Diskin bozuk kısmını bul, Windows'un dışında bırak", "Find the broken part of a disk and keep Windows away from it");
            lblSub.ForeColor = Theme.Sub;
            lblSub.AutoSize = true;
            page.Controls.Add(lblSub);

            segLang = Add(new Seg());
            segLang.Items = new[] { "TR", "EN" };
            segLang.Font = new Font("Segoe UI", 8.5f);
            segLang.Selected = Math.Max(0, Array.IndexOf(L.Codes, L.Lang));
            segLang.Changed += delegate (int i)
            {
                if (scanner != null || busy) { segLang.Selected = Array.IndexOf(L.Codes, L.Lang); return; }
                cfg.Lang = L.Codes[i];
                cfg.Save();
                L.Set(cfg.Lang);
                BeginInvoke((MethodInvoker)RebuildUi);
            };

            btnAbout = Btn(L.T("Hakkında", "About"), BtnKind.Ghost, delegate { using (var a = new AboutDialog()) a.ShowDialog(this); });

            // --- Disk
            secDisk = Section(L.T("Disk", "Disk"));
            btnRefresh = Btn(L.T("↻ Diskleri yenile", "↻ Refresh disks"), BtnKind.Ghost, delegate { RefreshDisks(); });
            segDisk = Add(new Seg());
            segDisk.Changed += delegate (int i) { if (i >= 0 && i < disks.Count) SelectDisk(disks[i]); };
            info = Add(new StatusBox());

            platter = Add(new PlatterView(vs));
            lblHint = Text2(L.T("Plakada ya da alttaki şeritte basılı tutup sürükleyerek alan seç. Dış halka diskin başı, içe doğru sonu. Daire bir şemadır; gerçek plakadaki yerleşim farklıdır.",
                                "Press and drag on the platter or the bar below to select an area. The outer ring is the start of the disk, inner rings are the end. The circle is a schematic; the real layout on the platters differs."), Theme.Sub);
            strip = Add(new StripView(vs));

            lblSel = Text2("", Theme.Text);
            lblSel.Font = Font;
            btnMark = Btn(L.T("Bozuk olarak işaretle", "Mark as bad"), BtnKind.Secondary, delegate { MarkSelection(); });
            btnScanSel = Btn(L.T("Seçimi tam tara", "Scan selection"), BtnKind.Secondary, delegate { StartScan(ScanMode.Full, true, false); });
            btnClearSel = Btn(L.T("Seçimi kaldır", "Clear selection"), BtnKind.Ghost, delegate { vs.ClearSel(); });

            // --- Tarama
            secScan = Section(L.T("Tarama (salt okunur, diske yazmaz)", "Scan (read-only, never writes)"));
            segMode = Add(new Seg());
            segMode.Items = new[] { L.T("Hızlı · birkaç dakika", "Quick · a few minutes"), L.T("Tam · saatler", "Full · hours") };
            segMode.Selected = 0;
            btnScan = Btn(L.T("Taramayı başlat", "Start scan"), BtnKind.Primary, delegate
            {
                if (scanner != null) { scanner.Stop(); return; }
                StartScan(segMode.Selected == 0 ? ScanMode.Quick : ScanMode.Full, false, false);
            });
            btnResume = Btn(L.T("Kaldığı yerden devam et", "Resume"), BtnKind.Secondary, delegate { StartScan(ScanMode.Full, false, true); });
            lblProgress = Text2("", Theme.Sub);
            btnImport = Btn(L.T("Format/tarama log'u içe aktar…", "Import format/scan log…"), BtnKind.Secondary, delegate { ImportLog(); });
            btnReset = Btn(L.T("Haritayı sıfırla", "Reset map"), BtnKind.DangerGhost, delegate { ResetMap(); });

            // --- Sağ sütun
            secHealth = Section(L.T("Diskin sağlığı (SMART)", "Disk health (SMART)"));
            health = Add(new HealthCard());
            health.Info = smart;

            secLegend = Section(L.T("Renkler", "Colours"));
            legend = Add(new Legend());

            secZones = Section(L.T("Bozuk bölgeler", "Bad areas"));
            zones = Add(new RowList());
            zones.Empty = L.T("Henüz bozuk bölge yok. Diski tara ya da log içe aktar.", "No bad areas yet. Scan the disk or import a log.");
            zones.Removed += delegate (object tag)
            {
                var z = tag as Zone;
                if (z == null || map == null) return;
                map.Marks.Remove(z);
                map.Save();
                Changed();
            };
            zones.Picked += delegate (object tag) { var z = tag as Zone; if (z != null) vs.SetSel(z.Start, z.End); };

            secBuffer = Section(L.T("Güvenlik payı", "Safety margin"));
            btnMinus = Btn("−", BtnKind.Secondary, delegate { slBuf.Value = slBuf.Value - 1; OnBuffer(slBuf.Value); });
            btnPlus = Btn("+", BtnKind.Secondary, delegate { slBuf.Value = slBuf.Value + 1; OnBuffer(slBuf.Value); });
            lblBuf = Text2("", Theme.Text);
            lblBuf.Font = new Font(Font, FontStyle.Bold);
            lblBuf.TextAlign = ContentAlignment.MiddleCenter;
            slBuf = Add(new FlatSlider());
            slBuf.Minimum = 0;
            slBuf.Maximum = 30;
            slBuf.Value = (int)Math.Round(cfg.BufferPct);
            slBuf.ValueChanged += OnBuffer;

            tglVerySlow = Add(new Toggle(L.T("Çok yavaş okunan yerleri de bozuk say", "Treat very slow areas as bad")));
            tglVerySlow.Checked = cfg.CountVerySlow;
            tglVerySlow.Changed += delegate (bool v) { cfg.CountVerySlow = v; cfg.Save(); Changed(); };
            tglUnscanned = Add(new Toggle(L.T("Taranmamış yerleri kullanma", "Don't use areas that weren't scanned")));
            tglUnscanned.Checked = cfg.ExcludeUnscanned;
            tglUnscanned.Changed += delegate (bool v) { cfg.ExcludeUnscanned = v; cfg.Save(); Changed(); };

            secPlan = Section(L.T("Açılacak bölümler", "Partitions to create"));
            parts = Add(new RowList());
            parts.Empty = L.T("Kullanılabilir alan yok.", "No usable space.");
            parts.MaxRows = 6;
            parts.MidW = 170;
            parts.Picked += delegate (object tag) { if (tag is Span) { var s = (Span)tag; vs.SetSel(s.Start, s.End); } };

            status = Add(new StatusBox());
            btnSave = Btn(L.T("Betiği kaydet", "Save script"), BtnKind.Secondary, delegate { SaveScript(); });
            btnApply = Btn(L.T("Uygula", "Apply"), BtnKind.Primary, delegate { Apply(); });

            secRecord = Section(L.T("Kayıt", "Log"));
            btnReport = Btn(L.T("Raporu kaydet…", "Save report…"), BtnKind.Ghost, delegate { SaveReport(); });
            logBox = new TextBox();
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.None;
            logBox.BackColor = Theme.Card;
            logBox.ForeColor = Theme.Sub;
            logBox.Font = new Font("Consolas", 8.5f);
            logBox.Text = logText.ToString();
            page.Controls.Add(logBox);

            ResumeLayout();
            UpdateBufferLabel();
            DoLayout();
        }

        void RebuildUi()
        {
            SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in page.Controls) old.Add(c);
            page.Controls.Clear();
            foreach (var c in old) c.Dispose();
            BuildUi();
            FillDiskSeg();
            UpdateAll();
            ResumeLayout();
        }

        // ------------------------------------------------------------------ Yerleşim
        void DoLayout()
        {
            if (status == null) return;
            int W = page.ClientSize.Width;
            int pad = P(28), gap = P(36);
            bool two = W >= P(1000);
            // geniş ekranda içerik en fazla P(1240) genişlikte ve ortada
            int inner = Math.Min(P(1240), W - pad * 2), ox = (W - inner) / 2;
            int lw, rw, rx;
            if (two)
            {
                int avail = inner - gap;
                lw = (int)(avail * 0.57f);
                rw = avail - lw;
                rx = ox + lw + gap;
            }
            else { lw = rw = inner; rx = ox; }
            int x = ox, oy = page.AutoScrollPosition.Y, y = pad + oy;

            // Başlık
            logo.SetBounds(x, y, P(48), P(48));
            lblTitle.Location = new Point(x + P(62), y - P(1));
            lblSub.Location = new Point(x + P(64), y + P(30));
            btnAbout.SetBounds(x + lw - P(86), y + P(6), P(86), P(32));
            segLang.SetBounds(x + lw - P(86) - P(104), y + P(6), P(96), P(32));
            y += P(66);

            secDisk.Location = new Point(x, y);
            btnRefresh.SetBounds(x + lw - P(150), y - P(6), P(150), P(26));
            y += P(22);
            segDisk.SetBounds(x, y, lw, P(40));
            y += P(48);
            info.Width = lw;
            info.SetBounds(x, y, lw, info.Measure(lw));
            y = info.Bottom + P(10);

            int ph = Math.Min(lw, two ? Math.Max(P(360), page.ClientSize.Height - P(420)) : P(520));
            ph = Math.Max(P(300), Math.Min(ph, P(560)));
            platter.SetBounds(x, y, lw, ph);
            y += ph + P(6);
            lblHint.SetBounds(x, y, lw, Measure(lblHint, lw));
            y = lblHint.Bottom + P(8);
            strip.SetBounds(x, y, lw, P(62));
            y += P(68);

            lblSel.SetBounds(x, y, lw, P(22));
            y += P(26);
            int bw = (lw - P(16)) / 3;
            btnMark.SetBounds(x, y, bw, P(36));
            btnScanSel.SetBounds(x + bw + P(8), y, bw, P(36));
            btnClearSel.SetBounds(x + (bw + P(8)) * 2, y, lw - (bw + P(8)) * 2, P(36));
            y += P(50);

            secScan.Location = new Point(x, y);
            y += P(22);
            segMode.SetBounds(x, y, lw, P(40));
            y += P(48);
            int hb = (lw - P(8)) / 2;
            btnScan.SetBounds(x, y, hb, P(42));
            btnResume.SetBounds(x + hb + P(8), y, lw - hb - P(8), P(42));
            y += P(48);
            lblProgress.SetBounds(x, y, lw, Measure(lblProgress, lw));
            y = lblProgress.Bottom + P(8);
            btnImport.SetBounds(x, y, hb, P(36));
            btnReset.SetBounds(x + hb + P(8), y, lw - hb - P(8), P(36));
            y += P(36);
            int leftBottom = y;

            // Sağ sütun
            int ry = two ? pad + oy + P(66) : y + P(28);
            int r = rx;
            secHealth.Location = new Point(r, ry);
            ry += P(22);
            health.SetBounds(r, ry, rw, health.Measure(rw));
            ry = health.Bottom + P(14);

            secLegend.Location = new Point(r, ry);
            ry += P(22);
            legend.SetBounds(r, ry, rw, legend.Measure(rw));
            ry = legend.Bottom + P(14);

            secZones.Location = new Point(r, ry);
            ry += P(22);
            zones.SetBounds(r, ry, rw, zones.Measure());
            ry = zones.Bottom + P(16);

            secBuffer.Location = new Point(r, ry);
            ry += P(22);
            btnMinus.SetBounds(r, ry, P(40), P(40));
            lblBuf.SetBounds(r + P(44), ry, P(150), P(40));
            btnPlus.SetBounds(r + P(198), ry, P(40), P(40));
            slBuf.SetBounds(r + P(250), ry, Math.Max(P(60), rw - P(250)), P(40));
            if (rw < P(380))
            {
                slBuf.SetBounds(r, ry + P(44), rw, P(36));
                ry += P(40);
            }
            ry += P(48);
            tglVerySlow.SetBounds(r, ry, rw, P(30));
            ry += P(32);
            tglUnscanned.SetBounds(r, ry, rw, P(30));
            ry += P(42);

            secPlan.Location = new Point(r, ry);
            ry += P(22);
            parts.SetBounds(r, ry, rw, parts.Measure());
            ry = parts.Bottom + P(12);
            status.SetBounds(r, ry, rw, status.Measure(rw));
            ry = status.Bottom + P(12);
            int sb = (rw - P(8)) * 2 / 5;
            btnSave.SetBounds(r, ry, sb, P(44));
            btnApply.SetBounds(r + sb + P(8), ry, rw - sb - P(8), P(44));
            ry += P(58);

            secRecord.Location = new Point(r, ry);
            btnReport.SetBounds(r + rw - P(140), ry - P(6), P(140), P(26));
            ry += P(22);
            int lh = Math.Max(P(110), two ? leftBottom - ry : P(160));
            logBox.SetBounds(r, ry, rw, lh);
            ry += lh;

            int bottom = Math.Max(leftBottom, ry) - oy + pad;
            if (page.AutoScrollMinSize.Height != bottom) page.AutoScrollMinSize = new Size(0, bottom);
        }

        static int Measure(Label l, int w)
        {
            using (var g = l.CreateGraphics())
                return (int)Math.Ceiling(g.MeasureString(l.Text.Length == 0 ? " " : l.Text, l.Font, w).Height) + 2;
        }

        // ------------------------------------------------------------------ Diskler
        void RefreshDisks()
        {
            if (scanner != null || busy) return;
            string keep = cur != null ? cur.Key : cfg.LastDisk;
            try { disks = Disks.List(); }
            catch (Exception ex) { disks = new List<DiskInfo>(); Log(L.T("Diskler okunamadı: ", "Could not list disks: ") + ex.Message); }
            FillDiskSeg();
            DiskInfo pick = null;
            foreach (var d in disks) if (d.Key == keep) pick = d;
            if (pick == null) foreach (var d in disks) if (!d.Protected) { pick = d; break; }
            if (pick == null && disks.Count > 0) pick = disks[0];
            if (pick != null) SelectDisk(pick);
            else { cur = null; map = null; vs.Map = null; UpdateAll(); }
        }

        static string KindName(ZoneKind k)
        {
            switch (k)
            {
                case ZoneKind.Scan: return L.T("tarama", "scan");
                case ZoneKind.Log: return "log";
                case ZoneKind.Dropped: return L.T("disk düştü", "dropout");
                default: return L.T("elle", "manual");
            }
        }

        static string Signature(List<DiskInfo> list)
        {
            var sb = new StringBuilder();
            foreach (var d in list) sb.Append(d.Number).Append(':').Append(d.Key).Append(':').Append(d.Size).Append('|');
            return sb.ToString();
        }

        void FillDiskSeg()
        {
            var items = new string[disks.Count];
            for (int i = 0; i < disks.Count; i++)
            {
                var d = disks[i];
                string model = d.Model.Length > 18 ? d.Model.Substring(0, 18) + "…" : d.Model;
                items[i] = d.Number + " · " + model + " · " + Fmt.Bytes(d.Size).Replace(",00 ", " ").Replace(".00 ", " ");
            }
            segDisk.Items = items;
            segDisk.Selected = cur == null ? -1 : disks.FindIndex(d => d.Key == cur.Key);
        }

        void SelectDisk(DiskInfo d)
        {
            if (scanner != null || busy) return;
            cur = d;
            cfg.LastDisk = d.Key;
            cfg.Save();
            map = ScanMap.Load(d);
            vs.Map = map;
            vs.ClearSel();
            vs.Head = -1;
            segDisk.Selected = disks.IndexOf(d);
            progressText = "";
            UpdateAll();
            LoadSmart(d);
        }

        // SMART okuması birkaç saniye sürebilir; arayüz donmasın diye arka planda yapılır
        void LoadSmart(DiskInfo d)
        {
            int req = ++smartReq;
            smart = null;
            health.Info = null;
            health.Loading = true;
            health.Invalidate();
            var t = new Thread(delegate ()
            {
                SmartInfo s = null;
                try { s = Smart.Read(d); } catch { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (req != smartReq || health.IsDisposed) return;
                        smart = s;
                        health.Info = s;
                        health.Loading = false;
                        health.Invalidate();
                        if (s != null && s.Ok)
                            Log("SMART: " + L.T("yeniden atanmış ", "reallocated ") + s.Reallocated + ", " + L.T("bekleyen ", "pending ") + s.Pending +
                                ", " + L.T("düzeltilemeyen ", "uncorrectable ") + s.Offline + (s.Temperature >= 0 ? ", " + s.Temperature + " °C" : ""));
                        DoLayout();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ------------------------------------------------------------------ Güncellemeler
        void Changed()
        {
            BuildPlan();
            vs.Version++;
            UpdateAll();
        }

        void BuildPlan()
        {
            if (map == null) { plan = null; vs.Plan = null; return; }
            var o = new PlanOptions();
            o.BufferPct = cfg.BufferPct;
            o.CountVerySlow = cfg.CountVerySlow;
            o.ExcludeUnscanned = cfg.ExcludeUnscanned;
            plan = Plan.Build(map, o);
            vs.Plan = plan;
        }

        void UpdateAll()
        {
            if (status == null) return;
            BuildPlan();
            vs.Version++;
            UpdateInfo();
            UpdateSel();
            UpdateZones();
            UpdateParts();
            UpdateBufferLabel();
            UpdateStatus();
            UpdateScanUi();
            platter.Invalidate();
            strip.Invalidate();
            DoLayout();
        }

        void UpdateInfo()
        {
            if (cur == null)
            {
                info.Kind = StatusKind.Warn;
                info.Text = L.T("Disk bulunamadı. Programı yönetici olarak çalıştırdığından emin ol, sonra Diskleri yenile'ye bas.",
                                "No disks found. Make sure the program runs as administrator, then press Refresh disks.");
                info.Invalidate();
                return;
            }
            var bits = new List<string>();
            bits.Add(cur.Model);
            bits.Add(Fmt.Bytes(cur.Size));
            if (cur.Serial != "") bits.Add(L.T("Seri ", "Serial ") + cur.Serial);
            bits.Add(cur.Letters.Count > 0 ? string.Join(" ", cur.Letters.ConvertAll(c => c + ":").ToArray()) : L.T("sürücü harfi yok", "no drive letter"));
            if (cur.IsOffline) bits.Add(L.T("çevrimdışı", "offline"));
            if (cur.Health == 1) bits.Add(L.T("Windows: uyarı", "Windows: warning"));
            if (cur.Health == 2) bits.Add(L.T("Windows: sağlıksız", "Windows: unhealthy"));
            string t = string.Join("  ·  ", bits.ToArray());
            if (cur.Protected)
            {
                info.Kind = StatusKind.Warn;
                t += "\n" + L.T("Bu, Windows'un çalıştığı disk. Taranabilir ama bölümlenemez.", "This is the disk Windows runs from. It can be scanned but not partitioned.");
            }
            else info.Kind = cur.Health == 2 || cur.Health == 1 ? StatusKind.Warn : StatusKind.Idle;
            info.Text = t;
            info.Invalidate();
        }

        void UpdateSel()
        {
            if (lblSel == null) return;
            bool has = vs.HasSel && scanner == null && !busy;
            if (vs.HasSel)
            {
                var st = map.Worst(vs.SelA, vs.SelB);
                lblSel.Text = L.T("Seçim: ", "Selection: ") + Fmt.GB(vs.SelA) + " – " + Fmt.GB(vs.SelB) + "  (" + Fmt.Bytes(vs.SelB - vs.SelA) + ")  ·  " + ViewState.StateName(st);
            }
            else lblSel.Text = L.T("Seçim yok", "Nothing selected");
            btnMark.Enabled = has;
            btnScanSel.Enabled = has;
            btnClearSel.Enabled = vs.HasSel;
        }

        void UpdateZones()
        {
            zones.Rows.Clear();
            if (plan != null)
                foreach (var z in plan.Bad)
                {
                    var row = new RowList.Row();
                    row.Dot = Theme.Danger;
                    row.Left = z.Length < 1000000000L ? Fmt.GB(z.Start) : Fmt.GB(z.Start) + " – " + Fmt.GB(z.End);
                    row.Mid = Fmt.Bytes(z.Length);
                    row.Right = KindName(z.Kind);
                    row.Removable = z.Kind != ZoneKind.Scan;
                    row.Tag = z;
                    zones.Rows.Add(row);
                }
            zones.Invalidate();
        }

        void UpdateParts()
        {
            parts.Rows.Clear();
            if (plan != null)
            {
                int n = 0;
                foreach (var u in plan.Usable)
                {
                    n++;
                    var row = new RowList.Row();
                    row.Dot = Theme.Accent;
                    row.Left = L.T("Bölüm ", "Partition ") + n;
                    row.Mid = Fmt.GB(u.Start) + " – " + Fmt.GB(u.End);
                    row.Right = Fmt.Bytes(u.Length);
                    row.Tag = u;
                    parts.Rows.Add(row);
                }
            }
            parts.Invalidate();
        }

        void OnBuffer(int v)
        {
            cfg.BufferPct = v;
            cfg.Save();
            UpdateBufferLabel();
            Changed();
        }

        void UpdateBufferLabel()
        {
            if (lblBuf == null) return;
            long b = cur == null ? 0 : (long)(cur.Size * cfg.BufferPct / 100);
            lblBuf.Text = (L.Lang == "tr" ? "%" + (int)cfg.BufferPct : (int)cfg.BufferPct + "%") + "  ·  " + Fmt.Bytes(b);
            secBuffer.Text = L.Upper(L.T("Güvenlik payı · bozuk bölgenin iki yanına", "Safety margin · on both sides of each bad area"));
        }

        void UpdateStatus()
        {
            btnApply.Enabled = false;
            btnSave.Enabled = false;
            if (cur == null || plan == null)
            {
                status.Kind = StatusKind.Idle;
                status.Text = L.T("Disk seçilmedi.", "No disk selected.");
            }
            else if (scanner != null)
            {
                status.Kind = StatusKind.Idle;
                status.Text = L.T("Tarama sürüyor. Plan tarama bitince netleşir.", "Scanning. The plan settles when the scan ends.");
            }
            else if (busy)
            {
                status.Kind = StatusKind.Warn;
                status.Text = L.T("Bölümler oluşturuluyor… Programı kapatma.", "Creating partitions… Don't close the program.");
            }
            else if (map.Coverage == 0 && plan.Bad.Count == 0)
            {
                status.Kind = StatusKind.Idle;
                status.Text = L.T("Önce diski tara (hızlı tarama birkaç dakika sürer) ya da elindeki format log'unu içe aktar.",
                                  "First scan the disk (a quick scan takes a few minutes) or import a format log you already have.");
            }
            else if (plan.Usable.Count == 0)
            {
                status.Kind = StatusKind.Warn;
                status.Text = cfg.ExcludeUnscanned && map.Coverage < 0.999
                    ? L.T("Taranmamış yerler kullanılmıyor ve kullanılabilir alan kalmadı. Diski tara ya da 'Taranmamış yerleri kullanma' seçeneğini kapat.",
                          "Unscanned areas are excluded and no usable space is left. Scan the disk or turn off 'Don't use areas that weren't scanned'.")
                    : L.T("Güvenlik payıyla birlikte kullanılabilir alan kalmadı. Payı küçült ya da bu diski emekliye ayır.",
                          "With the safety margin there is no usable space left. Lower the margin or retire this disk.");
            }
            else
            {
                long excluded = cur.Size - plan.UsableBytes;
                string t = L.T("Plan hazır: ", "Plan ready: ") + plan.Usable.Count + L.T(" bölüm, ", " partition(s), ") + Fmt.Bytes(plan.UsableBytes) +
                           L.T(" kullanılabilir. Kalan ", " usable. The remaining ") + Fmt.Bytes(excluded) +
                           L.T(" ayrılmamış kalır; Windows oraya hiç yazmaz.", " stays unallocated; Windows never writes there.");
                if (map.Coverage < 0.999)
                    t += "\n" + L.T("Uyarı: diskin %", "Note: ") + Fmt.N((1 - map.Coverage) * 100, "0") + L.T("'i hiç taranmadı.", "% of the disk was never scanned.");
                if (cur.Protected)
                    t += "\n" + L.T("Bu Windows diski olduğu için uygulanamaz.", "This is the Windows disk, so it can't be applied.");
                status.Kind = cur.Protected ? StatusKind.Warn : StatusKind.Active;
                status.Text = t;
                btnApply.Enabled = !cur.Protected;
                btnSave.Enabled = true;
            }
            status.Invalidate();
        }

        void UpdateScanUi()
        {
            bool scanning = scanner != null;
            btnScan.Text = scanning ? L.T("Durdur", "Stop") : L.T("Taramayı başlat", "Start scan");
            btnScan.Kind = scanning ? BtnKind.Secondary : BtnKind.Primary;
            btnScan.Enabled = cur != null && !busy;
            btnResume.Enabled = !scanning && !busy && map != null && map.ResumeAt >= 0 && map.ResumeAt < map.Size;
            btnResume.Text = map != null && map.ResumeAt >= 0
                ? L.T("Devam et · ", "Resume · ") + Fmt.GB(map.ResumeAt)
                : L.T("Kaldığı yerden devam et", "Resume");
            segMode.Enabled = !scanning && !busy;
            segDisk.Enabled = !scanning && !busy;
            btnRefresh.Enabled = !scanning && !busy;
            btnImport.Enabled = !scanning && !busy && cur != null;
            btnReset.Enabled = !scanning && !busy && map != null;
            slBuf.Enabled = btnMinus.Enabled = btnPlus.Enabled = !busy;
            string cov = map == null ? "" : L.T("Taranan: %", "Scanned: ") + Fmt.N(map.Coverage * 100, "0") + L.T("", "%");
            lblProgress.Text = progressText.Length > 0 ? progressText : cov;
            UpdateSel();
        }

        void OnTick()
        {
            if (scanner == null || !scanDirty) return;
            scanDirty = false;
            vs.Version++;
            BuildPlan();
            lblProgress.Text = progressText;
            WinShell.Progress(Handle, scanFrac, WinShell.Normal);
            platter.Invalidate();
            strip.Invalidate();
        }

        void Log(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n";
            logText.Append(line);
            if (logBox != null && !logBox.IsDisposed) logBox.AppendText(line);
        }

        // ------------------------------------------------------------------ Tarama
        void StartScan(ScanMode mode, bool selectionOnly, bool resume)
        {
            if (cur == null || map == null || scanner != null || busy) return;
            long from = 0, to = cur.Size;
            if (selectionOnly)
            {
                if (!vs.HasSel) return;
                from = vs.SelA;
                to = vs.SelB;
            }
            else if (resume)
            {
                if (map.ResumeAt < 0) return;
                from = map.ResumeAt;
                to = map.ResumeEnd > from ? map.ResumeEnd : cur.Size;
            }
            if (mode == ScanMode.Full && !resume)
            {
                double hours = (to - from) / 120e6 / 3600;
                if (hours > 0.25 && !ConfirmDialog.Ask(this, L.T("Tam tarama", "Full scan"),
                        L.T("Seçilen alanın her megabaytı okunacak. Tahmini süre: ", "Every megabyte of the area will be read. Estimated time: ") +
                        Fmt.N(Math.Max(0.3, hours), "0.0") + L.T(" saat.\n\nDiske hiçbir şey yazılmaz. İstediğin an durdurabilir, sonra kaldığın yerden devam edebilirsin.",
                                                               " hours.\n\nNothing is written to the disk. You can stop any time and resume later."),
                        null, L.T("Başlat", "Start"), false)) return;
                map.ClearRange(from, to);
                map.ResumeAt = -1;
            }
            if (mode == ScanMode.Quick) map.Marks.RemoveAll(z => z.Kind == ZoneKind.Scan);

            var s = new Scanner(cur, map, mode, from, to);
            var startedAt = DateTime.Now;
            s.Progress += delegate (long off, double rate, TimeSpan eta, BlockState st)
            {
                vs.Head = off;
                double pct = (off - from) * 100.0 / Math.Max(1, to - from);
                scanFrac = pct / 100;
                progressText = (mode == ScanMode.Quick ? L.T("Hızlı tarama: %", "Quick scan: ") : L.T("Tam tarama: %", "Full scan: ")) +
                               Fmt.N(Math.Min(100, pct), "0.0") + (L.Lang == "tr" ? "" : "%") + "  ·  " + Fmt.GB(off) + "  ·  " +
                               (mode == ScanMode.Quick ? Fmt.N(rate, "0") + L.T(" okuma/sn", " reads/s") : Fmt.N(rate, "0") + " MB/s") +
                               "  ·  " + L.T("kalan ", "left ") + Fmt.Time(eta);
                scanDirty = true;
            };
            s.Log += delegate (string m) { BeginInvoke((MethodInvoker)delegate { Log(m); }); };
            s.Finished += delegate (ScanEnd end, string detail)
            {
                try { BeginInvoke((MethodInvoker)delegate { OnScanEnd(end, detail, startedAt); }); }
                catch { }
            };
            scanner = s;
            lastScanner = s;
            progressText = L.T("Başlatılıyor…", "Starting…");
            Log((mode == ScanMode.Quick ? L.T("Hızlı tarama", "Quick scan") : L.T("Tam tarama", "Full scan")) + ": " + Fmt.GB(from) + " – " + Fmt.GB(to));
            vs.Head = from;
            scanFrac = 0;
            WinShell.KeepAwake(true);
            WinShell.Progress(Handle, 0, WinShell.Normal);
            UpdateAll();
            s.Start();
        }

        void OnScanEnd(ScanEnd end, string detail, DateTime startedAt)
        {
            scanner = null;
            vs.Head = -1;
            WinShell.KeepAwake(false);
            WinShell.Progress(Handle, 0, WinShell.NoProgress);
            if (Form.ActiveForm != this && !ExitRequested) WinShell.Flash(Handle);
            var sc = lastScanner;
            if (sc != null && sc.DroppedAt >= 0) map.AddMark(sc.DroppedAt, sc.DroppedAt + Math.Max(sc.DroppedLen, Plan.MiB), ZoneKind.Dropped);
            map.Save();
            if (ExitRequested) { map.Save(); Close(); return; }
            string took = Fmt.Time(DateTime.Now - startedAt);
            switch (end)
            {
                case ScanEnd.Done:
                    progressText = "";
                    Log(L.T("Tarama bitti (", "Scan finished (") + took + ").");
                    break;
                case ScanEnd.Cancelled:
                    progressText = "";
                    Log(L.T("Tarama durduruldu.", "Scan stopped."));
                    break;
                case ScanEnd.OpenFailed:
                    progressText = "";
                    Log(L.T("Disk açılamadı: ", "Could not open the disk: ") + detail);
                    ConfirmDialog.Info(this, L.T("Disk açılamadı", "Could not open the disk"),
                        detail + "\n\n" + L.T("Programın yönetici olarak çalıştığından ve diskin bağlı olduğundan emin ol.", "Make sure the program runs as administrator and the disk is connected."));
                    break;
                case ScanEnd.DeviceLost:
                    progressText = "";
                    Log(L.T("Disk yanıt vermeyi kesti: ", "The disk stopped responding: ") + detail);
                    ConfirmDialog.Info(this, L.T("Disk yanıt vermeyi kesti", "The disk stopped responding"),
                        L.T("Okuma sırasında disk Windows'tan düştü (format log'undaki hatanın aynısı). O bölge bozuk olarak işaretlendi ve harita kaydedildi.\n\n" +
                            "1. Bilgisayarı kapat, diskin SATA ve güç kablosunu çıkarıp tak.\n" +
                            "2. Programı yeniden aç; bu disk ve haritası kendiliğinden gelir.\n" +
                            "3. Tam taramadaysan 'Devam et' ile tarama bozuk yerin biraz ilerisinden sürer.\n\n" +
                            "Disk okuma sırasında düşüyorsa üzerine önemli hiçbir şey koyma.",
                            "The disk dropped off Windows during a read (the same failure as in a format log). That area was marked bad and the map was saved.\n\n" +
                            "1. Shut down, then unplug and replug the disk's SATA and power cables.\n" +
                            "2. Open the program again; this disk and its map come back automatically.\n" +
                            "3. If this was a full scan, Resume continues a little past the bad spot.\n\n" +
                            "A disk that drops out during reads should not hold anything important."));
                    break;
            }
            Changed();
        }

        // ------------------------------------------------------------------ Seçim / log / sıfırlama
        void MarkSelection()
        {
            if (!vs.HasSel || map == null) return;
            map.AddMark(vs.SelA, vs.SelB, ZoneKind.Manual);
            map.Save();
            Log(L.T("Elle bozuk işaretlendi: ", "Marked bad: ") + Fmt.GB(vs.SelA) + " – " + Fmt.GB(vs.SelB));
            vs.ClearSel();
            Changed();
        }

        void ImportLog()
        {
            if (cur == null || map == null) return;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = L.T("Log dosyaları", "Log files") + " (*.txt;*.log)|*.txt;*.log|" + L.T("Tüm dosyalar", "All files") + " (*.*)|*.*";
                dlg.Title = L.T("Format ya da tarama log'unu seç", "Pick a format or scan log");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                LogImport li;
                try { li = LogImport.Parse(File.ReadAllText(dlg.FileName)); }
                catch (Exception ex) { ConfirmDialog.Info(this, L.T("Log okunamadı", "Could not read the log"), ex.Message); return; }
                if (li.BadOffsets.Count == 0)
                {
                    ConfirmDialog.Info(this, L.T("Hatalı konum bulunamadı", "No bad locations found"),
                        L.T("Bu log'da 'error at offset … bytes' biçiminde bir hata satırı yok.", "This log has no lines like 'error at offset … bytes'."));
                    return;
                }
                if (li.DeviceCapacity > 0 && li.DeviceCapacity != cur.Size &&
                    !ConfirmDialog.Ask(this, L.T("Disk boyutu tutmuyor", "Disk size doesn't match"),
                        L.T("Log'daki disk ", "The log's disk is ") + li.DeviceCapacity.ToString("N0") + L.T(" bayt, seçili disk ", " bytes, the selected disk is ") + cur.Size.ToString("N0") +
                        L.T(" bayt. Log başka bir diske ait olabilir.\n\nLog: ", " bytes. The log may belong to another disk.\n\nLog: ") + li.Device +
                        L.T("\nSeçili: ", "\nSelected: ") + cur.Model + "\n\n" + L.T("Yine de eklensin mi?", "Add it anyway?"),
                        null, L.T("Yine de ekle", "Add anyway"), true)) return;
                var zs = li.Zones();
                foreach (var z in zs) map.AddMark(z.Start, z.End, ZoneKind.Log);
                map.Save();
                Log(L.T("Log içe aktarıldı: ", "Log imported: ") + li.BadOffsets.Count + L.T(" hatalı blok, ", " bad blocks, ") + zs.Count + L.T(" bölge.", " area(s)."));
                foreach (var z in zs) Log("  " + Fmt.GB(z.Start) + " – " + Fmt.GB(z.End) + " (" + Fmt.Bytes(z.Length) + ")");
                if (li.Processed > 0 && li.Processed < cur.Size)
                    Log(L.T("Log'a göre işlem ", "According to the log the operation stopped at ") + Fmt.GB(li.Processed) +
                        L.T("'ta durmuş; sonrası hiç denenmemiş. O kısmı tarayarak kontrol et.", "; nothing after it was tried. Scan that part to check it."));
                Changed();
            }
        }

        void ResetMap()
        {
            if (map == null || scanner != null) return;
            if (!ConfirmDialog.Ask(this, L.T("Haritayı sıfırla", "Reset map"),
                    L.T("Bu diskin tarama sonuçları ve işaretli bölgeleri silinir (diskin kendisine dokunulmaz).", "This disk's scan results and marked areas are cleared (the disk itself is untouched)."),
                    null, L.T("Sıfırla", "Reset"), true)) return;
            map.ClearAll();
            map.Marks.Clear();
            map.Save();
            Log(L.T("Harita sıfırlandı.", "Map reset."));
            Changed();
        }

        // ------------------------------------------------------------------ Uygulama
        const string LabelPrefix = "SAGLAM";

        void SaveScript()
        {
            if (cur == null || plan == null || plan.Usable.Count == 0) return;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "diskpart (*.txt)|*.txt";
                dlg.FileName = "BrokenDiskResizer_disk" + cur.Number + ".txt";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, DiskPart.Script(cur, plan, LabelPrefix), Encoding.ASCII);
                Log(L.T("Betik kaydedildi: ", "Script saved: ") + dlg.FileName);
            }
        }

        void Apply()
        {
            if (cur == null || plan == null || plan.Usable.Count == 0 || scanner != null || busy) return;
            if (cur.Protected) return;

            // Disk numaraları yeniden takınca değişebilir: aynı disk aynı numarada mı, yeniden doğrulanır
            var fresh = Disks.Find(cur.Key);
            if (fresh == null || fresh.Number != cur.Number || fresh.Size != cur.Size || fresh.Protected)
            {
                ConfirmDialog.Info(this, L.T("Disk değişmiş", "The disk changed"),
                    L.T("Seçili disk artık aynı numarada değil ya da bulunamadı. Diskleri yenile'ye basıp tekrar dene.", "The selected disk is no longer at the same number or wasn't found. Press Refresh disks and try again."));
                RefreshDisks();
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(L.T("Disk ", "Disk ") + cur.Number + ":  " + cur.Model + "  ·  " + Fmt.Bytes(cur.Size));
            if (cur.Serial != "") sb.AppendLine(L.T("Seri: ", "Serial: ") + cur.Serial);
            if (cur.Letters.Count > 0) sb.AppendLine(L.T("Şu an üzerindeki sürücüler: ", "Drives on it now: ") + string.Join(" ", cur.Letters.ConvertAll(c => c + ":").ToArray()));
            sb.AppendLine();
            sb.AppendLine(L.T("Bu diskteki TÜM VERİ SİLİNECEK. Sonra şu bölümler açılacak:", "ALL DATA ON THIS DISK WILL BE ERASED. Then these partitions are created:"));
            int n = 0;
            foreach (var u in plan.Usable) sb.AppendLine("  " + (++n) + ".  " + Fmt.GB(u.Start) + " – " + Fmt.GB(u.End) + "  (" + Fmt.Bytes(u.Length) + ")");
            sb.AppendLine(L.T("Geri kalan ", "The remaining ") + Fmt.Bytes(cur.Size - plan.UsableBytes) + L.T(" ayrılmamış kalacak.", " stays unallocated."));
            string word = L.T("SİL", "ERASE");
            if (!ConfirmDialog.Ask(this, L.T("Disk silinip bölümlenecek", "The disk will be erased and partitioned"), sb.ToString(), word, L.T("Sil ve bölümle", "Erase and partition"), true)) return;

            string script = DiskPart.Script(cur, plan, LabelPrefix);
            busy = true;
            Log(L.T("diskpart çalıştırılıyor…", "Running diskpart…"));
            UpdateAll();
            var t = new Thread(delegate ()
            {
                string output;
                int code;
                try { code = DiskPart.Run(script, out output); }
                catch (Exception ex) { code = -1; output = ex.Message; }
                BeginInvoke((MethodInvoker)delegate
                {
                    busy = false;
                    foreach (var line in output.Split('\n'))
                        if (line.Trim().Length > 0) Log("  " + line.TrimEnd('\r'));
                    if (code == 0)
                        ConfirmDialog.Info(this, L.T("Tamamlandı", "Done"),
                            L.T("Bölümler oluşturuldu ve biçimlendirildi. Bozuk bölge ve güvenlik payı ayrılmamış alan olarak kaldı.\n\nDisk Yönetimi'nde görebilirsin. Yine de önemli dosyaların yedeğini başka yerde tut.",
                                "The partitions were created and formatted. The bad area and its margin stay unallocated.\n\nYou can see them in Disk Management. Still, keep backups of anything important elsewhere."));
                    else
                        ConfirmDialog.Info(this, L.T("diskpart hata verdi", "diskpart reported an error"),
                            L.T("Çıkış kodu ", "Exit code ") + code + L.T(". Ayrıntılar Kayıt bölümünde. Disk kısmen bölümlenmiş olabilir; Disk Yönetimi'nden kontrol et.",
                                ". Details are in the Log section. The disk may be partly partitioned; check Disk Management."));
                    RefreshDisks();
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // ------------------------------------------------------------------ Kapanış
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                // Bölümleme yarıda kesilirse disk yarım kalır
                e.Cancel = true;
                if (e.CloseReason == CloseReason.UserClosing)
                    ConfirmDialog.Info(this, L.T("Bölümleme sürüyor", "Partitioning in progress"),
                        L.T("diskpart işini bitirene kadar program kapatılamaz. Genelde bir dakikadan kısa sürer.", "The program can't close until diskpart finishes. It usually takes under a minute."));
                return;
            }
            if (scanner != null)
            {
                if (e.CloseReason == CloseReason.UserClosing && !ExitRequested &&
                    !ConfirmDialog.Ask(this, L.T("Tarama sürüyor", "A scan is running"),
                        L.T("Tarama durdurulup program kapatılsın mı? Buraya kadarki sonuçlar kaydedilir; tam taramaya sonra kaldığı yerden devam edebilirsin.",
                            "Stop the scan and close? Results so far are saved; a full scan can be resumed later."),
                        null, L.T("Durdur ve kapat", "Stop and close"), false))
                {
                    e.Cancel = true;
                    return;
                }
                scanner.Stop();
                for (int i = 0; i < 100 && scanner != null && scanner.Running; i++) { Thread.Sleep(100); }
                WinShell.KeepAwake(false);
            }
            if (map != null) map.Save();
            SavePlacement();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------ Rapor
        void SaveReport()
        {
            if (cur == null || map == null) return;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = L.T("Metin dosyası", "Text file") + " (*.txt)|*.txt";
                dlg.FileName = "BrokenDiskResizer_" + cur.Key + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".txt";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, Report(), new UTF8Encoding(true));
                    Log(L.T("Rapor kaydedildi: ", "Report saved: ") + dlg.FileName);
                }
                catch (Exception ex) { ConfirmDialog.Info(this, L.T("Kaydedilemedi", "Could not save"), ex.Message); }
            }
        }

        string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine(AppInfo.Name + " " + AppInfo.Version + " — " + L.T("disk raporu", "disk report"));
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine(L.T("DİSK", "DISK"));
            sb.AppendLine("  " + L.T("Numara: ", "Number: ") + cur.Number);
            sb.AppendLine("  " + L.T("Model: ", "Model: ") + cur.Model);
            sb.AppendLine("  " + L.T("Seri: ", "Serial: ") + cur.Serial);
            sb.AppendLine("  " + L.T("Boyut: ", "Size: ") + cur.Size.ToString("N0") + L.T(" bayt (", " bytes (") + Fmt.Bytes(cur.Size) + ")");
            sb.AppendLine("  " + L.T("Sürücüler: ", "Drives: ") + string.Join(" ", cur.Letters.ConvertAll(c => c + ":").ToArray()));
            sb.AppendLine();
            sb.AppendLine("SMART");
            if (smart != null && smart.Ok)
            {
                sb.AppendLine("  5   " + L.T("Yeniden atanmış: ", "Reallocated: ") + smart.Reallocated + Since(smart.Reallocated, smart.First == null ? -1 : smart.First.Reallocated));
                sb.AppendLine("  197 " + L.T("Bekleyen: ", "Pending: ") + smart.Pending + Since(smart.Pending, smart.First == null ? -1 : smart.First.Pending));
                sb.AppendLine("  198 " + L.T("Düzeltilemeyen: ", "Uncorrectable: ") + smart.Offline + Since(smart.Offline, smart.First == null ? -1 : smart.First.Offline));
                sb.AppendLine("  187 " + L.T("Bildirilen hata: ", "Reported errors: ") + smart.Reported);
                sb.AppendLine("  194 " + L.T("Sıcaklık: ", "Temperature: ") + smart.Temperature + " °C");
                sb.AppendLine("  9   " + L.T("Çalışma saati: ", "Power-on hours: ") + smart.Hours);
                sb.AppendLine("  " + HealthCard.Verdict(smart));
            }
            else sb.AppendLine("  " + L.T("okunamadı", "not available"));
            sb.AppendLine();
            sb.AppendLine(L.T("TARAMA", "SCAN"));
            sb.AppendLine("  " + L.T("Taranan: %", "Scanned: ") + Fmt.N(map.Coverage * 100, "0.0") + (L.Lang == "tr" ? "" : "%"));
            if (map.ResumeAt >= 0) sb.AppendLine("  " + L.T("Yarım kalan tam tarama: ", "Unfinished full scan at: ") + Fmt.GB(map.ResumeAt));
            sb.AppendLine();
            sb.AppendLine(L.T("BOZUK BÖLGELER", "BAD AREAS"));
            if (plan == null || plan.Bad.Count == 0) sb.AppendLine("  " + L.T("yok", "none"));
            else foreach (var z in plan.Bad)
                    sb.AppendLine("  " + z.Start.ToString("N0") + " – " + z.End.ToString("N0") + L.T(" bayt  (", " bytes  (") + Fmt.GB(z.Start) + " – " + Fmt.GB(z.End) + ", " + Fmt.Bytes(z.Length) + ", " +
                                  KindName(z.Kind) + ")");
            sb.AppendLine();
            sb.AppendLine(L.T("PLAN", "PLAN"));
            sb.AppendLine("  " + L.T("Güvenlik payı: %", "Safety margin: ") + cfg.BufferPct + (L.Lang == "tr" ? "" : "%") + " (" + Fmt.Bytes(plan == null ? 0 : plan.Buffer) + ")");
            if (plan != null)
            {
                int n = 0;
                foreach (var u in plan.Usable)
                    sb.AppendLine("  " + L.T("Bölüm ", "Partition ") + (++n) + ": " + Fmt.GB(u.Start) + " – " + Fmt.GB(u.End) + " (" + Fmt.Bytes(u.Length) + ")");
                sb.AppendLine("  " + L.T("Kullanılabilir: ", "Usable: ") + Fmt.Bytes(plan.UsableBytes) + " / " + Fmt.Bytes(cur.Size));
            }
            sb.AppendLine();
            sb.AppendLine(L.T("KAYIT", "LOG"));
            sb.Append(logText.ToString());
            return sb.ToString();
        }

        static string Since(long now, long first)
        {
            if (first < 0 || now < 0) return "";
            long d = now - first;
            return d == 0 ? L.T("  (ilk kontrolden beri değişmedi)", "  (unchanged since first check)") : "  (" + (d > 0 ? "+" : "") + d + L.T(" ilk kontrolden beri)", " since first check)");
        }
    }
}
