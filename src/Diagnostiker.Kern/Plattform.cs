using System.Runtime.InteropServices;

namespace Diagnostiker.Kern;

public static class Plattform
{
    public static bool IstWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IstLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static string OsName => IstWindows ? "windows" : IstLinux ? "linux" : "andere";

    /// <summary>Verzeichnisname unter /werkzeuge, passend zum laufenden System.</summary>
    public static string WerkzeugOrdner => IstWindows ? "win-x64" : "linux-x64";

    public static bool HatAdminRechte => Environment.IsPrivilegedProcess;
}
