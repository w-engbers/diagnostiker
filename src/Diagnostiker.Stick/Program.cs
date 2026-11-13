// Stick-Manager: erstellt und aktualisiert den Diagnostiker-Stick (nur unter Linux).
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

if (args.Contains("laufwerke"))
{
    await LaufwerkeZeigenAsync();
    return 0;
}

while (true)
{
    AnsiConsole.Write(new Rule("Diagnostiker – Stick-Manager") { Justification = Justify.Left });
    var wahl = AnsiConsole.Prompt(new SelectionPrompt<string>()
        .Title("Was soll getan werden?")
        .AddChoices("Laufwerke ansehen", "Beenden"));
    if (wahl == "Beenden") return 0;
    await LaufwerkeZeigenAsync();
}

static async Task LaufwerkeZeigenAsync()
{
    IReadOnlyList<Laufwerk> laufwerke;
    try { laufwerke = await LaufwerkLeser.LesenAsync(); }
    catch (Exception e) { AnsiConsole.MarkupLine($"[red]Laufwerke lassen sich nicht lesen:[/] {Markup.Escape(e.Message)}"); return; }

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

    var geeignet = laufwerke.Count(l => Sicherheit.Pruefe(l).Erlaubt);
    AnsiConsole.MarkupLine(geeignet == 0
        ? "[yellow]Kein geeigneter USB-Stick gefunden.[/] Stick einstecken und erneut ansehen."
        : $"[green]{geeignet} geeignete(r) Stick(s).[/]");
}
