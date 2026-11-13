using Diagnostiker.StickKern;

namespace Diagnostiker.Tests;

public class StickErstellerTests
{
    const long GiB = 1L << 30;

    static Laufwerk Stick(string pfad = "/dev/sdd", long groesse = 58 * GiB, params string[] eingehaengt) => new(
        pfad, pfad[5..], "Stick", "Intenso", groesse, "usb", true, false,
        [new Partition(pfad + "1", null, "ntfs", groesse, eingehaengt)], []);

    sealed class Quelle(params IReadOnlyList<Laufwerk>[] staende) : ILaufwerkQuelle
    {
        int _i;
        public Task<IReadOnlyList<Laufwerk>> LesenAsync(CancellationToken ct) => Task.FromResult(staende[Math.Min(_i++, staende.Length - 1)]);
    }

    sealed class Befehle(Func<string, int>? exitcode = null) : IBefehlsausfuehrer
    {
        public List<(string Programm, string[] Args, IReadOnlyDictionary<string, string>? Umgebung)> Aufrufe { get; } = [];
        public Task<int> AusfuehrenAsync(string programm, IReadOnlyList<string> argumente,
            IReadOnlyDictionary<string, string>? umgebung, Action<string> zeile, CancellationToken ct)
        {
            Aufrufe.Add((programm, argumente.ToArray(), umgebung));
            return Task.FromResult(exitcode?.Invoke(programm) ?? 0);
        }
    }

    static StickErsteller Ersteller(ILaufwerkQuelle q, IBefehlsausfuehrer b) => new(q, b, "/projekt") { Pause = TimeSpan.Zero, Nachpruefungen = 2 };

    static IReadOnlyList<Laufwerk> Mit(params Laufwerk[] l) => l;

    static Laufwerk Fertig(string pfad = "/dev/sdd") => Stick(pfad) with
    {
        Partitionen = [new Partition(pfad + "2", "DIAGBOOT", "vfat", 2 * GiB, []), new Partition(pfad + "3", "DIAGDATEN", "exfat", 50 * GiB, [])],
    };

