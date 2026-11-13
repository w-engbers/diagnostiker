// Stick-Manager: erstellt den Diagnostiker-Stick (nur unter Linux).
// Aufruf: diagnostiker-stick [laufwerke]
//   ohne Argument   Menü
//   laufwerke       Laufwerke auflisten und bewerten (nur lesend), ohne Menü
using Diagnostiker.StickKern;
using Spectre.Console;

if (!OperatingSystem.IsLinux())
{
    AnsiConsole.MarkupLine("[red]Der Stick-Manager läuft nur unter Linux.[/]");
    return 1;
}

var quelle = new LsblkQuelle();
var befehle = new ProzessBefehle();

if (args.Contains("laufwerke"))
{
    await LaufwerkeZeigenAsync();
    return 0;
}

var projekt = ProjektOrdner.Finden(AppContext.BaseDirectory, Directory.GetCurrentDirectory());

while (true)
{
    AnsiConsole.Write(new Rule("Diagnostiker – Stick-Manager") { Justification = Justify.Left });
    var wahl = AnsiConsole.Prompt(new SelectionPrompt<string>()
        .Title("Was soll getan werden?")
        .AddChoices("Laufwerke ansehen", "Stick erstellen", "Abbild (ISO) bauen", "Beenden"));
    switch (wahl)
    {
        case "Beenden": return 0;
        case "Laufwerke ansehen": await LaufwerkeZeigenAsync(); break;
        case "Abbild (ISO) bauen": await IsoBauenAsync(); break;
        case "Stick erstellen": await StickErstellenAsync(); break;
    }
}

async Task IsoBauenAsync()
{
    if (projekt is null) { Fehler("Projektordner nicht gefunden. Bitte im Projektordner starten."); return; }
    AnsiConsole.MarkupLine("[yellow]Das dauert je nach Verbindung 5 bis 20 Minuten.[/]");
    var code = await befehle.AusfuehrenAsync(Path.Combine(projekt, "basis", "debian", "bauen.sh"), [], null,
        z => AnsiConsole.MarkupLineInterpolated($"[grey]{z}[/]"), CancellationToken.None);
    if (code == 0) AnsiConsole.MarkupLine("[green]Das Abbild ist fertig.[/]");
    else Fehler($"Der Bau ist fehlgeschlagen (Exitcode {code}).");
}

async Task StickErstellenAsync()
{
    if (projekt is null) { Fehler("Projektordner nicht gefunden. Bitte im Projektordner starten."); return; }
    if (!File.Exists(Path.Combine(projekt, "build", "out", "diagnostiker.iso")))
    {
        Fehler("Es gibt noch kein Live-Abbild. Zuerst „Abbild (ISO) bauen“ wählen.");
        return;
    }

    var alle = await quelle.LesenAsync(CancellationToken.None);
    var geeignet = alle.Where(l => Sicherheit.Pruefe(l).Erlaubt).ToList();
    if (geeignet.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]Kein geeigneter USB-Stick gefunden.[/] Stick einstecken, dann „Laufwerke ansehen“ prüfen.");
        return;
    }

    var wahlen = geeignet.Select(l => $"{l.Pfad}  {l.Beschreibung}  {Sicherheit.Groesse(l.Groesse)}").Append("Abbrechen").ToList();
    var gewaehlt = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Auf welchen Stick soll geschrieben werden?").AddChoices(wahlen));
    if (gewaehlt == "Abbrechen") return;
    var laufwerk = geeignet[wahlen.IndexOf(gewaehlt)];

    var pruefung = Sicherheit.Pruefe(laufwerk);
    AnsiConsole.Write(new Panel(new Markup(
        $"[bold]{Markup.Escape(laufwerk.Pfad)}[/]  {Markup.Escape(laufwerk.Beschreibung)}  {Sicherheit.Groesse(laufwerk.Groesse)}\n" +
        string.Join("\n", pruefung.Hinweise.Select(h => "• " + Markup.Escape(h)))))
        .Header("Ausgewählter Stick").RoundedBorder());
    AnsiConsole.MarkupLine($"[bold red]ALLES auf {Markup.Escape(laufwerk.Pfad)} wird gelöscht und lässt sich nicht wiederherstellen.[/]");

    var eingabe = AnsiConsole.Prompt(new TextPrompt<string>(
            $"Zur Bestätigung den Gerätenamen eingeben ([bold]{Markup.Escape(Bestaetigung.Erwartet(laufwerk))}[/]), leer lassen zum Abbrechen:")
        .AllowEmpty());
    if (string.IsNullOrWhiteSpace(eingabe)) { AnsiConsole.MarkupLine("Abgebrochen, es wurde nichts verändert."); return; }

    var ersteller = new StickErsteller(quelle, befehle, projekt);
    var ergebnis = await ersteller.ErstellenAsync(laufwerk, eingabe,
        z => AnsiConsole.MarkupLineInterpolated($"[grey]{z}[/]"));
    if (ergebnis.Erfolg) AnsiConsole.MarkupLine($"[green]{Markup.Escape(ergebnis.Meldung)}[/]");
    else Fehler(ergebnis.Meldung);
}

static void Fehler(string text) => AnsiConsole.MarkupLine($"[red]{Markup.Escape(text)}[/]");

async Task LaufwerkeZeigenAsync()
{
    IReadOnlyList<Laufwerk> laufwerke;
    try { laufwerke = await quelle.LesenAsync(CancellationToken.None); }
    catch (Exception e) { Fehler($"Laufwerke lassen sich nicht lesen: {e.Message}"); return; }

    var tabelle = new Table().RoundedBorder()
        .AddColumn("Gerät").AddColumn("Modell").AddColumn("Größe").AddColumn("Anschluss").AddColumn("Als Ziel");
    foreach (var l in laufwerke)
    {
        var p = Sicherheit.Pruefe(l);
        tabelle.AddRow(Markup.Escape(l.Pfad), Markup.Escape(l.Beschreibung), Sicherheit.Groesse(l.Groesse),
            Markup.Escape(l.Transport ?? "?"), p.Erlaubt ? "[green]geeignet[/]" : "[red]nicht geeignet[/]");
    }
    AnsiConsole.Write(tabelle);

    // Die Gründe stehen unter der Tabelle, damit sie lesbar bleiben.
    foreach (var l in laufwerke)
    {
        var p = Sicherheit.Pruefe(l);
        foreach (var g in p.Gruende) AnsiConsole.MarkupLine($"  [red]{Markup.Escape(l.Pfad)}[/]: {Markup.Escape(g)}");
        foreach (var h in p.Hinweise) AnsiConsole.MarkupLine($"  [yellow]{Markup.Escape(l.Pfad)}[/]: {Markup.Escape(h)}");
    }

    var anzahl = laufwerke.Count(l => Sicherheit.Pruefe(l).Erlaubt);
    AnsiConsole.MarkupLine(anzahl == 0
        ? "[yellow]Kein geeigneter USB-Stick gefunden.[/] Stick einstecken und erneut ansehen."
        : $"[green]{anzahl} geeignete(r) Stick(s).[/]");
}
