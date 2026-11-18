using Diagnostiker.StickKern;
using Xunit;

namespace Diagnostiker.Tests;

public class FortschrittAuswertungTests
{
    const long Abbild = 2_686_451_712;

    [Fact]
    public void Schritte_folgen_der_Helferausgabe()
    {
        var a = new FortschrittAuswertung(Abbild);
        Assert.True(a.Zeile("== Abbilddatei prüfen"));
        Assert.Equal("Paketdatei prüfen", a.Schritt);
        Assert.Null(a.Prozent);
        a.Zeile("== Abbild schreiben (2562 MiB)");
        Assert.Equal("Abbild auf den Stick schreiben", a.Schritt);
        Assert.Equal(0, a.Prozent);
    }

    [Fact]
    public void Dd_Zeile_ergibt_Prozent_und_Geschwindigkeit()
    {
        var a = new FortschrittAuswertung(Abbild);
        a.Zeile("== Abbild schreiben (2562 MiB)");
        Assert.True(a.Zeile("1343225856 bytes (1.3 GB, 1.2 GiB) copied, 61.2 s, 21.9 MB/s"));
        Assert.Equal(50.0, a.Prozent!.Value, 1);
        Assert.Equal("21,9 MB/s", a.Geschwindigkeit);
        Assert.InRange(a.Gesamt, 4 + 55 * 0.49, 4 + 55 * 0.51);
    }

    [Fact]
    public void Dd_Zeile_ausserhalb_von_Schreiben_und_Lesen_wird_ignoriert()
    {
        var a = new FortschrittAuswertung(Abbild);
        a.Zeile("== Partitionstabelle fertigstellen");
        Assert.False(a.Zeile("1343225856 bytes (1.3 GB, 1.2 GiB) copied, 61.2 s, 21.9 MB/s"));
        Assert.Null(a.Prozent);
    }

    [Fact]
    public void Der_Schritt_geht_nie_rueckwaerts()
    {
        var a = new FortschrittAuswertung(Abbild);
        a.Zeile("== 10:09:33 Zurücklesen und vergleichen");
        Assert.False(a.Zeile("== Abbild schreiben (2562 MiB)"));
        Assert.Equal("Zurücklesen und vergleichen", a.Schritt);
    }

    [Fact]
    public void Gesamtfortschritt_steigt_ueber_alle_Schritte()
    {
        var a = new FortschrittAuswertung(Abbild);
        var letzte = -1.0;
        foreach (var z in new[]
        {
            "== Abbilddatei prüfen", "== Laufwerk /dev/sdc (58 GiB) wird vollständig neu angelegt", "== Abbild schreiben (2562 MiB)",
            "2686451712 bytes (2.7 GB, 2.5 GiB) copied, 120 s, 22.4 MB/s", "== 10:09:33 Schreiben beendet, Zwischenspeicher leeren",
            "== 10:09:33 Zurücklesen und vergleichen", "2686451712 bytes (2.7 GB, 2.5 GiB) copied, 18 s, 149 MB/s", "== 10:09:52 Vergleich in Ordnung",
            "== 10:09:52 Partitionstabelle fertigstellen", "== Datenpartition formatieren (exFAT, DIAGDATEN)", "Datenpartition einhängen …",
            "Programme und Module auf den Stick kopieren (/x) …", "Daten sichern und Stick aushängen …",
        })
        {
            a.Zeile(z);
            Assert.True(a.Gesamt >= letzte, z);
            letzte = a.Gesamt;
        }
        a.Fertig();
        Assert.Equal(100, a.Gesamt);
    }

    [Fact]
    public void Leere_und_fremde_Zeilen_aendern_nichts()
    {
        var a = new FortschrittAuswertung(Abbild);
        Assert.False(a.Zeile(""));
        Assert.False(a.Zeile("irgendeine Meldung"));
        Assert.Equal("", a.Schritt);
    }
}
