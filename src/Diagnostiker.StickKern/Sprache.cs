using System.Globalization;

namespace Diagnostiker.StickKern;

/// <summary>
/// Sprache der Meldungen des Kerns und des Fensters. Die deutschen Texte stehen im Code und sind der Schlüssel; für Englisch und Niederländisch
/// liefert der <see cref="Katalog"/> die Übersetzung (fehlt sie, bleibt der deutsche Text stehen). Der Kern beginnt auf Deutsch (Eingabezeile, Tests),
/// das Fenster stellt die gespeicherte oder die Standardsprache (Englisch) ein.
/// </summary>
public static class Sprache
{
    public static readonly IReadOnlyList<string> Alle = ["en", "de", "nl"];
    public const string Standard = "en";

    static volatile string _global = "de";
    static readonly AsyncLocal<string?> _lokal = new();

    static string _aktuell => _lokal.Value ?? _global;

    /// <summary>Aktuelle Sprache: „de“, „en“ oder „nl“.</summary>
    public static string Aktuell
    {
        get => _aktuell;
        set { _lokal.Value = null; _global = Alle.Contains(value) ? value : Standard; }
    }

    /// <summary>Schaltet die Sprache nur für den laufenden (asynchronen) Ablauf um, bis das Ergebnis freigegeben wird. Für Tests.</summary>
    public static IDisposable Fuer(string sprache)
    {
        var vorher = _lokal.Value;
        _lokal.Value = Alle.Contains(sprache) ? sprache : Standard;
        return new Zuruecksetzen(vorher);
    }

    sealed class Zuruecksetzen(string? vorher) : IDisposable { public void Dispose() => _lokal.Value = vorher; }

    /// <summary>Übersetzt einen deutschen Text und setzt die Platzhalter {0}, {1} … ein.</summary>
    public static string T(string deutsch, params object[] args)
    {
        var text = _aktuell == "de" ? deutsch : Katalog.Suche(_aktuell, deutsch) ?? deutsch;
        return args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
    }

    /// <summary>Dezimalzeichen der aktuellen Sprache (Komma im Deutschen und Niederländischen, Punkt im Englischen).</summary>
    public static string Zahl(double wert, string format)
    {
        var s = wert.ToString(format, CultureInfo.InvariantCulture);
        return _aktuell == "en" ? s : s.Replace('.', ',');
    }

    /// <summary>Speicherort der gewählten Sprache: <c>$XDG_CONFIG_HOME|~/.config/diagnostiker/sprache</c>.</summary>
    public static string Speicherort()
    {
        var basis = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(basis)) basis = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(basis, "diagnostiker", "sprache");
    }

    /// <summary>Liest die gespeicherte Sprache (oder die Umgebungsvariable DIAGNOSTIKER_SPRACHE), sonst die Standardsprache.</summary>
    public static string Gespeichert(string? pfad = null)
    {
        var umgebung = Environment.GetEnvironmentVariable("DIAGNOSTIKER_SPRACHE")?.Trim().ToLowerInvariant();
        if (umgebung is not null && Alle.Contains(umgebung)) return umgebung;
        try
        {
            var s = File.ReadAllText(pfad ?? Speicherort()).Trim().ToLowerInvariant();
            if (Alle.Contains(s)) return s;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return Standard;
    }

    public static void Speichern(string sprache, string? pfad = null)
    {
        try
        {
            pfad ??= Speicherort();
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.WriteAllText(pfad, sprache + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }       // die Wahl gilt dann nur für diesen Start
    }
}
