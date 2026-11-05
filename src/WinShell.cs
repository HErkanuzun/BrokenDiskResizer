using System;
using System.Runtime.InteropServices;

namespace BrokenDiskResizer
{
    // Windows kabuğu ile küçük işler: görev çubuğu simgesinde ilerleme, simgeyi yakıp söndürme, uykuyu engelleme
    static class WinShell
    {
        // ------------------------------------------------------------------ Görev çubuğu ilerlemesi
        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool full);
            void SetProgressValue(IntPtr hwnd, ulong done, ulong total);
            void SetProgressState(IntPtr hwnd, int state);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        class TaskbarInstance { }

        public const int NoProgress = 0, Indeterminate = 1, Normal = 2, Error = 4, Paused = 8;

        static ITaskbarList3 tb;
        static bool tried;

        static ITaskbarList3 Tb
        {
            get
            {
                if (tb == null && !tried)
                {
                    tried = true;
                    try { tb = (ITaskbarList3)new TaskbarInstance(); tb.HrInit(); }
                    catch { tb = null; }
                }
                return tb;
            }
        }

        public static void Progress(IntPtr hwnd, double fraction, int state)
        {
            try
            {
                var t = Tb;
                if (t == null || hwnd == IntPtr.Zero) return;
                t.SetProgressState(hwnd, state);
                if (state == Normal || state == Error || state == Paused)
                    t.SetProgressValue(hwnd, (ulong)(Math.Max(0, Math.Min(1, fraction)) * 1000), 1000);
            }
            catch { }
        }

        // ------------------------------------------------------------------ Dikkat çekme
        [StructLayout(LayoutKind.Sequential)]
        struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        // Pencere öne gelene kadar görev çubuğu simgesi yanıp söner
        public static void Flash(IntPtr hwnd)
        {
            try
            {
                var f = new FLASHWINFO();
                f.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
                f.hwnd = hwnd;
                f.dwFlags = 3 | 12;   // başlık + görev çubuğu, pencere öne gelene kadar
                FlashWindowEx(ref f);
            }
            catch { }
        }

        // ------------------------------------------------------------------ Uykuyu engelleme
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

        // Uzun tarama sürerken bilgisayar uykuya geçmesin (ekran kapanabilir)
        public static void KeepAwake(bool on)
        {
            try { SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS); }
            catch { }
        }
    }
}
