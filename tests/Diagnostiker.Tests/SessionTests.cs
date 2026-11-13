using System.Text.Json;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

public sealed class SessionTests : IDisposable
{
    readonly string _ordner = Directory.CreateTempSubdirectory("session").FullName;
    public void Dispose() => Directory.Delete(_ordner, true);

    static ModulErgebnis Lauf(string id, Status status, string ausgabe = "x", int minute = 0) => new()
    {
        Id = id, Titel = "Prüfung " + id, Status = status, Beginn = new DateTime(2026, 11, 13, 12, minute, 0),
        Schritte = [new SchrittErgebnis { Titel = "Schritt", Status = status, Ausgabe = ausgabe, ExitCode = 0, Meldungen = ["Befund"] }],
    };

    [Fact]
    public void Wiederholte_Pruefung_ersetzt_den_alten_Lauf_in_der_aktuellen_Sicht_und_behaelt_die_Reihenfolge()
    {
        var s = new Session(_ordner);
        s.Hinzufuegen(Lauf("a", Status.Ok));
        s.Hinzufuegen(Lauf("netz", Status.Warnung, minute: 18));
        s.Hinzufuegen(Lauf("b", Status.Ok));
        s.Hinzufuegen(Lauf("netz", Status.Ok, minute: 31));

        Assert.Equal(["a", "netz", "b"], s.Aktuelle.Select(e => e.Id));              // Reihenfolge des ersten Auftretens
        Assert.Equal(Status.Ok, s.Aktuelle.Single(e => e.Id == "netz").Status);      // der neue Lauf gilt
        Assert.Equal(4, s.Ergebnisse.Count);                                         // der Verlauf bleibt erhalten
    }

    [Fact]
    public void Gesamtbewertung_folgt_den_neuesten_Laeufen()
    {
        var s = new Session(_ordner);
        s.Hinzufuegen(Lauf("netz", Status.Warnung));
        Assert.Equal(Status.Warnung, ModulAusfuehrer.ZusammenfassenStatus(s.Aktuelle.Select(e => e.Status)));
        s.Hinzufuegen(Lauf("netz", Status.Ok));
        Assert.Equal(Status.Ok, ModulAusfuehrer.ZusammenfassenStatus(s.Aktuelle.Select(e => e.Status)));
        Assert.Contains("\"gesamtstatus\": \"Ok\"", File.ReadAllText(Path.Combine(s.Verzeichnis, "session.json")));
    }

    [Fact]
    public void Vorheriger_liefert_den_Lauf_davor()
    {
        var s = new Session(_ordner);
        var erster = Lauf("netz", Status.Warnung);
        var zweiter = Lauf("netz", Status.Ok);
        s.Hinzufuegen(erster);
        s.Hinzufuegen(zweiter);
        Assert.Null(s.Vorheriger(erster));
        Assert.Same(erster, s.Vorheriger(zweiter));
    }

    [Fact]
    public void Rohausgaben_frueherer_Laeufe_werden_nicht_ueberschrieben()
    {
        var s = new Session(_ordner);
        s.Hinzufuegen(Lauf("netz.adapter", Status.Warnung, "ERSTER LAUF"));
        s.Hinzufuegen(Lauf("netz.adapter", Status.Ok, "ZWEITER LAUF"));

        var roh = Path.Combine(s.Verzeichnis, "roh");
        Assert.Contains("ERSTER LAUF", File.ReadAllText(Path.Combine(roh, "netz_adapter-1.txt")));
        Assert.Contains("ZWEITER LAUF", File.ReadAllText(Path.Combine(roh, "netz_adapter-lauf2-1.txt")));
    }

    [Fact]
    public void Bericht_zeigt_je_Pruefung_eine_Zeile_und_den_Verlauf_der_Wiederholungen()
    {
        var s = new Session(_ordner);
        s.Hinzufuegen(Lauf("a", Status.Ok, minute: 10));
        s.Hinzufuegen(Lauf("netz", Status.Warnung, minute: 18));
        s.Hinzufuegen(Lauf("netz", Status.Ok, minute: 31));
        var bericht = s.BerichtErzeugen();

        var uebersicht = bericht.Split("## Übersicht")[1].Split("##")[0];
        Assert.Equal(2, uebersicht.Split('\n').Count(z => z.StartsWith("| [")));       // nur "a" und "netz", je einmal
        Assert.Contains("## Wiederholte Prüfungen (Verlauf)", bericht);
        Assert.Contains("12:18 Warnung → 12:31 OK", bericht);
        Assert.Contains("**Gesamtbewertung:** [OK]", bericht);
    }

    [Fact]
    public void Bericht_nennt_die_Bewertung_nur_einmal_und_ohne_Aufzaehlungsnamen_ohne_Umlaute()
    {
        var s = new Session(_ordner);
        s.Hinzufuegen(Lauf("a", Status.Uebersprungen));
        var bericht = s.BerichtErzeugen();
        Assert.DoesNotContain("Uebersprungen", bericht);
        Assert.Contains("| [übersprungen] | Prüfung a |", bericht);
        Assert.Contains("**Schritt** – übersprungen", bericht);
    }

    [Fact]
    public void Ohne_Wiederholung_gibt_es_keinen_Verlaufsabschnitt()
    {
        var s = new Session(_ordner);
        s.Hinzufuegen(Lauf("a", Status.Ok));
        Assert.DoesNotContain("Wiederholte Prüfungen", s.BerichtErzeugen());
    }

    [Fact]
    public void Session_json_enthaelt_aktuelle_Module_und_den_ganzen_Verlauf()
    {
        var s = new Session(_ordner);
        s.Hinzufuegen(Lauf("netz", Status.Warnung, minute: 18));
        s.Hinzufuegen(Lauf("netz", Status.Ok, minute: 31));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(s.Verzeichnis, "session.json")));

        Assert.Equal(1, doc.RootElement.GetProperty("module").GetArrayLength());
        Assert.Equal(2, doc.RootElement.GetProperty("verlauf").GetArrayLength());
    }
}
