// Stick-Manager: erstellt den Diagnostiker-Stick (nur unter Linux).
// Aufruf: diagnostiker-stick [laufwerke | aktualisieren [--ja]]
//   ohne Argument       Menü
//   laufwerke           Laufwerke auflisten und bewerten (nur lesend), ohne Menü
//   paket-pruefen       neueste Paketversion lesen und die Signatur prüfen (nichts wird geschrieben)
//   paket-erstellen     Stick aus einem signierten Paket erstellen (ohne Docker; auch im Menü). Optionen: --quelle <Ordner|https-Adresse>
//   aktualisieren       Programm und Module auf einem vorhandenen Stick auf den neuesten Stand bringen
//                       (mit --ja ohne Rückfrage, mit --plan nur anzeigen, mit --basis auch das Live-System ersetzen; protokolle/ wird nie angefasst)
using Diagnostiker.StickKern;
using Diagnostiker.StickKern.Paket;
using Spectre.Console;

// Gewählte Zeile: weiß auf blau, gut lesbar auch auf der schwarzen Linux-Konsole (Standard wäre dunkelblaue Schrift).
Style Markierung = new(Color.White, Color.Blue);

if (!OperatingSystem.IsLinux())
{
    AnsiConsole.MarkupLine("[red]Der Stick-Manager läuft nur unter Linux.[/]");
    return 1;
}

// Herausgeber-Befehle (kein Laufwerkszugriff):
//   schluessel-erzeugen <datei>                                       neues Signaturschlüsselpaar (privat in <datei>, Rechte 600; öffentlich auf der Ausgabe)
//   paket-bauen <boot.img> <datenordner> <ausgabe> <version> <schluesseldatei> [--bootmib N] [--folgeschluessel <öffentlicher Schlüssel>]
if (args.Length > 0 && args[0] == "schluessel-erzeugen") return SchluesselErzeugen(args);
if (args.Length > 0 && args[0] == "paket-bauen") return PaketBauen(args);

var quelle = new LsblkQuelle();
var befehle = new ProzessBefehle();

if (args.Contains("paket-pruefen"))
    return await PaketPruefenAsync(Option("--quelle")) ? 0 : 1;
if (args.Contains("paket-erstellen"))
    return await PaketErstellenAsync(Option("--quelle")) ? 0 : 1;

if (args.Contains("laufwerke"))
{
    await LaufwerkeZeigenAsync();
    return 0;
}

var projekt = ProjektOrdner.Finden(AppContext.BaseDirectory, Directory.GetCurrentDirectory());

if (args.Contains("aktualisieren"))
{
    return await StickAktualisierenAsync(args.Contains("--ja"), args.Contains("--plan"), args.Contains("--basis")) ? 0 : 1;
}

