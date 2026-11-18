using System.Text.RegularExpressions;
using Diagnostiker.StickKern;
using Diagnostiker.StickKern.Paket;

namespace Diagnostiker.Tests;

public partial class SpracheTests
{
    static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Diagnostiker.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Projektordner nicht gefunden.");
    }

    [GeneratedRegex(@"\{(\d+)\}")] private static partial Regex Platzhalter();
    [GeneratedRegex("""\bT\(\s*"((?:[^"\\]|\\.)*)"(?=[,)])""")] private static partial Regex Aufruf();

    [Fact]
    public void Jeder_Katalogeintrag_hat_Englisch_und_Niederlaendisch_mit_denselben_Platzhaltern()
    {
        foreach (var (de, (en, nl)) in Katalog.Alle)
        {
            Assert.False(string.IsNullOrWhiteSpace(en), "Englisch fehlt: " + de);
            Assert.False(string.IsNullOrWhiteSpace(nl), "Niederländisch fehlt: " + de);
            var soll = Platzhalter().Matches(de).Select(m => m.Value).OrderBy(x => x).ToList();
            Assert.True(soll.SequenceEqual(Platzhalter().Matches(en).Select(m => m.Value).OrderBy(x => x)), "Platzhalter (en) stimmen nicht: " + de);
            Assert.True(soll.SequenceEqual(Platzhalter().Matches(nl).Select(m => m.Value).OrderBy(x => x)), "Platzhalter (nl) stimmen nicht: " + de);
            Assert.NotEqual(de, en);                    // eine nicht übersetzte Zeile würde im Fenster deutsch bleiben
        }
    }

    [Fact]
    public void Jeder_im_Code_benutzte_Text_steht_im_Katalog()
    {
        var gefunden = 0;
        foreach (var ordner in new[] { "src/Diagnostiker.StickKern", "src/Diagnostiker.StickGui" })
            foreach (var datei in Directory.EnumerateFiles(Path.Combine(Wurzel(), ordner), "*.cs", SearchOption.AllDirectories)
                         .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.EndsWith("Katalog.cs") && !f.EndsWith("Sprache.cs")))
                foreach (Match m in Aufruf().Matches(File.ReadAllText(datei)))
                {
                    gefunden++;
                    var text = m.Groups[1].Value;
                    Assert.True(Katalog.Alle.ContainsKey(text), $"Nicht im Katalog ({Path.GetFileName(datei)}): {text}");
                }
        Assert.True(gefunden > 60, "Es wurden auffällig wenige Aufrufe gefunden: " + gefunden);
    }

    [Fact]
    public void Der_Katalog_enthaelt_keine_unbenutzten_Texte()
    {
        var benutzt = new HashSet<string>();
        foreach (var ordner in new[] { "src/Diagnostiker.StickKern", "src/Diagnostiker.StickGui" })
            foreach (var datei in Directory.EnumerateFiles(Path.Combine(Wurzel(), ordner), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/") && !f.Contains("/bin/")))
                foreach (Match m in Aufruf().Matches(File.ReadAllText(datei))) benutzt.Add(m.Groups[1].Value);
        foreach (var t in FortschrittAuswertung.SchrittTexte) benutzt.Add(t);
        foreach (var de in Katalog.Alle.Keys) Assert.True(benutzt.Contains(de), "Im Code nicht mehr benutzt (entfernen): " + de);
    }

    [Fact]
    public void Jeder_Schritt_der_Fortschrittsanzeige_ist_uebersetzt()
    {
        foreach (var t in FortschrittAuswertung.SchrittTexte) Assert.True(Katalog.Alle.ContainsKey(t), "Nicht im Katalog: " + t);
    }

    [Fact]
    public void Meldungen_folgen_der_Sprache()
    {
        var gross = new Laufwerk("/dev/sdx", "sdx", "Test", "X", 2L << 30, "usb", true, false, [], []);
        using (Sprache.Fuer("de")) Assert.Contains("Zu klein (2 GiB, mindestens 4 GiB).", Sicherheit.Pruefe(gross).Gruende);
        using (Sprache.Fuer("en")) Assert.Contains("Too small (2 GiB, at least 4 GiB).", Sicherheit.Pruefe(gross).Gruende);
        using (Sprache.Fuer("nl")) Assert.Contains("Te klein (2 GiB, minimaal 4 GiB).", Sicherheit.Pruefe(gross).Gruende);
    }

    [Fact]
    public void Dezimalzeichen_und_Schrittnamen_folgen_der_Sprache()
    {
        var bytes = (long)(1.5 * (1L << 30));
        using (Sprache.Fuer("de")) Assert.Equal("1,5 GiB", Sicherheit.Groesse(bytes));
        using (Sprache.Fuer("nl")) Assert.Equal("1,5 GiB", Sicherheit.Groesse(bytes));
        using (Sprache.Fuer("en")) Assert.Equal("1.5 GiB", Sicherheit.Groesse(bytes));
        var a = new FortschrittAuswertung(1000);
        a.Zeile("== Abbild schreiben (1 MiB)");
        a.Zeile("500 bytes (500 B) copied, 1.0 s, 21.9 MB/s");
        using (Sprache.Fuer("de")) { Assert.Equal("Abbild auf den Stick schreiben", a.Schritt); Assert.Equal("21,9 MB/s", a.Geschwindigkeit); }
        using (Sprache.Fuer("en")) Assert.Equal("Writing the image to the stick", a.Schritt);
        using (Sprache.Fuer("nl")) Assert.Equal("Image naar de stick schrijven", a.Schritt);
    }

    [Fact]
    public void Fehler_beim_Paketpruefen_sind_uebersetzt()
    {
        using (Sprache.Fuer("en"))
        {
            var e = Assert.Throws<PaketException>(() => Manifest.Lesen("{ nicht gültig"u8.ToArray()));
            Assert.StartsWith("The manifest is not valid JSON", e.Message);
        }
        using (Sprache.Fuer("nl"))
            Assert.Throws<PaketException>(() => PaketQuellen.Aus("/gibt/es/nicht"));
    }

    [Fact]
    public void Die_Wahl_wird_gespeichert_und_gelesen_und_Englisch_ist_der_Standard()
    {
        var datei = Path.Combine(Directory.CreateTempSubdirectory("sprache").FullName, "x", "sprache");
        Assert.Equal("en", Sprache.Gespeichert(datei));
        Sprache.Speichern("nl", datei);
        Assert.Equal("nl", Sprache.Gespeichert(datei));
        File.WriteAllText(datei, "klingonisch");
        Assert.Equal("en", Sprache.Gespeichert(datei));
    }
}
