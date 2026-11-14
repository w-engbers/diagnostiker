using Diagnostiker.Kern;
using Spectre.Console;
using Status = Diagnostiker.Kern.Status;

namespace Diagnostiker.Tui;

/// <summary>Einfache Textansicht (Menü und fortlaufende Ausgabe, Spectre.Console). Rückfall, falls die Bildschirmoberfläche
/// nicht startet, außerdem für <c>--text</c> und die nicht interaktive <c>--schnell</c>-Prüfung.</summary>
sealed class TextOberflaeche(Kontext k)
{
    // Gewählte Zeile: weiß auf blau, gut lesbar auch auf der schwarzen Linux-Konsole (Standard wäre dunkelblaue Schrift).
    static readonly Style Markierung = new(Color.White, Color.Blue);
    const string NetzwerkEintrag = "Netzwerk verbinden (WLAN/LAN einrichten)";

    void Kopf()
    {
        AnsiConsole.Write(new Rule("[bold]Diagnostiker[/] – Prüfstick").LeftJustified());
        AnsiConsole.MarkupLine($"Rechner: [bold]{Markup.Escape(k.Session.Rechnername)}[/]   System: {Plattform.OsName}   " +
            (Plattform.HatAdminRechte ? "[green]Administratorrechte: ja[/]" : "[yellow]Administratorrechte: nein[/] (einige Prüfungen werden übersprungen)"));
        AnsiConsole.MarkupLine($"Protokoll: [grey]{Markup.Escape(k.Session.Verzeichnis)}[/]");
        foreach (var f in k.Ladefehler) AnsiConsole.MarkupLine($"[red]Modul fehlerhaft:[/] {Markup.Escape(f)}");
        AnsiConsole.WriteLine();
    }

    /// <summary>Alle Schnellprüfungen ohne Rückfragen, dann die Zusammenfassung.</summary>
    public async Task<int> SchnellAsync()
    {
        Kopf();
        foreach (var m in k.Schnellpruefungen) await ModulLaufenLassen(m, fragen: false);
        Zusammenfassung();
        return 0;
    }

