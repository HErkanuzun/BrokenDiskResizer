using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// exe'nin "Özellikler > Ayrıntılar" bilgileri
[assembly: AssemblyTitle("Broken Disk Resizer")]
[assembly: AssemblyDescription("Finds the broken area of a hard disk and keeps it out of use")]
[assembly: AssemblyCompany("HErkanuzun")]
[assembly: AssemblyProduct("Broken Disk Resizer")]
[assembly: AssemblyCopyright("Copyright (c) 2026 HErkanuzun. MIT License.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0")]
[assembly: NeutralResourcesLanguage("en")]
[assembly: ComVisible(false)]

namespace BrokenDiskResizer
{
    static class AppInfo
    {
        public const string Name = "Broken Disk Resizer";
        public const string Version = "1.0";
        public const string Repo = "https://github.com/HErkanuzun/BrokenDiskResizer";
    }
}
