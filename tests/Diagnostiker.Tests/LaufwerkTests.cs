using System.Text.Json;
using Diagnostiker.StickKern;

namespace Diagnostiker.Tests;

public class LaufwerkTests
{
    const long GiB = 1L << 30;

    // ---- Hilfen: lsblk-JSON wie die echte Ausgabe aufbauen ----
    static Dictionary<string, object?> Teil(string name, long groesse, string? label = null, string? fs = null,
        params string?[] eingehaengt) => new()
    {
        ["name"] = name, ["path"] = "/dev/" + name, ["size"] = groesse, ["type"] = "part",
        ["ro"] = false, ["label"] = label, ["fstype"] = fs, ["mountpoints"] = eingehaengt,
    };

    static Dictionary<string, object?> Platte(string name, long groesse, string? tran, bool rm = false, bool ro = false,
        string? modell = "Testmodell", string type = "disk", string?[]? eingehaengt = null,
        params Dictionary<string, object?>[] teile)
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = name, ["path"] = "/dev/" + name, ["size"] = groesse, ["model"] = modell, ["vendor"] = null,
            ["tran"] = tran, ["rm"] = rm, ["hotplug"] = rm, ["type"] = type, ["ro"] = ro,
            ["mountpoints"] = eingehaengt ?? [null],
        };
        if (teile.Length > 0) d["children"] = teile;
        return d;
    }

    static IReadOnlyList<Laufwerk> Lesen(params Dictionary<string, object?>[] geraete) =>
        LsblkParser.Lesen(JsonSerializer.Serialize(new { blockdevices = geraete }));

    static Pruefung Pruefe(Dictionary<string, object?> g) => Sicherheit.Pruefe(Lesen(g).Single());

    // ---- Der eigene Rechner: nichts davon darf angeboten werden ----
    [Fact]
    public void Systemplatten_werden_nie_angeboten()
    {
        var alle = Lesen(
            Platte("nvme0n1", 953 * GiB, "nvme", teile: [Teil("nvme0n1p1", GiB, null, "vfat", "/boot/efi"), Teil("nvme0n1p2", 950 * GiB, null, "ext4", "/")]),
            Platte("sda", 931 * GiB, "sata", teile: [Teil("sda1", 931 * GiB, "daten", "ext4", "/media/werner/daten")]),
            Platte("sdb", 232 * GiB, "sata", teile: [Teil("sdb1", 232 * GiB, "SECONDARY", "ntfs", "/home/anna/Z")]),
            Platte("sr0", GiB, "sata", rm: true, type: "rom"));

        Assert.Equal(3, alle.Count);   // das optische Laufwerk gehört nicht zu den Laufwerken
        Assert.All(alle, l => Assert.False(Sicherheit.Pruefe(l).Erlaubt, l.Pfad));
    }

    [Fact]
    public void Interne_Platte_wird_auch_mit_USB_Groesse_abgelehnt_wegen_Anschluss()
    {
        var p = Pruefe(Platte("sdc", 16 * GiB, "sata"));
        Assert.False(p.Erlaubt);
        Assert.Contains(p.Gruende, g => g.Contains("Kein USB"));
    }

    // ---- Ein normaler Stick ----
    [Fact]
    public void Leerer_USB_Stick_wird_angeboten()
    {
        var p = Pruefe(Platte("sdc", 16 * GiB, "usb", rm: true));
        Assert.True(p.Erlaubt, string.Join("; ", p.Gruende));
        Assert.Contains(p.Hinweise, h => h.Contains("Keine Partitionen"));
    }

    [Fact]
    public void Stick_mit_Daten_wird_angeboten_aber_gewarnt_und_wird_ausgehaengt()
    {
        var p = Pruefe(Platte("sdc", 16 * GiB, "usb", rm: true, teile:
            [Teil("sdc1", 2 * GiB, "BOOT", "vfat", "/media/werner/BOOT"), Teil("sdc2", 14 * GiB, "DATEN", "exfat", "/media/werner/DATEN")]));
        Assert.True(p.Erlaubt);
        Assert.Contains(p.Hinweise, h => h.Contains("2 Partition") && h.Contains("gelöscht"));
        Assert.Contains(p.Hinweise, h => h.Contains("ausgehängt"));
    }

    // ---- Ablehnungsgründe ----
    [Fact]
    public void Stick_von_dem_das_System_laeuft_wird_abgelehnt()
    {
        var p = Pruefe(Platte("sdc", 16 * GiB, "usb", rm: true, teile: [Teil("sdc2", 2 * GiB, "DIAGBOOT", "vfat", "/run/live/medium")]));
        Assert.False(p.Erlaubt);
        Assert.Contains(p.Gruende, g => g.Contains("/run/live/medium"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/boot")]
    [InlineData("/home/anna")]
    [InlineData("/var/lib/docker")]
    [InlineData("[SWAP]")]
    [InlineData("/media/../etc")]
    public void Laufwerk_mit_Systempfad_wird_abgelehnt(string ort)
    {
        var p = Pruefe(Platte("sdc", 16 * GiB, "usb", rm: true, teile: [Teil("sdc1", 16 * GiB, null, "ext4", ort)]));
        Assert.False(p.Erlaubt);
    }

    [Fact]
    public void Zu_kleine_und_zu_grosse_Laufwerke_werden_abgelehnt()
    {
        Assert.False(Pruefe(Platte("sdc", 2 * GiB, "usb", rm: true)).Erlaubt);
        Assert.True(Pruefe(Platte("sdc", 4 * GiB, "usb", rm: true)).Erlaubt);
        Assert.True(Pruefe(Platte("sdc", 512 * GiB, "usb", rm: true)).Erlaubt);
        Assert.False(Pruefe(Platte("sdc", 2048 * GiB, "usb")).Erlaubt);   // externe Datenplatte
    }

    [Fact]
    public void Schreibgeschuetzter_Stick_wird_abgelehnt()
    {
        Assert.False(Pruefe(Platte("sdc", 16 * GiB, "usb", rm: true, ro: true)).Erlaubt);
    }

    [Theory]
    [InlineData("/dev/sdc; rm -rf /")]
    [InlineData("/dev/../etc/passwd")]
    [InlineData("sdc")]
    [InlineData("/dev/SDC")]
    public void Ungueltige_Geraetenamen_werden_abgelehnt(string pfad)
    {
        var l = new Laufwerk(pfad, "sdc", null, null, 16 * GiB, "usb", true, false, [], []);
        Assert.False(Sicherheit.Pruefe(l).Erlaubt);
    }

    // ---- Ein-Fehler-Schutz: jede einzelne Regel reicht zum Ablehnen ----
    [Fact]
    public void Ein_einziger_Grund_genuegt_zur_Ablehnung_auch_bei_sonst_idealem_Stick()
    {
        var ideal = new Laufwerk("/dev/sdc", "sdc", "Stick", null, 16 * GiB, "usb", true, false, [], []);
        Assert.True(Sicherheit.Pruefe(ideal).Erlaubt);
        Assert.False(Sicherheit.Pruefe(ideal with { Transport = "ata" }).Erlaubt);
        Assert.False(Sicherheit.Pruefe(ideal with { Transport = null }).Erlaubt);
        Assert.False(Sicherheit.Pruefe(ideal with { SchreibGeschuetzt = true }).Erlaubt);
        Assert.False(Sicherheit.Pruefe(ideal with { Groesse = 0 }).Erlaubt);
        Assert.False(Sicherheit.Pruefe(ideal with { Einhaengepunkte = ["/"] }).Erlaubt);
    }

    // ---- Parser ----
    [Fact]
    public void Parser_versteht_aeltere_lsblk_Formen()
    {
        const string alt = """
            {"blockdevices":[{"name":"sdc","path":"/dev/sdc","size":"16000000000","tran":"usb","rm":"1","ro":"0","hotplug":"1","type":"disk","mountpoint":null,
              "children":[{"name":"sdc1","path":"/dev/sdc1","size":"16000000000","type":"part","mountpoint":"/media/x/STICK","fstype":"vfat","label":"STICK"}]}]}
            """;
        var l = LsblkParser.Lesen(alt).Single();
        Assert.Equal("usb", l.Transport);
        Assert.True(l.Wechselmedium);
        Assert.False(l.SchreibGeschuetzt);
        Assert.Equal(16_000_000_000, l.Groesse);
        Assert.Equal("/media/x/STICK", l.Partitionen.Single().Einhaengepunkte.Single());
    }

    [Fact]
    public void Parser_sammelt_verschachtelte_Geraete_wie_verschluesselte_Partitionen()
    {
        var verschluesselt = Teil("sdc1", 15 * GiB, null, "crypto_LUKS");
        verschluesselt["children"] = new[] { new Dictionary<string, object?> { ["name"] = "luks", ["path"] = "/dev/mapper/luks", ["size"] = 15 * GiB, ["type"] = "crypt", ["mountpoints"] = new[] { "/" } } };
        var l = Lesen(Platte("sdc", 16 * GiB, "usb", rm: true, teile: verschluesselt)).Single();
        Assert.Equal(2, l.Partitionen.Count);
        Assert.False(Sicherheit.Pruefe(l).Erlaubt);   // darauf liegt das Wurzelverzeichnis
    }

    [Fact]
    public void Parser_ohne_blockdevices_liefert_leere_Liste()
    {
        Assert.Empty(LsblkParser.Lesen("{}"));
    }

    // ---- Bestätigung ----
    [Fact]
    public void Bestaetigung_verlangt_den_genauen_Namen()
    {
        var l = Lesen(Platte("sdc", 16 * GiB, "usb", rm: true)).Single();
        Assert.Equal("sdc", Bestaetigung.Erwartet(l));
        Assert.True(Bestaetigung.Stimmt("sdc", l));
        Assert.True(Bestaetigung.Stimmt(" sdc ", l));
        Assert.False(Bestaetigung.Stimmt("SDC", l));
        Assert.False(Bestaetigung.Stimmt("ja", l));
        Assert.False(Bestaetigung.Stimmt("", l));
        Assert.False(Bestaetigung.Stimmt(null, l));
        Assert.False(Bestaetigung.Stimmt("/dev/sdc", l));
    }

    [Fact]
    public void Testlauf_ist_im_invarianten_Kulturmodus_wie_das_Live_System()
    {
        // Das Live-System setzt DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1; dort gibt es keine Kultur "de-DE".
        // Das Testprojekt läuft deshalb immer so (runtimeconfig.template.json), damit kulturabhängiger Code auffällt.
        Assert.True(AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant) && invariant);
    }

    [Fact]
    public void Groessenangabe_ist_lesbar()
    {
        Assert.Equal("16 GiB", Sicherheit.Groesse(16 * GiB));
        Assert.Equal("2,5 GiB", Sicherheit.Groesse((long)(2.5 * GiB)));
        Assert.Equal("512 B", Sicherheit.Groesse(512));
    }
}