while (true)
{
    AnsiConsole.Write(new Rule("Diagnostiker – Stick-Manager") { Justification = Justify.Left });
    var wahl = AnsiConsole.Prompt(new SelectionPrompt<string>()
        .HighlightStyle(Markierung)
        .Title("Was soll getan werden?")
        .AddChoices("Laufwerke ansehen", "Stick erstellen (fertiges Paket laden)", "Stick erstellen (aus eigenem Bau, Docker)", "Stick aktualisieren", "Abbild (ISO) bauen", "Beenden"));
    switch (wahl)
    {
        case "Beenden": return 0;
        case "Laufwerke ansehen": await LaufwerkeZeigenAsync(); break;
        case "Abbild (ISO) bauen": await IsoBauenAsync(); break;
        case "Stick erstellen (fertiges Paket laden)": await PaketErstellenAsync(null); break;
        case "Stick erstellen (aus eigenem Bau, Docker)": await StickErstellenAsync(); break;
        case "Stick aktualisieren": await StickAktualisierenAsync(false, false, false); break;
    }
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

IPaketQuelle PaketQuelle(string? angabe) => PaketQuellen.Aus(angabe);

async Task<bool> PaketPruefenAsync(string? quellenangabe)
{
    try
    {
        var holen = new PaketHolen(new VertrauensSpeicher(VertrauensSpeicher.Standard()).Laden(), new VertrauensSpeicher(VertrauensSpeicher.Standard()), PaketHolen.StandardZwischenspeicher());
        var (m, _) = await holen.ManifestPruefenAsync(PaketQuelle(quellenangabe), CancellationToken.None);
        AnsiConsole.MarkupLine($"[green]Signatur in Ordnung.[/] Version [bold]{Markup.Escape(m.Version)}[/], erstellt {m.Erstellt:yyyy-MM-dd HH:mm} UTC.");
        foreach (var d in m.Dateien) AnsiConsole.MarkupLine($"  {Markup.Escape(d.Name)}: {Sicherheit.Groesse(d.Groesse)}");
        return true;
    }
    catch (Exception e) when (e is PaketException or HttpRequestException or IOException)
    { Fehler(e is HttpRequestException ? "Das Paket lässt sich nicht laden: " + e.Message : e.Message); return false; }
    catch (Exception e)
    { Fehler("Unerwarteter Fehler: " + e); return false; }
}

async Task<bool> PaketErstellenAsync(string? quellenangabe)
{
    try
    {
        // 1. Paket holen und prüfen (vor jeder Änderung an einem Laufwerk)
        var speicher = new VertrauensSpeicher(VertrauensSpeicher.Standard());
        var holen = new PaketHolen(speicher.Laden(), speicher, PaketHolen.StandardZwischenspeicher());
        GeholtesPaket paket = null!;
        await AnsiConsole.Progress().AutoClear(false).Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new TransferSpeedColumn())
            .StartAsync(async ctx =>
            {
                var aufgaben = new Dictionary<string, ProgressTask>();
                paket = await holen.HolenAsync(PaketQuelle(quellenangabe), z => AnsiConsole.MarkupLineInterpolated($"[grey]{z}[/]"),
                    (name, bytes, gesamt) =>
                    {
                        if (!aufgaben.TryGetValue(name, out var t)) aufgaben[name] = t = ctx.AddTask(name, maxValue: Math.Max(gesamt, 1));
                        t.Value = bytes;
                    }, CancellationToken.None);
            });

        // 2. Laufwerk wählen
        var alle = await quelle.LesenAsync(CancellationToken.None);
        var geeignet = alle.Where(l => Sicherheit.Pruefe(l).Erlaubt).ToList();
        if (geeignet.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Kein geeigneter USB-Stick gefunden.[/] Stick einstecken und erneut versuchen. Das Paket ist geladen und geprüft, es muss nicht noch einmal geladen werden.");
            return false;
        }
        var wahlen = geeignet.Select(l => $"{l.Pfad}  {l.Beschreibung}  {Sicherheit.Groesse(l.Groesse)}").Append("Abbrechen").ToList();
        var gewaehlt = AnsiConsole.Prompt(new SelectionPrompt<string>().HighlightStyle(Markierung).Title("Auf welchen Stick soll geschrieben werden?").AddChoices(wahlen));
        if (gewaehlt == "Abbrechen") return true;
        var laufwerk = geeignet[wahlen.IndexOf(gewaehlt)];

        // 3. Bestätigen
        var pruefung = Sicherheit.Pruefe(laufwerk);
        AnsiConsole.Write(new Panel(new Markup(
            $"[bold]{Markup.Escape(laufwerk.Pfad)}[/]  {Markup.Escape(laufwerk.Beschreibung)}  {Sicherheit.Groesse(laufwerk.Groesse)}\n" +
            string.Join("\n", pruefung.Hinweise.Select(h => "• " + Markup.Escape(h)))))
            .Header("Ausgewählter Stick").RoundedBorder());
        AnsiConsole.MarkupLine($"[bold red]ALLES auf {Markup.Escape(laufwerk.Pfad)} wird gelöscht und lässt sich nicht wiederherstellen.[/]");
        var eingabe = AnsiConsole.Prompt(new TextPrompt<string>(
                $"Zur Bestätigung den Gerätenamen eingeben ([bold]{Markup.Escape(Bestaetigung.Erwartet(laufwerk))}[/]), leer lassen zum Abbrechen:").AllowEmpty());
        if (string.IsNullOrWhiteSpace(eingabe)) { AnsiConsole.MarkupLine("Abgebrochen, es wurde nichts verändert."); return true; }

        // 4. Schreiben (dd-Fortschrittszeilen nur jede fünfte zeigen)
        var zaehler = 0;
        using var log = new LaufProtokoll();
        void Protokoll(string z) => log.Schreiben(z);
        AnsiConsole.MarkupLineInterpolated($"[grey]Protokoll: {log.Pfad}[/]");
        Protokoll($"Start: {laufwerk.Pfad}");
        var ersteller = new PaketStickErsteller(quelle, befehle);
        var ergebnis = await ersteller.ErstellenAsync(laufwerk, eingabe, paket, z =>
        {
            Protokoll(z);
            if (z.Contains(" copied, ") && ++zaehler % 5 != 0) return;
            AnsiConsole.MarkupLineInterpolated($"[grey]{z}[/]");
        });
        Protokoll($"Ergebnis: Erfolg={ergebnis.Erfolg} {ergebnis.Meldung}");
        if (ergebnis.Erfolg) AnsiConsole.MarkupLine($"[green]{Markup.Escape(ergebnis.Meldung)}[/]");
        else Fehler(ergebnis.Meldung);
        return ergebnis.Erfolg;
    }
    catch (Exception e) when (e is PaketException or HttpRequestException or IOException)
    { Fehler(e is HttpRequestException ? "Das Paket lässt sich nicht laden: " + e.Message : e.Message); return false; }
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
    var gewaehlt = AnsiConsole.Prompt(new SelectionPrompt<string>().HighlightStyle(Markierung).Title("Auf welchen Stick soll geschrieben werden?").AddChoices(wahlen));
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

async Task<bool> StickAktualisierenAsync(bool ohneRueckfrage, bool nurPlan, bool basisErzwingen)
{
    if (projekt is null) { Fehler("Projektordner nicht gefunden. Bitte im Projektordner starten."); return false; }
    if (!File.Exists(Path.Combine(projekt, "build", "out", "diagnostiker.iso")))
    {
        Fehler("Es gibt noch kein Live-Abbild (wird für den Stand der Basis gebraucht). Zuerst „Abbild (ISO) bauen“ wählen.");
        return false;
    }

    var aktualisierer = new StickAktualisierer(quelle, befehle, projekt);
    var v = await aktualisierer.VorbereitenAsync(z => AnsiConsole.MarkupLineInterpolated($"[grey]{z}[/]"));
    if (v.Plan is null) { Fehler(v.Fehler ?? "Unbekannter Fehler."); return false; }

    var plan = v.Plan;
    AnsiConsole.MarkupLine($"Stick: [bold]{Markup.Escape(plan.StickOrdner)}[/]");
    if (plan.KeineAenderung) AnsiConsole.MarkupLine("[green]Programm und Module sind auf dem neuesten Stand.[/]");
    else
    {
        var tabelle = new Table().RoundedBorder().AddColumn("Änderung").AddColumn("Datei").AddColumn("Größe");
        foreach (var a in plan.Aenderungen)
            tabelle.AddRow(a.Art switch { AenderungsArt.Neu => "[green]neu[/]", AenderungsArt.Geaendert => "[yellow]geändert[/]", _ => "[red]entfernt[/]" },
                Markup.Escape(a.Pfad), a.Art == AenderungsArt.Entfernt ? "" : Sicherheit.Groesse(a.Groesse));
        AnsiConsole.Write(tabelle);
        AnsiConsole.MarkupLine("Der Ordner [bold]protokolle[/] und eigene Dateien außerhalb von bin und module (sowie fremde Werkzeuge) bleiben unberührt.");
    }
    var basis = false;
    if (plan.BasisVeraltet)
    {
        AnsiConsole.MarkupLine("[yellow]Hinweis:[/] Das Live-System (Basis) auf dem Stick ist älter als das gebaute Abbild.");
        basis = basisErzwingen || (!ohneRueckfrage && !nurPlan &&
            AnsiConsole.Confirm("Auch das Live-System aktualisieren? (Dauert einige Minuten; Protokolle und Daten bleiben erhalten, der Stick darf dabei nicht abgezogen werden.)", true));
        if (!basis && !nurPlan) AnsiConsole.MarkupLine("Das Live-System wird nicht aktualisiert (mit --basis oder auf Rückfrage möglich).");
    }

    if (nurPlan)
    {
        await aktualisierer.FreigebenAsync(v, _ => { });
        AnsiConsole.MarkupLine("Nur Planansicht, es wurde nichts verändert.");
        return true;
    }
    if (!plan.KeineAenderung && !ohneRueckfrage && !AnsiConsole.Confirm("Änderungen auf den Stick übernehmen?", false))
    {
        await aktualisierer.FreigebenAsync(v, _ => { });
        AnsiConsole.MarkupLine("Abgebrochen, es wurde nichts verändert.");
        return true;
    }

    var e = await aktualisierer.AnwendenAsync(v, z => AnsiConsole.MarkupLineInterpolated($"[grey]{z}[/]"), basis);
    if (e.Erfolg) AnsiConsole.MarkupLine($"[green]{Markup.Escape(e.Meldung)}[/]");
    else Fehler(e.Meldung);
    return e.Erfolg;
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


static int SchluesselErzeugen(string[] a)
{
    if (a.Length < 2) { Fehler("Aufruf: schluessel-erzeugen <datei für den privaten Schlüssel>"); return 2; }
    var datei = a[1];
    if (File.Exists(datei)) { Fehler($"{datei} existiert bereits und wird nicht überschrieben."); return 1; }
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(datei))!);
    var (priv, pub) = Diagnostiker.StickKern.Paket.Signatur.SchluesselErzeugen();
    File.WriteAllText(datei, priv + "\n");
    File.SetUnixFileMode(datei, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.WriteLine(pub);
    Console.Error.WriteLine($"Privater Schlüssel: {datei} (Rechte 600). Eine Kopie gehört offline in die Sicherung, nicht ins Repo.");
    return 0;
}

static int PaketBauen(string[] a)
{
    if (a.Length < 6) { Fehler("Aufruf: paket-bauen <boot.img> <datenordner> <ausgabe> <version> <schluesseldatei> [--bootmib N] [--folgeschluessel K]"); return 2; }
    var bootMib = 2560; string? folge = null;
    for (var i = 6; i < a.Length - 1; i++)
    {
        if (a[i] == "--bootmib") bootMib = int.Parse(a[i + 1]);
        if (a[i] == "--folgeschluessel") folge = a[i + 1];
    }
    try
    {
        var m = Diagnostiker.StickKern.Paket.PaketBauer.Bauen(a[1], a[2], a[3], a[4], bootMib, File.ReadAllText(a[5]).Trim(), folge,
            z => AnsiConsole.MarkupLineInterpolated($"[grey]{z}[/]"));
        AnsiConsole.MarkupLineInterpolated($"[green]Paket {m.Version} gebaut:[/] {a[3]}");
        foreach (var d in m.Dateien) Console.WriteLine($"  {d.Name}  {Sicherheit.Groesse(d.Groesse)}  {d.Sha256}");
        return 0;
    }
    catch (Diagnostiker.StickKern.Paket.PaketException e) { Fehler(e.Message); return 1; }
}
