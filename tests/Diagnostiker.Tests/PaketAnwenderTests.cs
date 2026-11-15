using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using Diagnostiker.StickKern;
using Diagnostiker.StickKern.Paket;

namespace Diagnostiker.Tests;

/// <summary>Anwenderseite: Paket holen und prüfen, Stick aus dem Paket schreiben (mit Attrappen für Laufwerke, Befehle und Netz).</summary>
public sealed class PaketAnwenderTests : IDisposable
{
    const long GiB = 1L << 30;
    readonly string _wurzel = Directory.CreateTempSubdirectory("paketanwender").FullName;
    public void Dispose() => Directory.Delete(_wurzel, true);

    // ---------- Paket bauen (mit dem echten Bauer, kleines Abbild) ----------
    (string Ordner, string Pub, Manifest M) Paket(string version = "2026.11.15", string? folge = null, string? privAus = null)
    {
        var (priv, pub) = Signatur.SchluesselErzeugen();
        if (privAus is not null) priv = privAus;
        var abbild = Path.Combine(_wurzel, "boot.img"); File.WriteAllBytes(abbild, Enumerable.Range(0, 100_000).Select(i => (byte)(i * 7)).ToArray());
        var daten = Path.Combine(_wurzel, "daten-" + version); Directory.CreateDirectory(Path.Combine(daten, "module", "x"));
        File.WriteAllText(Path.Combine(daten, "stick.json"), "{}"); File.WriteAllText(Path.Combine(daten, ".versteckt"), "x"); File.WriteAllText(Path.Combine(daten, "module", "x", "a.json"), "{}");
        Directory.CreateDirectory(Path.Combine(daten, "protokolle"));
        var aus = Path.Combine(_wurzel, "paket-" + version);
        var m = PaketBauer.Bauen(abbild, daten, aus, version, 2560, priv, folge);
        return (aus, pub, m);
    }

    PaketHolen Holer(string pub, VertrauensSpeicher? speicher = null) => new(new Vertrauen([pub]), speicher, Path.Combine(_wurzel, "cache"));

    // ---------- Holen und Prüfen ----------
    [Fact]
    public async Task Paket_aus_Ordner_wird_geholt_und_geprueft()
    {
        var (ordner, pub, m) = Paket();
        var g = await Holer(pub).HolenAsync(new OrdnerQuelle(ordner), _ => { }, null, default);
        Assert.Equal("2026.11.15", g.Manifest.Version);
        Assert.True(File.Exists(g.BootDatei)); Assert.True(File.Exists(g.DatenDatei));
        Assert.Equal(m.Boot.Sha256, PaketPruefer.Sha256(g.BootDatei));
    }

    [Fact]
    public async Task Falsch_signiertes_Paket_wird_abgelehnt_und_nichts_gespeichert()
    {
        var (ordner, _, _) = Paket();
        var (_, fremd) = Signatur.SchluesselErzeugen();
        var e = await Assert.ThrowsAsync<PaketException>(() => Holer(fremd).HolenAsync(new OrdnerQuelle(ordner), _ => { }, null, default));
        Assert.Contains("Signatur", e.Message);
        Assert.False(Directory.Exists(Path.Combine(_wurzel, "cache", "2026.11.15")));
    }

    [Fact]
    public async Task Verfaelschte_Datei_wird_erkannt_und_geloescht()
    {
        var (ordner, pub, _) = Paket();
        var boot = Path.Combine(ordner, "boot.img.gz");
        var b = File.ReadAllBytes(boot); b[b.Length / 2] ^= 0xFF; File.WriteAllBytes(boot, b);
        var e = await Assert.ThrowsAsync<PaketException>(() => Holer(pub).HolenAsync(new OrdnerQuelle(ordner), _ => { }, null, default));
        Assert.Contains("Prüfsumme", e.Message);
        Assert.False(File.Exists(Path.Combine(_wurzel, "cache", "2026.11.15", "boot.img.gz")));
        Assert.False(File.Exists(Path.Combine(_wurzel, "cache", "2026.11.15", "boot.img.gz.teil")));
    }

