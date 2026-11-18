using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Diagnostiker.StickGui;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Default;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
            // --quelle <Ordner|https-Adresse> nimmt ein Paket von dort statt von GitHub (zum Beispiel ohne Netz).
            var args = desktop.Args ?? [];
            var i = Array.IndexOf(args, "--quelle");
            desktop.MainWindow = new HauptFenster(null, null, i >= 0 && i + 1 < args.Length ? args[i + 1] : null, null, null);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
