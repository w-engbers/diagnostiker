using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

public class BewertungTests
{
    private static SchrittErgebnis Bewerte(string ausgabe, int exit, params Regel[] regeln)
    {
        var def = new SchrittDefinition { Regeln = [.. regeln] };
        var res = new SchrittErgebnis { Ausgabe = ausgabe, ExitCode = exit };
        ModulAusfuehrer.Bewerten(def, res);
        return res;
    }

    [Fact]
    public void KeineRegelTrifft_ErgibtInfo() =>
        Assert.Equal(Status.Info, Bewerte("irgendwas", 0, new Regel { Wert = "xyz", Status = Status.Fehler }).Status);

    [Fact]
    public void SchlimmsterStatusGewinnt()
    {
        var r = Bewerte("PASSED und APIPA", 0,
            new Regel { Wert = "PASSED", Status = Status.Ok, Meldung = "gut" },
            new Regel { Wert = "APIPA", Status = Status.Fehler, Meldung = "schlecht" });
        Assert.Equal(Status.Fehler, r.Status);
        Assert.Equal(2, r.Meldungen.Count);
    }

    [Fact]
    public void InfoNeben_Ok_BleibtOk() =>
        Assert.Equal(Status.Ok, ModulAusfuehrer.ZusammenfassenStatus([Status.Info, Status.Ok, Status.Uebersprungen]));

    [Fact]
    public void NurUebersprungen_IstNichtOk() =>
        Assert.Equal(Status.Uebersprungen, ModulAusfuehrer.ZusammenfassenStatus([Status.Uebersprungen]));

    [Fact]
    public void ZeigeAusgabe_blendet_reine_Markierungszeilen_aus_die_Regeln_sehen_sie_trotzdem()
    {
        var def = new SchrittDefinition
        {
            ZeigeAusgabe = true,
            Regeln = [new Regel { Wert = "KEIN_ADAPTER_BT", Status = Status.Info, Meldung = "Kein Adapter." }],
        };
        var res = new SchrittErgebnis { Ausgabe = "KEIN_ADAPTER_BT\nHersteller: Test\nPASSED\nSMART_OK_1\n", ExitCode = 0 };
        ModulAusfuehrer.Bewerten(def, res);

        Assert.DoesNotContain("KEIN_ADAPTER_BT", res.Meldungen);
        Assert.DoesNotContain("SMART_OK_1", res.Meldungen);
        Assert.Contains("Hersteller: Test", res.Meldungen);
        Assert.Contains("PASSED", res.Meldungen);             // ohne Unterstrich: ein normales Wort, bleibt sichtbar
        Assert.Contains("Kein Adapter.", res.Meldungen);      // die Regel hat die Markierung gesehen
    }

    [Fact]
    public void Exitcode_Regeln()
    {
        Assert.Equal(Status.Ok, Bewerte("", 1, new Regel { Typ = "exitcode", Wert = "1", Status = Status.Ok }).Status);
        Assert.Equal(Status.Warnung, Bewerte("", 3, new Regel { Typ = "exitcodeNicht", Wert = "0", Status = Status.Warnung }).Status);
    }

    [Fact]
    public void SmartRegex_FindetUmgelagerteSektoren()
    {
        const string smart = "  5 Reallocated_Sector_Ct   0x0033   100   100   010    Pre-fail  Always       -       12\n";
        var regel = new Regel { Typ = "regex", Wert = @"^\s*(5|196|197|198)\s+\S+.*\s[1-9]\d*\s*$", Status = Status.Warnung };
        Assert.Equal(Status.Warnung, Bewerte(smart, 0, regel).Status);
        Assert.Equal(Status.Info, Bewerte(smart.Replace("-       12", "-       0"), 0, regel).Status);
    }

    [Fact]
    public void AlleManifeste_SindGueltig()
    {
        var dir = ModulLader.ModulVerzeichnisFinden();
        Assert.NotNull(dir);
        var (module, fehler) = ModulLader.Laden(dir!);
        Assert.Empty(fehler);
        Assert.NotEmpty(module);
        Assert.Equal(module.Count, module.Select(m => m.Id).Distinct().Count());
        foreach (var s in module.SelectMany(m => m.Schritte))
            Assert.All(s.Regeln, r => Assert.Contains(r.Typ.ToLowerInvariant(), new[] { "enthaelt", "regex", "exitcode", "exitcodenicht" }));
    }
}
