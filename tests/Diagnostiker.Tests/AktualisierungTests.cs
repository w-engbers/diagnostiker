using System.Text.Json.Nodes;
using Diagnostiker.StickKern;

namespace Diagnostiker.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class AktualisierungTests : IDisposable
{
    readonly string _wurzel = Directory.CreateTempSubdirectory("aktualisierung").FullName;
    string Stick => Path.Combine(_wurzel, "stick");
    string Neu => Path.Combine(_wurzel, "neu");

    public AktualisierungTests() { Directory.CreateDirectory(Stick); Directory.CreateDirectory(Neu); }
    public void Dispose() => Directory.Delete(_wurzel, true);

    static void Schreibe(string ordner, string rel, string inhalt)
    {
        var pfad = Path.Combine(ordner, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        File.WriteAllText(pfad, inhalt);
    }

    static string StickJson(string basis, string programm, string erstellt = "2026-01-01T00:00:00Z") =>
        $$"""{ "schema": 1, "erstellt": "{{erstellt}}", "teile": { "basis": { "sha256": "{{basis}}" }, "programm": { "version": "{{programm}}" }, "module": { "sha256": "m" }, "werkzeuge": {} } }""";

    void Grundzustand()
    {
        // Stick: alter Stand
        Schreibe(Stick, "stick.json", StickJson("basis-alt", "v1"));
        Schreibe(Stick, "bin/linux-x64/Diagnostiker", "programm-alt");
        Schreibe(Stick, "module/netz/a.json", "gleich");
        Schreibe(Stick, "module/netz/alt.json", "nicht mehr da");
        Schreibe(Stick, "werkzeuge/linux-x64/mein-eigenes-tool", "vom Benutzer");
        Schreibe(Stick, "protokolle/20261113_x/bericht.md", "WICHTIG: Protokoll eines Kunden");
        // Neuer Stand
        Schreibe(Neu, "stick.json", StickJson("basis-neu", "v2", "2026-11-13T00:00:00Z"));
        Schreibe(Neu, "bin/linux-x64/Diagnostiker", "programm-neu");
        Schreibe(Neu, "module/netz/a.json", "gleich");
        Schreibe(Neu, "module/platte/smart-alle.json", "neues Modul");
        Schreibe(Neu, "LIES-MICH.txt", "neu");
        Directory.CreateDirectory(Path.Combine(Neu, "protokolle"));
    }

    [Fact]
    public void Plan_findet_neue_geaenderte_und_entfernte_Dateien()
    {
        Grundzustand();
        var plan = Aktualisierung.Planen(Stick, Neu);
        var nach = plan.Aenderungen.ToDictionary(a => a.Pfad, a => a.Art);

        Assert.Equal(AenderungsArt.Geaendert, nach["bin/linux-x64/Diagnostiker"]);
        Assert.Equal(AenderungsArt.Neu, nach["module/platte/smart-alle.json"]);
        Assert.Equal(AenderungsArt.Neu, nach["LIES-MICH.txt"]);
        Assert.Equal(AenderungsArt.Entfernt, nach["module/netz/alt.json"]);
        Assert.DoesNotContain("module/netz/a.json", nach.Keys);                 // unverändert
        Assert.DoesNotContain("stick.json", nach.Keys);                          // wird gesondert geschrieben
        Assert.DoesNotContain(nach.Keys, k => k.StartsWith("protokolle"));       // nie berührt
        Assert.DoesNotContain("werkzeuge/linux-x64/mein-eigenes-tool", nach.Keys); // eigene Dateien außerhalb bin/module bleiben
        Assert.True(plan.BasisVeraltet);
    }

    [Fact]
    public void Anwenden_uebernimmt_nur_Geaendertes_und_laesst_Protokolle_und_eigene_Dateien_unberuehrt()
    {
        Grundzustand();
        var ergebnis = Aktualisierung.Anwenden(Aktualisierung.Planen(Stick, Neu));

        Assert.True(ergebnis.Erfolg, ergebnis.Meldung);
        Assert.Equal("programm-neu", File.ReadAllText(Path.Combine(Stick, "bin/linux-x64/Diagnostiker")));
        Assert.Equal("neues Modul", File.ReadAllText(Path.Combine(Stick, "module/platte/smart-alle.json")));
        Assert.Equal("neu", File.ReadAllText(Path.Combine(Stick, "LIES-MICH.txt")));
        Assert.False(File.Exists(Path.Combine(Stick, "module/netz/alt.json")));
        Assert.Equal("WICHTIG: Protokoll eines Kunden", File.ReadAllText(Path.Combine(Stick, "protokolle/20261113_x/bericht.md")));
        Assert.Equal("vom Benutzer", File.ReadAllText(Path.Combine(Stick, "werkzeuge/linux-x64/mein-eigenes-tool")));
        Assert.Empty(Directory.EnumerateFiles(Stick, "*.neu", SearchOption.AllDirectories));   // keine Zwischendateien übrig
    }

    [Fact]
    public void Stick_json_bekommt_neues_Programm_aber_behaelt_Basis_und_Erstelldatum()
    {
        Grundzustand();
        Aktualisierung.Anwenden(Aktualisierung.Planen(Stick, Neu));
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Stick, "stick.json")))!;

        Assert.Equal("v2", (string?)j["teile"]!["programm"]!["version"]);
        Assert.Equal("basis-alt", (string?)j["teile"]!["basis"]!["sha256"]);   // die Basis wurde nicht erneuert, darf nicht als neu gelten
        Assert.Equal("2026-01-01T00:00:00Z", (string?)j["erstellt"]);
        Assert.NotNull(j["aktualisiert"]);
    }

    [Fact]
    public void Zweiter_Lauf_ohne_Aenderung_meldet_aktuellen_Stand()
    {
        Grundzustand();
        Aktualisierung.Anwenden(Aktualisierung.Planen(Stick, Neu));
        var plan = Aktualisierung.Planen(Stick, Neu);
        Assert.True(plan.KeineAenderung);
        Assert.Contains("neuesten Stand", Aktualisierung.Anwenden(plan).Meldung);
    }

    [Fact]
    public void Nicht_beschreibbarer_Stick_wird_vor_dem_Kopieren_erkannt()
    {
        Grundzustand();
        var plan = Aktualisierung.Planen(Stick, Neu);
        File.SetUnixFileMode(Stick, UnixFileMode.UserRead | UnixFileMode.UserExecute);   // schreibgeschützt
        try
        {
            if (Environment.IsPrivilegedProcess) return;   // Root darf trotzdem schreiben, der Fall ist dort nicht prüfbar
            var ergebnis = Aktualisierung.Anwenden(plan);
            Assert.False(ergebnis.Erfolg);
            Assert.Contains("nicht beschreibbar", ergebnis.Meldung);
            Assert.Equal("programm-alt", File.ReadAllText(Path.Combine(Stick, "bin/linux-x64/Diagnostiker")));
        }
        finally { File.SetUnixFileMode(Stick, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    [Fact]
    public void Gleiche_Basis_ist_nicht_veraltet()
    {
        Grundzustand();
        Schreibe(Neu, "stick.json", StickJson("basis-alt", "v2"));
        Assert.False(Aktualisierung.Planen(Stick, Neu).BasisVeraltet);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public class StickAktualisiererTests
{
    const long GiB = 1L << 30;

    sealed class Quelle(Func<int, IReadOnlyList<Laufwerk>> staende) : ILaufwerkQuelle
    {
        int _i;
        public Task<IReadOnlyList<Laufwerk>> LesenAsync(CancellationToken ct) => Task.FromResult(staende(_i++));
    }

    sealed class Befehle(Func<string, int>? exit = null) : IBefehlsausfuehrer
    {
        public List<string> Programme { get; } = [];
        public Task<int> AusfuehrenAsync(string programm, IReadOnlyList<string> a, IReadOnlyDictionary<string, string>? u, Action<string> z, CancellationToken ct)
        {
            Programme.Add(Path.GetFileName(programm) + " " + string.Join(' ', a));
            return Task.FromResult(exit?.Invoke(programm) ?? 0);
        }
    }

    static Laufwerk Stick(string? tran = "usb", params string[] eingehaengt) => new("/dev/sdd", "sdd", "Stick", null, 58 * GiB, tran, true, false,
        [new Partition("/dev/sdd2", "DIAGBOOT", "vfat", 2 * GiB, []), new Partition("/dev/sdd3", "DIAGDATEN", "exfat", 55 * GiB, eingehaengt)], []);

    static StickAktualisierer Aktualisierer(ILaufwerkQuelle q, IBefehlsausfuehrer b, string proj) =>
        new(q, b, proj) { ErlaubteOrte = [Path.GetTempPath()] };

    string ProjektMitStick(bool stickJson, out string punkt)
    {
        var proj = Directory.CreateTempSubdirectory("proj").FullName;
        punkt = Path.Combine(proj, "mnt");
        Directory.CreateDirectory(punkt);
        Directory.CreateDirectory(Path.Combine(proj, "build", "stick-staging"));
        File.WriteAllText(Path.Combine(proj, "build", "stick-staging", "stick.json"), """{"schema":1,"teile":{"basis":{"sha256":"x"}}}""");
        if (stickJson) File.WriteAllText(Path.Combine(punkt, "stick.json"), """{"schema":1,"teile":{"basis":{"sha256":"x"}}}""");
        return proj;
    }

    [Fact]
    public async Task Findet_den_Stick_haengt_bei_Bedarf_ein_und_plant()
    {
        var proj = ProjektMitStick(true, out var punkt);
        try
        {
            var b = new Befehle();
            var q = new Quelle(i => [i == 0 ? Stick() : Stick("usb", punkt)]);   // zuerst nicht eingehängt, danach eingehängt
            var v = await Aktualisierer(q, b, proj).VorbereitenAsync(_ => { });

            Assert.Null(v.Fehler);
            Assert.NotNull(v.Plan);
            Assert.Equal(punkt, v.Plan!.StickOrdner);
            Assert.Contains(b.Programme, p => p.StartsWith("staging.sh"));
            Assert.Contains("udisksctl mount -b /dev/sdd3", b.Programme);
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public async Task Bereits_eingehaengter_Stick_wird_nicht_erneut_eingehaengt()
    {
        var proj = ProjektMitStick(true, out var punkt);
        try
        {
            var b = new Befehle();
            var v = await Aktualisierer(new Quelle(_ => [Stick("usb", punkt)]), b, proj).VorbereitenAsync(_ => { });
            Assert.NotNull(v.Plan);
            Assert.DoesNotContain(b.Programme, p => p.StartsWith("udisksctl"));
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public async Task Schreibgeschuetzt_eingehaengter_Stick_wird_neu_eingehaengt()
    {
        var proj = ProjektMitStick(true, out var rw);
        var ro = Directory.CreateTempSubdirectory("ro").FullName;
        File.WriteAllText(Path.Combine(ro, "stick.json"), """{"schema":1}""");
        File.SetUnixFileMode(ro, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            if (Environment.IsPrivilegedProcess) return;   // Root kann auch in "schreibgeschützte" Ordner schreiben
            var b = new Befehle();
            var q = new Quelle(i => [i == 0 ? Stick("usb", ro) : Stick("usb", rw)]);   // nach dem Neueinhängen: beschreibbar
            var v = await Aktualisierer(q, b, proj).VorbereitenAsync(_ => { });

            Assert.Null(v.Fehler);
            Assert.Equal(rw, v.Plan!.StickOrdner);
            var kommandos = b.Programme.Where(p => p.StartsWith("udisksctl")).ToList();
            Assert.Equal(["udisksctl unmount -b /dev/sdd3", "udisksctl mount -b /dev/sdd3"], kommandos);
        }
        finally
        {
            File.SetUnixFileMode(ro, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(ro, true);
            Directory.Delete(proj, true);
        }
    }

    [Fact]
    public async Task Bleibt_der_Stick_auch_nach_dem_Neueinhaengen_schreibgeschuetzt_gibt_es_eine_klare_Meldung()
    {
        var proj = ProjektMitStick(true, out _);
        var ro = Directory.CreateTempSubdirectory("ro").FullName;
        File.WriteAllText(Path.Combine(ro, "stick.json"), """{"schema":1}""");
        File.SetUnixFileMode(ro, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            if (Environment.IsPrivilegedProcess) return;
            var v = await Aktualisierer(new Quelle(_ => [Stick("usb", ro)]), new Befehle(), proj).VorbereitenAsync(_ => { });
            Assert.Null(v.Plan);
            Assert.Contains("nicht beschreibbar einhängen", v.Fehler);
        }
        finally
        {
            File.SetUnixFileMode(ro, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(ro, true);
            Directory.Delete(proj, true);
        }
    }

    [Fact]
    public async Task Interne_Platte_mit_Bezeichnung_DIAGDATEN_wird_nicht_angefasst()
    {
        var proj = ProjektMitStick(true, out var punkt);
        try
        {
            var v = await Aktualisierer(new Quelle(_ => [Stick("sata", punkt)]), new Befehle(), proj).VorbereitenAsync(_ => { });
            Assert.Null(v.Plan);
            Assert.Contains("nicht zulässig", v.Fehler);
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public async Task Ohne_stick_json_wird_abgelehnt()
    {
        var proj = ProjektMitStick(false, out var punkt);
        try
        {
            var v = await Aktualisierer(new Quelle(_ => [Stick("usb", punkt)]), new Befehle(), proj).VorbereitenAsync(_ => { });
            Assert.Null(v.Plan);
            Assert.Contains("kein Diagnostiker-Stick", v.Fehler);
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public async Task Fehlender_Stick_und_misslungenes_Staging_werden_gemeldet()
    {
        var proj = ProjektMitStick(true, out _);
        try
        {
            var keiner = await Aktualisierer(new Quelle(_ => []), new Befehle(), proj).VorbereitenAsync(_ => { });
            Assert.Contains("Kein Diagnostiker-Stick", keiner.Fehler);

            var b = new Befehle(p => p.EndsWith("staging.sh") ? 1 : 0);
            var staging = await Aktualisierer(new Quelle(_ => []), b, proj).VorbereitenAsync(_ => { });
            Assert.Contains("zusammenstellen", staging.Fehler);
        }
        finally { Directory.Delete(proj, true); }
    }
}
