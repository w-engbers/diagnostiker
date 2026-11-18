using System.Globalization;
using System.Text.RegularExpressions;

namespace Diagnostiker.StickKern;

/// <summary>
/// Liest die Ausgabezeilen des Schreibhelfers und der Schritte danach und leitet daraus für eine Oberfläche ab:
/// den aktuellen Schritt (Klartext), den Fortschritt des Schritts in Prozent (nur beim Schreiben und Zurücklesen bekannt)
/// und die Geschwindigkeit. Der Auswerter ändert nichts und hat keine Abhängigkeit von einer Oberfläche.
/// </summary>
public sealed partial class FortschrittAuswertung(long abbildBytes)
{
    /// <summary>Klartext des aktuellen Schritts (leer, solange noch nichts gemeldet wurde).</summary>
    public string Schritt { get; private set; } = "";
    /// <summary>Fortschritt des aktuellen Schritts, 0 bis 100, oder null, wenn er sich nicht bemessen lässt.</summary>
    public double? Prozent { get; private set; }
    /// <summary>Zuletzt gemeldete Geschwindigkeit, zum Beispiel „18,4 MB/s“ (leer, wenn unbekannt).</summary>
    public string Geschwindigkeit { get; private set; } = "";
    /// <summary>Gesamtfortschritt über alle Schritte, 0 bis 100.</summary>
    public double Gesamt { get; private set; }

    // Gewichtung der Schritte am Gesamtbalken (Schreiben dauert bei langsamen Sticks am längsten).
    static readonly (string Kennung, string Text, double Anfang, double Breite, bool Messbar)[] Schritte =
    [
        ("Abbilddatei prüfen", "Paketdatei prüfen", 0, 3, false),
        ("wird vollständig neu angelegt", "Stick vorbereiten", 3, 1, false),
        ("Abbild schreiben", "Abbild auf den Stick schreiben", 4, 55, true),
        ("Schreiben beendet", "Zwischenspeicher leeren (bei langsamen Sticks kann das einige Minuten dauern)", 59, 10, false),
        ("Zurücklesen", "Zurücklesen und vergleichen", 69, 15, true),
        ("Vergleich in Ordnung", "Vergleich in Ordnung", 84, 1, false),
        ("Partitionstabelle", "Partitionen anlegen", 85, 3, false),
        ("Datenpartition formatieren", "Datenpartition formatieren", 88, 2, false),
        ("Datenpartition einhängen", "Datenpartition öffnen", 90, 1, false),
        ("Programme und Module", "Programme und Prüfmodule kopieren", 91, 6, false),
        ("Daten sichern", "Stick sichern und aushängen", 97, 3, false),
    ];

    int _index = -1;

    /// <summary>Wertet eine Zeile aus. Gibt true zurück, wenn sich Schritt, Prozent oder Geschwindigkeit geändert haben.</summary>
    public bool Zeile(string zeile)
    {
        var z = zeile.Trim();
        if (z.Length == 0) return false;

        for (var i = 0; i < Schritte.Length; i++)
        {
            if (!z.Contains(Schritte[i].Kennung, StringComparison.Ordinal)) continue;
            if (i < _index) break;               // nie rückwärts (z. B. Meldungen, die Wörter eines früheren Schritts enthalten)
            _index = i;
            Schritt = Schritte[i].Text;
            Prozent = Schritte[i].Messbar ? 0 : null;
            Gesamt = Schritte[i].Anfang;
            if (!Schritte[i].Messbar) Geschwindigkeit = "";
            return true;
        }

        var m = DdZeile().Match(z);
        if (m.Success && _index >= 0 && Schritte[_index].Messbar && abbildBytes > 0)
        {
            var bytes = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            Prozent = Math.Clamp(bytes * 100 / abbildBytes, 0, 100);
            Gesamt = Schritte[_index].Anfang + Schritte[_index].Breite * Prozent.Value / 100;
            Geschwindigkeit = m.Groups[2].Success ? m.Groups[2].Value.Replace('.', ',') : Geschwindigkeit;
            return true;
        }
        return false;
    }

    /// <summary>Alles abgeschlossen: Balken auf 100.</summary>
    public void Fertig() { Gesamt = 100; Prozent = 100; Schritt = "Fertig"; Geschwindigkeit = ""; }

    [GeneratedRegex(@"^(\d+) bytes .*copied, [\d.,]+ s, ([\d.,]+ [kKMG]?B/s)")]
    private static partial Regex DdZeile();
}