    public async Task<int> MenueAsync()
    {
        Kopf();
        while (true)
        {
            var auswahl = new List<string> { "Schnellprüfung (alle harmlosen Standardtests)", "Einzelne Prüfung wählen" };
            if (k.NetzwerkEinrichten) auswahl.Add(NetzwerkEintrag);
            auswahl.AddRange(["Notiz zum Protokoll hinzufügen", "Zusammenfassung anzeigen", "Beenden"]);
            var wahl = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .HighlightStyle(Markierung)
                .Title("Was soll geprüft werden?")
                .PageSize(15)
                .AddChoices(auswahl));

            switch (wahl)
            {
                case var w when w.StartsWith("Schnell"):
                    foreach (var m in k.Schnellpruefungen) await ModulLaufenLassen(m, fragen: true);
                    Zusammenfassung();
                    break;
                case "Einzelne Prüfung wählen":
                    var gewaehlt = AnsiConsole.Prompt(new SelectionPrompt<ModulDefinition>()
                        .HighlightStyle(Markierung)
                        .Title("Welche Prüfung?")
                        .PageSize(15)
                        .UseConverter(m => $"{m.Gruppe}: {m.Titel}")
                        .AddChoices(k.Module));
                    await ModulLaufenLassen(gewaehlt, fragen: true);
                    break;
                case NetzwerkEintrag:
                    await NetzwerkVerbindenAsync();
                    break;
                case "Notiz zum Protokoll hinzufügen":
                    k.Session.Notiz = AnsiConsole.Prompt(new TextPrompt<string>("Notiz (z. B. Name/Beschwerde des Besitzers):").AllowEmpty());
                    k.Session.Speichern();
                    break;
                case "Zusammenfassung anzeigen":
                    Zusammenfassung();
                    break;
                default:
                    k.Session.Speichern();
                    AnsiConsole.MarkupLine($"Bericht: [bold]{Markup.Escape(Path.Combine(k.Session.Verzeichnis, "bericht.md"))}[/]");
                    return 0;
            }
            AnsiConsole.WriteLine();
        }
    }

    async Task NetzwerkVerbindenAsync()
    {
        string? fehler = null;
        await AnsiConsole.Status().StartAsync("Suche WLAN-Netze …", async _ => { fehler = await Kontext.NetzwerkEinrichtenAsync(); });
        if (fehler is not null) { AnsiConsole.MarkupLine($"[red]{Markup.Escape(fehler)}[/]"); return; }
        AnsiConsole.Clear();
        if (AnsiConsole.Confirm("Netzwerkprüfung jetzt wiederholen?", true))
            foreach (var m in k.Module.Where(m => m.Gruppe == "Netzwerk")) await ModulLaufenLassen(m, fragen: false);
    }

    async Task ModulLaufenLassen(ModulDefinition m, bool fragen)
    {
        if (m.Schreibend && !AnsiConsole.Confirm($"[yellow]'{Markup.Escape(m.Titel)}' verändert Daten auf dem Prüfling.[/] Wirklich ausführen?", false)) return;

        var parameter = new Dictionary<string, string>();
        foreach (var p in m.Parameter)
        {
            if (!fragen) { parameter[p.Name] = p.Standard; continue; }
            var frage = new TextPrompt<string>(Markup.Escape(p.Frage)).DefaultValue(p.Standard).AllowEmpty();
            if (p.Geheim) frage = frage.Secret();   // Eingabe als Sternchen
            parameter[p.Name] = AnsiConsole.Prompt(frage);
        }

        ModulErgebnis erg = null!;
        await AnsiConsole.Status().StartAsync($"{m.Titel} …", async _ => { erg = await k.Ausfuehrer.AusfuehrenAsync(m, parameter); });
        k.Session.Hinzufuegen(erg);

        AnsiConsole.MarkupLine($"{Farbe(erg.Status)} [bold]{Markup.Escape(erg.Titel)}[/]");
        foreach (var s in erg.Schritte)
        {
            AnsiConsole.MarkupLine($"   {Farbe(s.Status)} {Markup.Escape(s.Titel)}");
            foreach (var meldung in s.Meldungen) AnsiConsole.MarkupLine($"        [grey]•[/] {Markup.Escape(meldung)}");
        }
    }

    void Zusammenfassung()
    {
        // Je Prüfung nur der neueste Lauf; war es ein Wiederholungslauf, steht der vorherige Befund daneben.
        var tabelle = new Table().Border(TableBorder.Rounded).AddColumn("Bewertung").AddColumn("Prüfung").AddColumn("Stand").AddColumn("Vorher");
        foreach (var e in k.Session.Aktuelle)
        {
            var vorher = k.Session.Vorheriger(e);
            tabelle.AddRow(Farbe(e.Status), Markup.Escape(e.Titel), $"{e.Beginn:HH:mm}",
                vorher is null ? "" : $"[grey]{vorher.Beginn:HH:mm} {Markup.Escape(Session.Text(vorher.Status))}[/]");
        }
        AnsiConsole.Write(tabelle);
        AnsiConsole.MarkupLine($"Gesamtbewertung: {Farbe(k.Gesamt)}");
        k.Session.Speichern();
        AnsiConsole.MarkupLine($"Bericht: [bold]{Markup.Escape(Path.Combine(k.Session.Verzeichnis, "bericht.md"))}[/]");
    }

    // Die Linux-Textkonsole (TERM=linux) hat keine Glyphen für ✔ ⚠ ✘ ℹ – dort ASCII-Kennzeichen.
    static string Farbe(Status s)
    {
        var term = Environment.GetEnvironmentVariable("TERM") ?? "";
        var einfach = term == "linux" || term.StartsWith("screen");
        return s switch
        {
            Status.Ok => einfach ? "[green][[OK]][/]" : "[green]✔ OK[/]",
            Status.Warnung => einfach ? "[yellow][[WARNUNG]][/]" : "[yellow]⚠ WARNUNG[/]",
            Status.Fehler => einfach ? "[red][[FEHLER]][/]" : "[red]✘ FEHLER[/]",
            Status.Uebersprungen => einfach ? "[grey][[uebersprungen]][/]" : "[grey]– übersprungen[/]",
            _ => einfach ? "[blue][[Info]][/]" : "[blue]ℹ Info[/]",
        };
    }
}
