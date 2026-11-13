using System.Text;
using System.Text.Json;

namespace Diagnostiker.Kern;

/// <summary>
/// Eine Prüfsitzung: ein Ordner pro Lauf mit session.json (maschinenlesbar),
/// bericht.md (lesbar) und roh/ (unveränderte Programmausgaben).
/// </summary>
public sealed class Session
{
    public string Verzeichnis { get; }
    public DateTime Beginn { get; } = DateTime.Now;
    public string Rechnername { get; } = Environment.MachineName;
    public List<ModulErgebnis> Ergebnisse { get; } = [];
    public string Notiz { get; set; } = "";

    /// <summary>Je Prüfung der jüngste Lauf, in der Reihenfolge des ersten Auftretens. Diese Sicht gilt für Zusammenfassung und
    /// Gesamtbewertung: Wird eine Prüfung wiederholt (z. B. nach dem Einrichten des WLANs), ersetzt der neue Lauf den alten.</summary>
    public IReadOnlyList<ModulErgebnis> Aktuelle => Ergebnisse.GroupBy(e => e.Id).Select(g => g.Last()).ToList();

    /// <summary>Der Lauf derselben Prüfung unmittelbar vor <paramref name="e"/> (oder null).</summary>
    public ModulErgebnis? Vorheriger(ModulErgebnis e) =>
        Ergebnisse.TakeWhile(x => !ReferenceEquals(x, e)).LastOrDefault(x => x.Id == e.Id);

    public Session(string protokollWurzel)
    {
        var name = $"{Beginn:yyyyMMdd-HHmmss}_{Bereinigen(Rechnername)}";
        Verzeichnis = Path.Combine(protokollWurzel, name);
        Directory.CreateDirectory(Path.Combine(Verzeichnis, "roh"));
    }

    private static string Bereinigen(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

    public void Hinzufuegen(ModulErgebnis e)
    {
        Ergebnisse.Add(e);
        // Beim ersten Lauf "<id>-<schritt>.txt", bei Wiederholungen "<id>-lauf<n>-<schritt>.txt": nichts wird überschrieben.
        var lauf = Ergebnisse.Count(x => x.Id == e.Id);
        var praefix = lauf == 1 ? Bereinigen(e.Id) : $"{Bereinigen(e.Id)}-lauf{lauf}";
        var nr = 0;
        foreach (var s in e.Schritte)
        {
            nr++;
            var datei = Path.Combine(Verzeichnis, "roh", $"{praefix}-{nr}.txt");
            File.WriteAllText(datei,
                $"# {e.Titel} / {s.Titel}\n# Befehl: {s.Befehlszeile}\n# Exitcode: {s.ExitCode?.ToString() ?? "-"}\n\n{s.Ausgabe}",
                new UTF8Encoding(false));
        }
        Speichern(); // nach jedem Modul, damit bei Absturz oder Stromausfall nichts verloren geht
    }

    public void Speichern()
    {
        var json = JsonSerializer.Serialize(new
        {
            programmversion = "0.1",
            beginn = Beginn,
            rechner = Rechnername,
            betriebssystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            administrator = Plattform.HatAdminRechte,
            notiz = Notiz,
            gesamtstatus = ModulAusfuehrer.ZusammenfassenStatus(Aktuelle.Select(m => m.Status)).ToString(),
            module = Aktuelle,
            verlauf = Ergebnisse.Select(m => new { m.Id, m.Titel, m.Beginn, status = m.Status.ToString() }),
        }, new JsonSerializerOptions(ModulLader.JsonOptionen) { WriteIndented = true });
        File.WriteAllText(Path.Combine(Verzeichnis, "session.json"), json, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Verzeichnis, "bericht.md"), BerichtErzeugen(), new UTF8Encoding(false));
    }

    public string BerichtErzeugen()
    {
        var sb = new StringBuilder();
        var aktuelle = Aktuelle;
        var gesamt = ModulAusfuehrer.ZusammenfassenStatus(aktuelle.Select(m => m.Status));
        sb.AppendLine("# Diagnosebericht");
        sb.AppendLine();
        sb.AppendLine($"- **Rechner:** {Rechnername}");
        sb.AppendLine($"- **System:** {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"- **Datum:** {Beginn:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"- **Administratorrechte:** {(Plattform.HatAdminRechte ? "ja" : "nein")}");
        sb.AppendLine($"- **Gesamtbewertung:** {Symbol(gesamt)}");
        if (Notiz.Length > 0) sb.AppendLine($"- **Notiz:** {Notiz}");
        sb.AppendLine();

        sb.AppendLine("## Übersicht");
        sb.AppendLine();
        sb.AppendLine("| Bewertung | Prüfung |");
        sb.AppendLine("|---|---|");
        foreach (var m in aktuelle) sb.AppendLine($"| {Symbol(m.Status)} | {m.Titel} |");
        sb.AppendLine();

        // Wurde eine Prüfung wiederholt, bleibt der Verlauf sichtbar (z. B. Warnung vor dem Einrichten des WLANs, danach Ok).
        var wiederholt = Ergebnisse.GroupBy(e => e.Id).Where(g => g.Count() > 1).ToList();
        if (wiederholt.Count > 0)
        {
            sb.AppendLine("## Wiederholte Prüfungen (Verlauf)");
            sb.AppendLine();
            sb.AppendLine("Die Übersicht und die Gesamtbewertung zeigen jeweils den neuesten Lauf. Die Rohausgaben früherer Läufe liegen in `roh/` (Dateien mit `-lauf2-` usw.).");
            sb.AppendLine();
            sb.AppendLine("| Prüfung | Verlauf (älteste zuerst) |");
            sb.AppendLine("|---|---|");
            foreach (var g in wiederholt)
                sb.AppendLine($"| {g.First().Titel} | {string.Join(" → ", g.Select(m => $"{m.Beginn:HH:mm} {Text(m.Status)}"))} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Details");
        foreach (var m in aktuelle)
        {
            sb.AppendLine();
            sb.AppendLine($"### {Symbol(m.Status)} {m.Titel}");
            foreach (var s in m.Schritte)
            {
                sb.AppendLine();
                sb.AppendLine($"**{s.Titel}** – {Text(s.Status)}");
                foreach (var meldung in s.Meldungen) sb.AppendLine($"- {meldung}");
            }
        }
        return sb.ToString();
    }

    /// <summary>Lesbare Bezeichnung für Anzeige und Bericht (die Namen der Aufzählung sind ohne Umlaute).</summary>
    public static string Text(Status s) => s switch
    {
        Status.Ok => "OK",
        Status.Warnung => "Warnung",
        Status.Fehler => "Fehler",
        Status.Uebersprungen => "übersprungen",
        _ => "Info",
    };

    public static string Symbol(Status s) => s switch
    {
        Status.Ok => "[OK]",
        Status.Warnung => "[WARNUNG]",
        Status.Fehler => "[FEHLER]",
        Status.Uebersprungen => "[übersprungen]",
        _ => "[Info]",
    };
}
