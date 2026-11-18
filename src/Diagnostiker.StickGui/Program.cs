using Avalonia;

namespace Diagnostiker.StickGui;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Der Stick-Manager läuft nur unter Linux.");
            return 1;
        }
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
