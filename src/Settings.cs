using System;
using System.Globalization;
using System.IO;

namespace BrokenDiskResizer
{
    // Basit ayar dosyası: %AppData%\BrokenDiskResizer\settings.txt (anahtar=değer)
    class Settings
    {
        public string Lang = "";
        public double BufferPct = 10;
        public bool CountVerySlow = true;
        public bool ExcludeUnscanned = true;
        public string LastDisk = "";
        public bool Welcomed = false;
        public string Window = "";      // x,y,w,h,maximized

        static string FilePath
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrokenDiskResizer");
                Directory.CreateDirectory(d);
                return Path.Combine(d, "settings.txt");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    double dv;
                    switch (k)
                    {
                        case "lang": s.Lang = v; break;
                        case "buffer": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) s.BufferPct = Math.Max(0, Math.Min(30, dv)); break;
                        case "veryslow": s.CountVerySlow = v == "1"; break;
                        case "unscanned": s.ExcludeUnscanned = v == "1"; break;
                        case "disk": s.LastDisk = v; break;
                        case "welcomed": s.Welcomed = v == "1"; break;
                        case "window": s.Window = v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "lang=" + Lang,
                    "buffer=" + BufferPct.ToString(CultureInfo.InvariantCulture),
                    "veryslow=" + (CountVerySlow ? "1" : "0"),
                    "unscanned=" + (ExcludeUnscanned ? "1" : "0"),
                    "disk=" + LastDisk,
                    "welcomed=" + (Welcomed ? "1" : "0"),
                    "window=" + Window,
                });
            }
            catch { }
        }
    }
}
