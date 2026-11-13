using System.Diagnostics;
using Diagnostiker.Kern;
using Spectre.Console;
using Status = Diagnostiker.Kern.Status;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Aufrufe: Diagnostiker [--module <pfad>] [--protokolle <pfad>] [--schnell]   (--schnell = ohne Menü, alle Schnellprüfungen)
string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
var automatisch = args.Contains("--schnell");

var modulDir = ModulLader.ModulVerzeichnisFinden(Option("--module"));
if (modulDir is null)
{
    AnsiConsole.MarkupLine("[red]Verzeichnis 'module' nicht gefunden.[/] Mit --module <pfad> angeben.");
    return 2;
}

var wurzel = Path.GetDirectoryName(modulDir)!;
var (alleModule, ladefehler) = ModulLader.Laden(modulDir);
var ausfuehrer = new ModulAusfuehrer(wurzel);
var module = alleModule.Where(ausfuehrer.IstAnwendbar).OrderBy(m => m.Gruppe).ThenBy(m => m.Titel).ToList();

var protokollWurzel = ProtokollVerzeichnis(Option("--protokolle") ?? Path.Combine(wurzel, "protokolle"));
var session = new Session(protokollWurzel);

AnsiConsole.Write(new Rule("[bold]Diagnostiker[/] – Prüfstick").LeftJustified());
AnsiConsole.MarkupLine($"Rechner: [bold]{Markup.Escape(session.Rechnername)}[/]   System: {Plattform.OsName}   " +
    (Plattform.HatAdminRechte ? "[green]Administratorrechte: ja[/]" : "[yellow]Administratorrechte: nein[/] (einige Prüfungen werden übersprungen)"));
AnsiConsole.MarkupLine($"Protokoll: [grey]{Markup.Escape(session.Verzeichnis)}[/]");
foreach (var f in ladefehler) AnsiConsole.MarkupLine($"[red]Modul fehlerhaft:[/] {Markup.Escape(f)}");
AnsiConsole.WriteLine();

if (automatisch)
{
    foreach (var m in module.Where(m => m.Schnellpruefung && !m.Schreibend)) await ModulLaufenLassen(m, fragen: false);
    Zusammenfassung();
    return 0;
}

// Im Live-System (Root, NetworkManager) lässt sich das Netzwerk aus dem Programm heraus einrichten.
const string NetzwerkEintrag = "Netzwerk verbinden (WLAN/LAN einrichten)";
var netzwerkEinrichten = Plattform.IstLinux && Plattform.HatAdminRechte && ProgrammVorhanden("nmtui");

while (true)
{
    var auswahl = new List<string> { "Schnellprüfung (alle harmlosen Standardtests)", "Einzelne Prüfung wählen" };
    if (netzwerkEinrichten) auswahl.Add(NetzwerkEintrag);
    auswahl.AddRange(["Notiz zum Protokoll hinzufügen", "Zusammenfassung anzeigen", "Beenden"]);
    var wahl = AnsiConsole.Prompt(new SelectionPrompt<string>()
        .Title("Was soll geprüft werden?")
        .PageSize(15)
        .AddChoices(auswahl));

    switch (wahl)
    {
        case var w when w.StartsWith("Schnell"):
            foreach (var m in module.Where(m => m.Schnellpruefung && !m.Schreibend)) await ModulLaufenLassen(m, fragen: true);
            Zusammenfassung();
            break;
        case "Einzelne Prüfung wählen":
            var gewaehlt = AnsiConsole.Prompt(new SelectionPrompt<ModulDefinition>()
                .Title("Welche Prüfung?")
                .PageSize(15)
                .UseConverter(m => $"{m.Gruppe}: {m.Titel}")
                .AddChoices(module));
            await ModulLaufenLassen(gewaehlt, fragen: true);
            break;
        case NetzwerkEintrag:
            await NetzwerkVerbindenAsync();
            break;
        case "Notiz zum Protokoll hinzufügen":
            session.Notiz = AnsiConsole.Prompt(new TextPrompt<string>("Notiz (z. B. Name/Beschwerde des Besitzers):").AllowEmpty());
            session.Speichern();
            break;
        case "Zusammenfassung anzeigen":
            Zusammenfassung();
            break;
        default:
            session.Speichern();
            AnsiConsole.MarkupLine($"Bericht: [bold]{Markup.Escape(Path.Combine(session.Verzeichnis, "bericht.md"))}[/]");
            return 0;
    }
    AnsiConsole.WriteLine();
}

