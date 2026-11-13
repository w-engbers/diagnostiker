using Diagnostiker.Kern;

namespace Diagnostiker.Tui;

/// <summary>Alles, was beide Oberflächen (Bildschirmoberfläche und einfache Textansicht) gemeinsam brauchen.</summary>
sealed class Kontext
{
    public required string Wurzel { get; init; }
    public required IReadOnlyList<ModulDefinition> Module { get; init; }
    public required ModulAusfuehrer Ausfuehrer { get; init; }
    public required Session Session { get; init; }
    public required IReadOnlyList<string> Ladefehler { get; init; }

    /// <summary>Im Live-System (Root, NetworkManager) lässt sich das Netzwerk aus dem Programm heraus einrichten.</summary>
    public bool NetzwerkEinrichten { get; } = Plattform.IstLinux && Plattform.HatAdminRechte && ProgrammVorhanden("nmtui");

    public IEnumerable<ModulDefinition> Schnellpruefungen => Module.Where(m => m.Schnellpruefung && !m.Schreibend);

    public static Kontext? Erstellen(string? modulOption, string? protokollOption)
    {
        var modulDir = ModulLader.ModulVerzeichnisFinden(modulOption);
        if (modulDir is null)
        {
            Console.Error.WriteLine("Verzeichnis 'module' nicht gefunden. Mit --module <pfad> angeben.");
            return null;
        }
        var wurzel = Path.GetDirectoryName(modulDir)!;
        var (alle, ladefehler) = ModulLader.Laden(modulDir);
        var ausfuehrer = new ModulAusfuehrer(wurzel);
        var module = alle.Where(ausfuehrer.IstAnwendbar).OrderBy(m => m.Gruppe).ThenBy(m => m.Titel).ToList();
        var session = new Session(ProtokollVerzeichnis(protokollOption ?? Path.Combine(wurzel, "protokolle")));
        return new Kontext { Wurzel = wurzel, Module = module, Ausfuehrer = ausfuehrer, Session = session, Ladefehler = ladefehler };
    }

    public static bool ProgrammVorhanden(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(d => File.Exists(Path.Combine(d, name)));

    // Ist der Stick schreibgeschützt (oder das Verzeichnis nicht beschreibbar), wird ins temporäre Verzeichnis ausgewichen.
    static string ProtokollVerzeichnis(string gewuenscht)
    {
        try
        {
            Directory.CreateDirectory(gewuenscht);
            var probe = Path.Combine(gewuenscht, ".schreibtest");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return gewuenscht;
        }
        catch
        {
            var ersatz = Path.Combine(Path.GetTempPath(), "diagnostiker-protokolle");
            Console.Error.WriteLine($"Stick nicht beschreibbar – Protokolle gehen nach {ersatz}");
            return ersatz;
        }
    }

    /// <summary>WLAN neu suchen lassen (frisch gestartet kennt der NetworkManager oft noch keine Netze) und dann nmtui starten.
    /// Das Terminal gehört dabei ganz nmtui. Liefert eine Fehlermeldung oder null.</summary>
    public static async Task<string?> NetzwerkEinrichtenAsync()
    {
        try
        {
            using var rescan = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("nmcli", ["device", "wifi", "rescan"])
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
            await rescan.WaitForExitAsync();
        }
        catch { /* ohne nmcli oder ohne WLAN geht es trotzdem weiter */ }
        await Task.Delay(TimeSpan.FromSeconds(4));
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("nmtui") { UseShellExecute = false })!;
            await p.WaitForExitAsync();
            return null;
        }
        catch (Exception e) { return $"nmtui lässt sich nicht starten: {e.Message}"; }
    }

    /// <summary>Gesamtbewertung über die jeweils neuesten Läufe.</summary>
    public Status Gesamt => ModulAusfuehrer.ZusammenfassenStatus(Session.Aktuelle.Select(e => e.Status));
}