    [Fact]
    public async Task Verfaelschtes_Manifest_wird_abgelehnt()
    {
        var (ordner, pub, _) = Paket();
        var mf = Path.Combine(ordner, "manifest.json");
        File.WriteAllText(mf, File.ReadAllText(mf).Replace("2026.11.15", "2026.11.99"));
        await Assert.ThrowsAsync<PaketException>(() => Holer(pub).HolenAsync(new OrdnerQuelle(ordner), _ => { }, null, default));
    }

    [Fact]
    public async Task Vorhandene_geprueft_richtige_Dateien_werden_nicht_erneut_geladen()
    {
        var (ordner, pub, _) = Paket();
        var h = Holer(pub);
        await h.HolenAsync(new OrdnerQuelle(ordner), _ => { }, null, default);
        var meldungen = new List<string>();
        await h.HolenAsync(new OrdnerQuelle(ordner), meldungen.Add, null, default);
        Assert.Equal(2, meldungen.Count(z => z.Contains("bereits vorhanden")));
    }

    [Fact]
    public async Task Alte_Versionen_im_Zwischenspeicher_werden_entfernt()
    {
        var (o1, pub, _) = Paket("2026.11.1");
        var h = Holer(pub);
        await h.HolenAsync(new OrdnerQuelle(o1), _ => { }, null, default);
        // neues Paket mit demselben Schlüssel: Schlüssel gleich halten
        var (priv, pub2) = Signatur.SchluesselErzeugen();
        var abbild = Path.Combine(_wurzel, "boot.img"); var d = Path.Combine(_wurzel, "daten-2026.11.1");
        PaketBauer.Bauen(abbild, d, Path.Combine(_wurzel, "paket-neu"), "2026.11.2", 2560, priv);
        var h2 = new PaketHolen(new Vertrauen([pub2]), null, Path.Combine(_wurzel, "cache"));
        await h2.HolenAsync(new OrdnerQuelle(Path.Combine(_wurzel, "paket-neu")), _ => { }, null, default);
        Assert.Equal(new[] { "2026.11.2" }, Directory.GetDirectories(Path.Combine(_wurzel, "cache")).Select(o => Path.GetFileName(o)!).ToArray());
    }

    [Fact]
    public async Task Neuer_Schluessel_aus_gueltigem_Manifest_wird_gespeichert()
    {
        var (altPriv, altPub) = Signatur.SchluesselErzeugen();
        var (_, neuPub) = Signatur.SchluesselErzeugen();
        var (ordner, _, _) = Paket(privAus: altPriv, folge: neuPub);
        var datei = Path.Combine(_wurzel, "konfig", "vertrauen.txt");
        var s = new VertrauensSpeicher(datei);
        await Holer(altPub, s).HolenAsync(new OrdnerQuelle(ordner), _ => { }, null, default);
        Assert.Equal(new[] { neuPub }, s.Lesen().ToArray());
        Assert.Contains(neuPub, s.Laden().Schluessel); // und der eingebaute Schlüssel bleibt
        Assert.Contains(EingebauterSchluessel.Lesen(), s.Laden().Schluessel);
    }

