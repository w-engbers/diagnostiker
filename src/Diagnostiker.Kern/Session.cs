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
        var nr = 0;
        foreach (var s in e.Schritte)
        {
            nr++;
            var datei = Path.Combine(Verzeichnis, "roh", $"{Bereinigen(e.Id)}-{nr}.txt");
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
            gesamtstatus = ModulAusfuehrer.ZusammenfassenStatus(Ergebnisse.Select(m => m.Status)).ToString(),
            module = Ergebnisse,
        }, new JsonSerializerOptions(ModulLader.JsonOptionen) { WriteIndented = true });
        File.WriteAllText(Path.Combine(Verzeichnis, "session.json"), json, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Verzeichnis, "bericht.md"), BerichtErzeugen(), new UTF8Encoding(false));
    }

    public string BerichtErzeugen()
    {
        var sb = new StringBuilder();
        var gesamt = ModulAusfuehrer.ZusammenfassenStatus(Ergebnisse.Select(m => m.Status));
        sb.AppendLine("# Diagnosebericht");
        sb.AppendLine();
        sb.AppendLine($"- **Rechner:** {Rechnername}");
        sb.AppendLine($"- **System:** {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"- **Datum:** {Beginn:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"- **Administratorrechte:** {(Plattform.HatAdminRechte ? "ja" : "nein")}");
        sb.AppendLine($"- **Gesamtbewertung:** {Symbol(gesamt)} {gesamt}");
        if (Notiz.Length > 0) sb.AppendLine($"- **Notiz:** {Notiz}");
        sb.AppendLine();

        sb.AppendLine("## Übersicht");
        sb.AppendLine();
        sb.AppendLine("| Bewertung | Prüfung |");
        sb.AppendLine("|---|---|");
        foreach (var m in Ergebnisse) sb.AppendLine($"| {Symbol(m.Status)} {m.Status} | {m.Titel} |");
        sb.AppendLine();

        sb.AppendLine("## Details");
        foreach (var m in Ergebnisse)
        {
            sb.AppendLine();
            sb.AppendLine($"### {Symbol(m.Status)} {m.Titel}");
            foreach (var s in m.Schritte)
            {
                sb.AppendLine();
                sb.AppendLine($"**{s.Titel}** – {s.Status}");
                foreach (var meldung in s.Meldungen) sb.AppendLine($"- {meldung}");
            }
        }
        return sb.ToString();
    }

    public static string Symbol(Status s) => s switch
    {
        Status.Ok => "[OK]",
        Status.Warnung => "[WARNUNG]",
        Status.Fehler => "[FEHLER]",
        Status.Uebersprungen => "[übersprungen]",
        _ => "[Info]",
    };
}
