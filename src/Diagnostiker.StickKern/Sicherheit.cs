using System.Globalization;
using System.Text.RegularExpressions;

namespace Diagnostiker.StickKern;

public sealed record Pruefung(bool Erlaubt, IReadOnlyList<string> Gruende, IReadOnlyList<string> Hinweise);

/// <summary>
/// Entscheidet, ob ein Laufwerk als Ziel für einen Stick angeboten werden darf. Im Zweifel wird abgelehnt:
/// Es werden nur USB-Laufwerke in sinnvoller Größe angeboten, die das System nicht benutzt.
/// </summary>
public static partial class Sicherheit
{
    public const long MinGroesse = 4L << 30;     // 4 GiB: Boot-Partition (2,5 GiB) plus Platz für Daten
    public const long MaxGroesse = 512L << 30;   // größer ist fast sicher eine Datenplatte, kein Stick

    // Nur hier eingehängte Laufwerke gelten als Wechselmedium (Desktop hängt Sticks dort ein).
    static readonly string[] ErlaubteOrte = ["/media/", "/run/media/", "/mnt/"];

    [GeneratedRegex(@"^/dev/[a-z][a-z0-9]*$")]
    private static partial Regex GueltigerPfad();

    public static Pruefung Pruefe(Laufwerk l, long min = MinGroesse, long max = MaxGroesse)
    {
        var gruende = new List<string>();
        var hinweise = new List<string>();

        if (!GueltigerPfad().IsMatch(l.Pfad))
            gruende.Add($"Ungültiger Gerätename: {l.Pfad}");

        if (!string.Equals(l.Transport, "usb", StringComparison.OrdinalIgnoreCase))
            gruende.Add($"Kein USB-Laufwerk (Anschluss: {l.Transport ?? "unbekannt"}).");

        if (l.SchreibGeschuetzt)
            gruende.Add("Schreibgeschützt.");

        if (l.Groesse < min)
            gruende.Add($"Zu klein ({Groesse(l.Groesse)}, mindestens {Groesse(min)}).");
        if (l.Groesse > max)
            gruende.Add($"Zu groß ({Groesse(l.Groesse)}, mehr als {Groesse(max)}): vermutlich eine Datenplatte, kein Stick.");

        foreach (var ort in l.AlleEinhaengepunkte.Distinct())
        {
            if (!ErlaubterOrt(ort))
                gruende.Add($"Wird vom System benutzt (eingehängt unter {ort}).");
        }

        if (gruende.Count == 0)
        {
            if (l.Partitionen.Count > 0)
            {
                var liste = string.Join(", ", l.Partitionen.Select(p =>
                    $"{p.Bezeichnung ?? p.Pfad}{(p.Dateisystem is null ? "" : $" ({p.Dateisystem}, {Groesse(p.Groesse)})")}"));
                hinweise.Add($"Enthält {l.Partitionen.Count} Partition(en): {liste}. Alles darauf wird gelöscht.");
            }
            else hinweise.Add("Keine Partitionen erkannt (leer oder unformatiert).");

            var eingehaengt = l.AlleEinhaengepunkte.Distinct().ToList();
            if (eingehaengt.Count > 0)
                hinweise.Add($"Ist eingehängt unter {string.Join(", ", eingehaengt)} und wird vorher ausgehängt.");
        }

        return new Pruefung(gruende.Count == 0, gruende, hinweise);
    }

    static bool ErlaubterOrt(string ort) =>
        !ort.Contains("..") && ErlaubteOrte.Any(p => ort.StartsWith(p, StringComparison.Ordinal));

    static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    public static string Groesse(long bytes)
    {
        string[] einheiten = ["B", "KiB", "MiB", "GiB", "TiB"];
        double wert = bytes; var i = 0;
        while (wert >= 1024 && i < einheiten.Length - 1) { wert /= 1024; i++; }
        return string.Create(Deutsch, $"{wert:0.#} {einheiten[i]}");
    }
}

/// <summary>Bestätigung vor dem Löschen: Der Name des Laufwerks muss genau eingegeben werden.</summary>
public static class Bestaetigung
{
    public static string Erwartet(Laufwerk l) => l.Name;
    public static bool Stimmt(string? eingabe, Laufwerk l) => eingabe is not null && eingabe.Trim() == l.Name;
}