    [Fact]
    public async Task Falsche_Bestaetigung_fuehrt_nichts_aus()
    {
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit(Stick())), b).ErstellenAsync(Stick(), "ja", _ => { });
        Assert.False(e.Erfolg);
        Assert.Empty(b.Aufrufe);
    }

    [Fact]
    public async Task Verschwundenes_Laufwerk_fuehrt_nichts_aus()
    {
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit()), b).ErstellenAsync(Stick(), "sdd", _ => { });
        Assert.False(e.Erfolg);
        Assert.Empty(b.Aufrufe);
    }

    [Fact]
    public async Task Veraendertes_Laufwerk_fuehrt_nichts_aus()
    {
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit(Stick(groesse: 29 * GiB))), b).ErstellenAsync(Stick(), "sdd", _ => { });
        Assert.False(e.Erfolg);
        Assert.Contains("verändert", e.Meldung);
        Assert.Empty(b.Aufrufe);
    }

    [Fact]
    public async Task Ungeeignetes_Laufwerk_fuehrt_nichts_aus_auch_wenn_Auswahl_und_Eingabe_stimmen()
    {
        var sata = Stick() with { Transport = "sata" };
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit(sata)), b).ErstellenAsync(sata, "sdd", _ => { });
        Assert.False(e.Erfolg);
        Assert.Empty(b.Aufrufe);
    }

    [Fact]
    public async Task Eingehaengte_Partition_wird_zuerst_ausgehaengt_dann_geschrieben()
    {
        var eingehaengt = Stick(eingehaengt: "/media/werner/STICK");
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit(eingehaengt), Mit(Stick()), Mit(Stick()), Mit(Fertig())), b)
            .ErstellenAsync(eingehaengt, "sdd", _ => { });

        Assert.True(e.Erfolg, e.Meldung);
        Assert.Equal(2, b.Aufrufe.Count);
        Assert.Equal("udisksctl", b.Aufrufe[0].Programm);
        Assert.Equal(["unmount", "-b", "/dev/sdd1"], b.Aufrufe[0].Args);
        Assert.Equal("/projekt/stick/schreiben.sh", b.Aufrufe[1].Programm);
        Assert.Equal(["/dev/sdd"], b.Aufrufe[1].Args);
        Assert.Equal("/dev/sdd", b.Aufrufe[1].Umgebung!["BESTAETIGT_LAUFWERK"]);
    }

    [Fact]
    public async Task Nicht_eingehaengtes_Laufwerk_wird_ohne_Aushaengen_geschrieben()
    {
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit(Stick()), Mit(Stick()), Mit(Fertig())), b).ErstellenAsync(Stick(), "sdd", _ => { });
        Assert.True(e.Erfolg, e.Meldung);
        Assert.Single(b.Aufrufe);
        Assert.EndsWith("schreiben.sh", b.Aufrufe[0].Programm);
    }

    [Fact]
    public async Task Misslungenes_Aushaengen_bricht_vor_dem_Schreiben_ab()
    {
        var eingehaengt = Stick(eingehaengt: "/media/werner/STICK");
        var b = new Befehle(p => p == "udisksctl" ? 1 : 0);
        var e = await Ersteller(new Quelle(Mit(eingehaengt)), b).ErstellenAsync(eingehaengt, "sdd", _ => { });
        Assert.False(e.Erfolg);
        Assert.DoesNotContain(b.Aufrufe, a => a.Programm.EndsWith("schreiben.sh"));
    }

    [Fact]
    public async Task Noch_eingehaengt_nach_dem_Aushaengen_bricht_vor_dem_Schreiben_ab()
    {
        var eingehaengt = Stick(eingehaengt: "/media/werner/STICK");
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit(eingehaengt), Mit(eingehaengt)), b).ErstellenAsync(eingehaengt, "sdd", _ => { });
        Assert.False(e.Erfolg);
        Assert.Contains("noch eingehängt", e.Meldung);
        Assert.DoesNotContain(b.Aufrufe, a => a.Programm.EndsWith("schreiben.sh"));
    }

    [Fact]
    public async Task Laufwerk_das_nach_dem_Aushaengen_zum_Systemlaufwerk_wird_bricht_ab()
    {
        var nachher = Stick(eingehaengt: "/");
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit(Stick()), Mit(nachher)), b).ErstellenAsync(Stick(), "sdd", _ => { });
        Assert.False(e.Erfolg);
        Assert.Empty(b.Aufrufe);
    }

    [Fact]
    public async Task Fehlschlag_des_Skripts_wird_gemeldet()
    {
        var b = new Befehle(p => p.EndsWith("schreiben.sh") ? 3 : 0);
        var e = await Ersteller(new Quelle(Mit(Stick())), b).ErstellenAsync(Stick(), "sdd", _ => { });
        Assert.False(e.Erfolg);
        Assert.Contains("fehlgeschlagen", e.Meldung);
    }

    [Fact]
    public async Task Fehlende_Bezeichnungen_nach_dem_Schreiben_sind_ein_Fehler()
    {
        var b = new Befehle();
        var e = await Ersteller(new Quelle(Mit(Stick())), b).ErstellenAsync(Stick(), "sdd", _ => { });
        Assert.False(e.Erfolg);
        Assert.Contains("nicht zu sehen", e.Meldung);
    }

    [Fact]
    public void Projektordner_wird_von_unterhalb_gefunden()
    {
        var wurzel = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(wurzel, "stick"));
            File.WriteAllText(Path.Combine(wurzel, "stick", "schreiben.sh"), "#!/bin/sh");
            var tief = Directory.CreateDirectory(Path.Combine(wurzel, "src", "x", "bin")).FullName;
            Assert.Equal(wurzel, ProjektOrdner.Finden(tief));
            Assert.Null(ProjektOrdner.Finden(Path.GetTempPath() + "gibt-es-nicht"));
        }
        finally { Directory.Delete(wurzel, true); }
    }
}
