using System.Globalization;

namespace BrokenDiskResizer
{
    // Dil desteği: kodda metinler L.T("Türkçe", "English") olarak yazılır
    static class L
    {
        public static readonly string[] Codes = { "tr", "en" };
        public static string Lang = Detect();

        public static string Detect()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
        }

        public static void Set(string code) { Lang = code == "tr" || code == "en" ? code : Detect(); }

        public static string T(string tr, string en) { return Lang == "tr" ? tr : en; }

        public static string Upper(string s)
        {
            return s.ToUpper(Lang == "tr" ? new CultureInfo("tr-TR") : CultureInfo.InvariantCulture);
        }
    }
}
