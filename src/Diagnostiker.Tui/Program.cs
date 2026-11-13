// Diagnostiker – Textoberfläche.
// Aufrufe: Diagnostiker [--module <pfad>] [--protokolle <pfad>] [--schnell | --text]
//   ohne Option  Bildschirmoberfläche (Terminal.Gui); startet sie nicht, folgt die einfache Textansicht
//   --text       einfache Textansicht (Menü und fortlaufende Ausgabe)
//   --schnell    ohne Menü: alle Schnellprüfungen, dann die Zusammenfassung
using Diagnostiker.Tui;

Console.OutputEncoding = System.Text.Encoding.UTF8;
string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

var kontext = Kontext.Erstellen(Option("--module"), Option("--protokolle"));
if (kontext is null) return 2;

if (args.Contains("--schnell")) return await new TextOberflaeche(kontext).SchnellAsync();
if (args.Contains("--text")) return await new TextOberflaeche(kontext).MenueAsync();

try
{
    return await new GuiOberflaeche(kontext).LaufenAsync();
}
catch (Exception e)
{
    Console.Error.WriteLine($"Die Bildschirmoberfläche lässt sich nicht starten ({e.GetType().Name}: {e.Message}). Es folgt die einfache Textansicht.");
    return await new TextOberflaeche(kontext).MenueAsync();
}