    // ---------- Webquelle ----------
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Anfragen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Anfragen.Add(r); return Task.FromResult(f(r)); }
    }

    [Fact]
    public async Task Webquelle_laedt_neueste_Version_und_legt_dann_auf_diese_fest()
    {
        var (ordner, pub, m) = Paket();
        var handler = new Handler(r =>
        {
            var name = r.RequestUri!.Segments[^1];
            var datei = Path.Combine(ordner, name);
            return File.Exists(datei) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(datei)) } : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var q = HttpQuelle.GitHub("w-engbers/diagnostiker", new HttpClient(handler));
        var g = await Holer(pub).HolenAsync(q, _ => { }, null, default);
        Assert.Equal("2026.11.15", g.Manifest.Version);
        var urls = handler.Anfragen.Select(a => a.RequestUri!.ToString()).ToList();
        Assert.Equal("https://github.com/w-engbers/diagnostiker/releases/latest/download/manifest.json", urls[0]);
        Assert.Contains("https://github.com/w-engbers/diagnostiker/releases/download/v2026.11.15/boot.img.gz", urls);
        Assert.All(handler.Anfragen, a => Assert.Equal("Diagnostiker-Stick-Manager", a.Headers.UserAgent.ToString()));
    }

    [Fact]
    public void Webquelle_verlangt_HTTPS() =>
        Assert.Throws<PaketException>(() => new HttpQuelle("http://example.org/x"));

    [Fact]
    public async Task Webquelle_setzt_unterbrochenen_Download_mit_Range_fort()
    {
        var inhalt = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        string? range = null;
        var handler = new Handler(r =>
        {
            range = r.Headers.Range?.ToString();
            var ab = (int)(r.Headers.Range?.Ranges.First().From ?? 0);
            return new HttpResponseMessage(ab > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(inhalt[ab..]) };
        });
        var ziel = Path.Combine(_wurzel, "x.teil");
        File.WriteAllBytes(ziel, inhalt[..400]);
        await new HttpQuelle("https://example.org/p", new HttpClient(handler)).HerunterladenAsync("x", ziel, 1000, null, default);
        Assert.Equal("bytes=400-", range);
        Assert.Equal(inhalt, File.ReadAllBytes(ziel));
    }

    [Fact]
    public async Task Webquelle_bricht_ab_wenn_mehr_kommt_als_das_Manifest_sagt()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[5000]) });
        await Assert.ThrowsAsync<PaketException>(() =>
            new HttpQuelle("https://example.org/p", new HttpClient(handler)).HerunterladenAsync("x", Path.Combine(_wurzel, "y.teil"), 1000, null, default));
    }

    // ---------- Entpacken ----------
    [Fact]
    public void Entpacken_legt_Dateien_und_leere_Ordner_an()
    {
        var (ordner, _, _) = Paket();
        var ziel = Path.Combine(_wurzel, "stick"); Directory.CreateDirectory(ziel);
        var n = PaketStickErsteller.Entpacken(Path.Combine(ordner, "daten.tar.gz"), ziel);
        Assert.Equal(3, n);
        Assert.True(File.Exists(Path.Combine(ziel, "stick.json")));
        Assert.True(File.Exists(Path.Combine(ziel, "module", "x", "a.json")));
        Assert.True(File.Exists(Path.Combine(ziel, ".versteckt")));
        Assert.True(Directory.Exists(Path.Combine(ziel, "protokolle")));
    }

    static string TarMit(string root, Action<TarWriter> f)
    {
        var d = Path.Combine(root, Guid.NewGuid() + ".tar.gz");
        using var fs = File.Create(d); using var gz = new GZipStream(fs, CompressionLevel.Fastest); using var tw = new TarWriter(gz);
        f(tw); return d;
    }

    [Fact]
    public void Entpacken_lehnt_Pfade_ausserhalb_und_Verweise_ab()
    {
        var ziel = Path.Combine(_wurzel, "stick2"); Directory.CreateDirectory(ziel);
        var boese = TarMit(_wurzel, tw => tw.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "../ausserhalb.txt") { DataStream = new MemoryStream("x"u8.ToArray()) }));
        Assert.Throws<PaketException>(() => PaketStickErsteller.Entpacken(boese, ziel));
        Assert.False(File.Exists(Path.Combine(_wurzel, "ausserhalb.txt")));
        var link = TarMit(_wurzel, tw => tw.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "l") { LinkName = "/etc/passwd" }));
        Assert.Throws<PaketException>(() => PaketStickErsteller.Entpacken(link, ziel));
    }

    // ---------- Stick schreiben ----------
    static Laufwerk Stick(string pfad = "/dev/sdd", long groesse = 58 * GiB, params string[] eingehaengt) => new(
        pfad, pfad[5..], "Stick", "Intenso", groesse, "usb", true, false,
        [new Partition(pfad + "1", null, "ntfs", groesse, eingehaengt)], []);
    static Laufwerk Fertig(string pfad = "/dev/sdd", string? ort = null) => Stick(pfad) with
    {
        Partitionen = [new Partition(pfad + "2", "DIAGBOOT", "vfat", 2 * GiB, []), new Partition(pfad + "3", "DIAGDATEN", "exfat", 50 * GiB, ort is null ? [] : [ort])],
    };

    sealed class Quelle(params IReadOnlyList<Laufwerk>[] staende) : ILaufwerkQuelle
    {
        int _i;
        public Task<IReadOnlyList<Laufwerk>> LesenAsync(CancellationToken ct) => Task.FromResult(staende[Math.Min(_i++, staende.Length - 1)]);
    }

    sealed class Befehle(string stickOrdner, Func<string, string[], int?>? exit = null) : IBefehlsausfuehrer
    {
        public List<(string Programm, string[] Args)> Aufrufe { get; } = [];
        public Task<int> AusfuehrenAsync(string programm, IReadOnlyList<string> argumente, IReadOnlyDictionary<string, string>? umgebung, Action<string> zeile, CancellationToken ct)
        {
            var a = argumente.ToArray(); Aufrufe.Add((programm, a));
            var code = exit?.Invoke(programm, a);
            if (code is not null) return Task.FromResult(code.Value);
            if (programm is "pkexec" or "/bin/bash") { zeile("== Laufwerk wird beschrieben"); zeile("SCHREIBEN_FERTIG /dev/sdd3"); }
            if (programm == "udisksctl" && a[0] == "mount") zeile($"Mounted /dev/sdd3 at {stickOrdner}.");
            return Task.FromResult(0);
        }
    }

    PaketStickErsteller Ersteller(ILaufwerkQuelle q, IBefehlsausfuehrer b) => new(q, b) { Pause = TimeSpan.Zero, Nachpruefungen = 2, BinRoot = false };

    async Task<(GeholtesPaket Paket, string StickOrdner)> Vorbereiten()
    {
        var (ordner, pub, _) = Paket();
        var g = await Holer(pub).HolenAsync(new OrdnerQuelle(ordner), _ => { }, null, default);
        var stick = Path.Combine(_wurzel, "stickordner"); Directory.CreateDirectory(stick);
        return (g, stick);
    }

    [Fact]
    public async Task Stick_wird_geschrieben_gefuellt_und_ausgehaengt()
    {
        var (g, stick) = await Vorbereiten();
        var b = new Befehle(stick);
        var e = await Ersteller(new Quelle(Mit(Stick()), Mit(Stick()), Mit(Stick()), Mit(Fertig())), b).ErstellenAsync(Stick(), "sdd", g, _ => { });
        Assert.True(e.Erfolg, e.Meldung);
        Assert.Contains("2026.11.15", e.Meldung);
        Assert.True(File.Exists(Path.Combine(stick, "stick.json")));
        // Reihenfolge: Helfer über pkexec, dann einhängen, dann aushängen
        var progs = b.Aufrufe.Select(a => a.Programm + " " + (a.Args.FirstOrDefault() ?? "")).ToList();
        var iHelfer = progs.FindIndex(p => p.StartsWith("pkexec")); var iMount = b.Aufrufe.FindIndex(a => a.Programm == "udisksctl" && a.Args[0] == "mount");
        var iUnmount = b.Aufrufe.FindLastIndex(a => a.Programm == "udisksctl" && a.Args[0] == "unmount");
        Assert.True(iHelfer >= 0 && iHelfer < iMount && iMount < iUnmount);
        // Der Helfer bekommt Skript, Laufwerk, Abbild, Prüfsumme und Größe als Argumente (nicht in der Umgebung)
        var h = b.Aufrufe[iHelfer].Args;
        Assert.Equal("/bin/bash", h[0]); Assert.Equal("-c", h[1]); Assert.Contains("SCHREIBEN_FERTIG", h[2]);
        Assert.Equal(new[] { "/dev/sdd", g.BootDatei, g.Manifest.Boot.Sha256, g.Manifest.Boot.EntpacktGroesse.ToString()! }, h[4..]);
    }

    static IReadOnlyList<Laufwerk> Mit(params Laufwerk[] l) => l;

    [Fact]
    public async Task Ohne_Bestaetigung_oder_bei_ungeeignetem_Laufwerk_wird_kein_Helfer_gestartet()
    {
        var (g, stick) = await Vorbereiten();
        var b = new Befehle(stick);
        var e1 = await Ersteller(new Quelle(Mit(Stick())), b).ErstellenAsync(Stick(), "falsch", g, _ => { });
        var sata = Stick() with { Transport = "sata" };
        var e2 = await Ersteller(new Quelle(Mit(sata)), b).ErstellenAsync(sata, "sdd", g, _ => { });
        var system = Stick(eingehaengt: "/") ;
        var e3 = await Ersteller(new Quelle(Mit(system)), b).ErstellenAsync(system, "sdd", g, _ => { });
        Assert.False(e1.Erfolg); Assert.False(e2.Erfolg); Assert.False(e3.Erfolg);
        Assert.DoesNotContain(b.Aufrufe, a => a.Programm is "pkexec" or "/bin/bash");
    }

    [Fact]
    public async Task Verfaelschtes_Paket_im_Zwischenspeicher_wird_vor_dem_Schreiben_erkannt()
    {
        var (g, stick) = await Vorbereiten();
        var b = File.ReadAllBytes(g.BootDatei); b[10] ^= 1; File.WriteAllBytes(g.BootDatei, b);
        var bef = new Befehle(stick);
        var e = await Ersteller(new Quelle(Mit(Stick())), bef).ErstellenAsync(Stick(), "sdd", g, _ => { });
        Assert.False(e.Erfolg); Assert.Contains("Prüfsumme", e.Meldung);
        Assert.DoesNotContain(bef.Aufrufe, a => a.Programm == "pkexec");
    }

    [Theory]
    [InlineData(126, "abgebrochen")]
    [InlineData(127, "Administratorrechte")]
    [InlineData(3, "abgelehnt")]
    [InlineData(4, "fehlgeschlagen")]
    public async Task Fehler_des_Helfers_werden_verstaendlich_gemeldet_und_nichts_wird_gefuellt(int code, string text)
    {
        var (g, stick) = await Vorbereiten();
        var bef = new Befehle(stick, (p, a) => p == "pkexec" ? code : null);
        var e = await Ersteller(new Quelle(Mit(Stick())), bef).ErstellenAsync(Stick(), "sdd", g, _ => { });
        Assert.False(e.Erfolg); Assert.Contains(text, e.Meldung);
        Assert.DoesNotContain(bef.Aufrufe, a => a.Programm == "udisksctl" && a.Args[0] == "mount");
        Assert.False(File.Exists(Path.Combine(stick, "stick.json")));
    }

    [Fact]
    public async Task Hat_der_Desktop_die_Partition_schon_eingehaengt_wird_sie_genutzt()
    {
        var (g, stick) = await Vorbereiten();
        var bef = new Befehle(stick);
        var q = new Quelle(Mit(Stick()), Mit(Stick()), Mit(Fertig(ort: stick)));
        var e = await Ersteller(q, bef).ErstellenAsync(Stick(), "sdd", g, _ => { });
        Assert.True(e.Erfolg, e.Meldung);
        Assert.DoesNotContain(bef.Aufrufe, a => a.Programm == "udisksctl" && a.Args[0] == "mount");
        Assert.True(File.Exists(Path.Combine(stick, "stick.json")));
    }

    [Fact]
    public async Task Als_root_wird_das_Skript_direkt_ohne_pkexec_gestartet()
    {
        var (g, stick) = await Vorbereiten();
        var bef = new Befehle(stick);
        var e = await new PaketStickErsteller(new Quelle(Mit(Stick()), Mit(Stick()), Mit(Stick()), Mit(Fertig())), bef) { Pause = TimeSpan.Zero, Nachpruefungen = 2, BinRoot = true }
            .ErstellenAsync(Stick(), "sdd", g, _ => { });
        Assert.True(e.Erfolg, e.Meldung);
        Assert.DoesNotContain(bef.Aufrufe, a => a.Programm == "pkexec");
        Assert.Contains(bef.Aufrufe, a => a.Programm == "/bin/bash" && a.Args[0] == "-c");
    }

    [Fact]
    public void Das_eingebettete_Skript_ist_das_im_Repo()
    {
        var s = PaketStickErsteller.HelferSkript();
        Assert.StartsWith("#!/bin/bash", s);
        Assert.Contains("VERWEIGERT", s); Assert.Contains("mkfs.exfat", s);
    }
}