async Task NetzwerkVerbindenAsync()
{
    // nmtui ist ein eigenes Textprogramm und bekommt das Terminal ganz für sich.
    try
    {
        using var p = Process.Start(new ProcessStartInfo("nmtui") { UseShellExecute = false })!;
        await p.WaitForExitAsync();
    }
    catch (Exception e)
    {
        AnsiConsole.MarkupLine($"[red]nmtui lässt sich nicht starten:[/] {Markup.Escape(e.Message)}");
        return;
    }
    AnsiConsole.Clear();
    if (AnsiConsole.Confirm("Netzwerkprüfung jetzt wiederholen?", true))
        foreach (var m in module.Where(m => m.Gruppe == "Netzwerk")) await ModulLaufenLassen(m, fragen: false);
}

static bool ProgrammVorhanden(string name) =>
    (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
        .Any(d => File.Exists(Path.Combine(d, name)));

async Task ModulLaufenLassen(ModulDefinition m, bool fragen)
{
    if (m.Schreibend && !AnsiConsole.Confirm($"[yellow]'{Markup.Escape(m.Titel)}' verändert Daten auf dem Prüfling.[/] Wirklich ausführen?", false)) return;

    var parameter = new Dictionary<string, string>();
    foreach (var p in m.Parameter)
        parameter[p.Name] = fragen
            ? AnsiConsole.Prompt(new TextPrompt<string>(Markup.Escape(p.Frage)).DefaultValue(p.Standard))
            : p.Standard;

    ModulErgebnis erg = null!;
    await AnsiConsole.Status().StartAsync($"{m.Titel} …", async _ => { erg = await ausfuehrer.AusfuehrenAsync(m, parameter); });
    session.Hinzufuegen(erg);

    AnsiConsole.MarkupLine($"{Farbe(erg.Status)} [bold]{Markup.Escape(erg.Titel)}[/]");
    foreach (var s in erg.Schritte)
    {
        AnsiConsole.MarkupLine($"   {Farbe(s.Status)} {Markup.Escape(s.Titel)}");
        foreach (var meldung in s.Meldungen) AnsiConsole.MarkupLine($"        [grey]•[/] {Markup.Escape(meldung)}");
    }
}

void Zusammenfassung()
{
    var tabelle = new Table().Border(TableBorder.Rounded).AddColumn("Bewertung").AddColumn("Prüfung");
    foreach (var e in session.Ergebnisse) tabelle.AddRow(Farbe(e.Status), Markup.Escape(e.Titel));
    AnsiConsole.Write(tabelle);
    var gesamt = ModulAusfuehrer.ZusammenfassenStatus(session.Ergebnisse.Select(e => e.Status));
    AnsiConsole.MarkupLine($"Gesamtbewertung: {Farbe(gesamt)}");
    session.Speichern();
    AnsiConsole.MarkupLine($"Bericht: [bold]{Markup.Escape(Path.Combine(session.Verzeichnis, "bericht.md"))}[/]");
}

// Die Linux-Textkonsole (TERM=linux) hat keine Glyphen für ✔ ⚠ ✘ ℹ – dort ASCII-Kennzeichen.
static string Farbe(Status s)
{
    var einfach = Environment.GetEnvironmentVariable("TERM") == "linux";
    return s switch
    {
        Status.Ok => einfach ? "[green][[OK]][/]" : "[green]✔ OK[/]",
        Status.Warnung => einfach ? "[yellow][[WARNUNG]][/]" : "[yellow]⚠ WARNUNG[/]",
        Status.Fehler => einfach ? "[red][[FEHLER]][/]" : "[red]✘ FEHLER[/]",
        Status.Uebersprungen => einfach ? "[grey][[uebersprungen]][/]" : "[grey]– übersprungen[/]",
        _ => einfach ? "[blue][[Info]][/]" : "[blue]ℹ Info[/]",
    };
}

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
        AnsiConsole.MarkupLine($"[yellow]Stick nicht beschreibbar – Protokolle gehen nach {Markup.Escape(ersatz)}[/]");
        return ersatz;
    }
}
