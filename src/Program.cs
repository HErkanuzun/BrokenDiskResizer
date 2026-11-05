using System;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace BrokenDiskResizer
{
    static class Program
    {
        const string MutexName = "BrokenDiskResizer_SingleInstance";
        const string ShowEvent = "BrokenDiskResizer_Show", ExitEvent = "BrokenDiskResizer_Exit";

        static bool Signal(string name)
        {
            try { using (var e = EventWaitHandle.OpenExisting(name)) return e.Set(); }
            catch { return false; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // Kurulum paketi dosyaları değiştirmeden önce çalışan kopyayı düzgünce kapatır
            if (Array.IndexOf(args, "--exit") >= 0)
            {
                if (Signal(ExitEvent))
                    using (var m = new Mutex(false, MutexName))
                    {
                        try { if (m.WaitOne(30000)) m.ReleaseMutex(); }
                        catch (AbandonedMutexException) { }
                    }
                Environment.Exit(0);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var cfg = Settings.Load();
            if (cfg.Lang != "") L.Set(cfg.Lang);

            bool created;
            using (var mtx = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Zaten açık: o pencere öne getirilir
                    if (!Signal(ShowEvent))
                        MessageBox.Show(L.T("Broken Disk Resizer zaten açık.", "Broken Disk Resizer is already running."), AppInfo.Name);
                    return;
                }

                // Ham diske erişmek için yönetici hakkı gerekir (manifest bunu ister; yine de kontrol edilir)
                bool admin = false;
                try { admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { }
                if (!admin)
                    MessageBox.Show(L.T("Broken Disk Resizer diskleri okuyabilmek için yönetici olarak çalışmalı. Simgeye sağ tıkla → Yönetici olarak çalıştır.",
                                        "Broken Disk Resizer must run as administrator to read disks. Right-click the icon → Run as administrator."), AppInfo.Name);

                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent))
                using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEvent))
                {
                    var form = new MainForm(cfg);
                    var listener = new Thread(delegate ()
                    {
                        var handles = new WaitHandle[] { show, exit };
                        while (true)
                        {
                            int i;
                            try { i = WaitHandle.WaitAny(handles); } catch { return; }
                            try
                            {
                                if (i == 0) form.BeginInvoke((MethodInvoker)form.BringToFront2);
                                else form.BeginInvoke((MethodInvoker)form.RequestExit);
                            }
                            catch { return; }
                        }
                    });
                    listener.IsBackground = true;
                    listener.Start();
                    Application.Run(form);
                }
            }
        }
    }
}
