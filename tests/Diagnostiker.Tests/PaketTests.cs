using System.Text;
using Diagnostiker.StickKern.Paket;

namespace Diagnostiker.Tests;

public sealed class PaketTests : IDisposable
{
    readonly string _ordner = Directory.CreateTempSubdirectory("pakettest").FullName;
    public void Dispose() => Directory.Delete(_ordner, true);

    static string Hex(string s) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    static Manifest Beispiel(string? folge = null) => new(1, "2026.11.1", new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero), 2560,
        [new("boot.img.gz", "boot", 10, Hex("a")), new("daten.tar.gz", "daten", 5, Hex("b"))], folge);

    [Fact]
    public void Signatur_stimmt_nur_mit_dem_richtigen_Schluessel_und_unveraenderten_Daten()
    {
        var (priv, pub) = Signatur.SchluesselErzeugen();
        var (_, fremd) = Signatur.SchluesselErzeugen();
        var daten = Beispiel().ZuBytes();
        var sig = Signatur.Signieren(daten, priv);
        Assert.True(Signatur.Pruefen(daten, sig, pub));
        Assert.False(Signatur.Pruefen(daten, sig, fremd));
        var verfaelscht = (byte[])daten.Clone(); verfaelscht[^3] ^= 1;
        Assert.False(Signatur.Pruefen(verfaelscht, sig, pub));
        Assert.False(Signatur.Pruefen(daten, "kein base64!", pub));
        Assert.False(Signatur.Pruefen(daten, Convert.ToBase64String(new byte[10]), pub));
    }

    [Fact]
    public void Vertrauen_nimmt_nur_vertraut_signierte_Manifeste()
    {
        var (priv, pub) = Signatur.SchluesselErzeugen();
        var (priv2, _) = Signatur.SchluesselErzeugen();
        var daten = Beispiel().ZuBytes();
        var v = new Vertrauen([pub]);
        var (m, neu) = v.Pruefen(daten, Signatur.Signieren(daten, priv));
        Assert.Equal("2026.11.1", m.Version); Assert.Null(neu);
        var e = Assert.Throws<PaketException>(() => v.Pruefen(daten, Signatur.Signieren(daten, priv2)));
        Assert.Contains("Signatur", e.Message);
        Assert.Throws<PaketException>(() => new Vertrauen([]).Pruefen(daten, Signatur.Signieren(daten, priv)));
    }

    [Fact]
    public void Schluesselwechsel_gilt_erst_nach_gueltig_signiertem_Manifest()
    {
        var (alt, altPub) = Signatur.SchluesselErzeugen();
        var (neu, neuPub) = Signatur.SchluesselErzeugen();
        var v = new Vertrauen([altPub]);
        // Ein mit dem NEUEN Schlüssel signiertes Manifest wird vor dem Wechsel abgelehnt, auch wenn es den Schlüssel selbst nennt.
        var selbst = Beispiel(neuPub).ZuBytes();
        Assert.Throws<PaketException>(() => v.Pruefen(selbst, Signatur.Signieren(selbst, neu)));
        Assert.DoesNotContain(neuPub, v.Schluessel);
        // Der alte Schlüssel beglaubigt den neuen.
        var wechsel = Beispiel(neuPub).ZuBytes();
        var (_, vertraut) = v.Pruefen(wechsel, Signatur.Signieren(wechsel, alt));
        Assert.Equal(neuPub, vertraut);
        // Danach genügt der neue Schlüssel.
        var spaeter = Beispiel().ZuBytes();
        Assert.Equal("2026.11.1", v.Pruefen(spaeter, Signatur.Signieren(spaeter, neu)).Manifest.Version);
    }

    [Fact]
    public void Ungueltiger_Folgeschluessel_wird_abgelehnt()
    {
        var (priv, pub) = Signatur.SchluesselErzeugen();
        var daten = Beispiel("kein-schluessel").ZuBytes();
        Assert.Throws<PaketException>(() => new Vertrauen([pub]).Pruefen(daten, Signatur.Signieren(daten, priv)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("kein json")]
    public void Manifest_mit_falscher_Form_wird_abgelehnt(string text) =>
        Assert.Throws<PaketException>(() => Manifest.Lesen(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void Manifest_prueft_Dateinamen_Summen_und_Arten()
    {
        Assert.Equal("2026.11.1", Manifest.Lesen(Beispiel().ZuBytes()).Version);
        var pfad = Beispiel() with { Dateien = [new("../boot", "boot", 1, Hex("a")), new("d", "daten", 1, Hex("b"))] };
        Assert.Throws<PaketException>(() => Manifest.Lesen(pfad.ZuBytes()));
        var summe = Beispiel() with { Dateien = [new("b", "boot", 1, "zz"), new("d", "daten", 1, Hex("b"))] };
        Assert.Throws<PaketException>(() => Manifest.Lesen(summe.ZuBytes()));
        var doppelt = Beispiel() with { Dateien = [new("b", "boot", 1, Hex("a")), new("c", "boot", 1, Hex("a")), new("d", "daten", 1, Hex("b"))] };
        Assert.Throws<PaketException>(() => Manifest.Lesen(doppelt.ZuBytes()));
        var fehlt = Beispiel() with { Dateien = [new("b", "boot", 1, Hex("a"))] };
        Assert.Throws<PaketException>(() => Manifest.Lesen(fehlt.ZuBytes()));
        Assert.Throws<PaketException>(() => Manifest.Lesen((Beispiel() with { Format = 7 }).ZuBytes()));
    }

    [Fact]
    public void Dateipruefung_erkennt_falsche_Groesse_und_Summe()
    {
        var datei = Path.Combine(_ordner, "x.bin"); File.WriteAllText(datei, "inhalt");
        var summe = PaketPruefer.Sha256(datei);
        PaketPruefer.DateiPruefen(datei, new("x.bin", "boot", 6, summe));
        Assert.Contains("Größe", Assert.Throws<PaketException>(() => PaketPruefer.DateiPruefen(datei, new("x.bin", "boot", 7, summe))).Message);
        Assert.Contains("Prüfsumme", Assert.Throws<PaketException>(() => PaketPruefer.DateiPruefen(datei, new("x.bin", "boot", 6, Hex("a")))).Message);
        Assert.Throws<PaketException>(() => PaketPruefer.DateiPruefen(Path.Combine(_ordner, "fehlt"), new("fehlt", "boot", 1, summe)));
    }
}
