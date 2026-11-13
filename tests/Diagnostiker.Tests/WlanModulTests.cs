using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft das WLAN-Diagnosemodul mit Attrappen von nmcli, systemctl, iw und dmesg (die Shell-Logik läuft wirklich).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class WlanModulTests : IDisposable
{
    readonly string _ordner = Directory.CreateTempSubdirectory("wlantest").FullName;
    public void Dispose() => Directory.Delete(_ordner, true);

    void Attrappe(string name, string inhalt)
    {
        var pfad = Path.Combine(_ordner, name);
        File.WriteAllText(pfad, "#!/bin/sh\n" + inhalt);
        File.SetUnixFileMode(pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Richtet die Attrappen ein. <paramref name="zustand"/> ist der Adapterzustand, <paramref name="netze"/> die Scanzeilen.</summary>
    SchrittResultat Lauf(string nm = "active", string zustand = "disconnected", string radio = "enabled", string land = "country DE: DFS-ETSI", string netze = "")
    {
        Attrappe("systemctl", $"echo {nm}\n");
        Attrappe("iw", $"echo '{land}'\n");
        Attrappe("dmesg", "echo '[    5.1] ath9k 0000:03:00.0: enabled'\n");
        Attrappe("nmcli", $$"""
            case "$*" in
              *"DEVICE,TYPE,STATE dev"*) printf 'wlp3s0:wifi:{{zustand}}\nenp2s0:ethernet:unavailable\n' ;;
              *"radio wifi"*) echo {{radio}} ;;
              *"dev wifi list"*) printf '%s' '{{netze}}' ;;
            esac
            """);

        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var schritt = module.Single(m => m.Id == "netz.wlan").Schritte.Single(s => s.Os == "linux");
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in schritt.Argumente) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = _ordner + ":/usr/bin:/bin";
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(schritt, res);
        return new SchrittResultat(aus, res);
    }

    sealed record SchrittResultat(string Ausgabe, SchrittErgebnis Ergebnis)
    {
        public Status Status => Ergebnis.Status;
        public bool Meldet(string text) => Ergebnis.Meldungen.Any(m => m.Contains(text));
    }

    const string Netze = "FritzEngbersOben:9:78:WPA2\nNachbar:1:40:WPA2\n";

    [Fact]
    public void Gesunder_Fall_zeigt_Adapter_Radio_Land_und_Netze()
    {
        var r = Lauf(netze: Netze);
        Assert.Contains("NetworkManager: active", r.Ausgabe);
        Assert.Contains("WLAN-Adapter wlp3s0: Zustand disconnected", r.Ausgabe);
        Assert.Contains("WLAN-Radio: enabled", r.Ausgabe);
        Assert.Contains("Länderkennung: DE", r.Ausgabe);
        Assert.Contains("Gefundene WLAN-Netze: 2", r.Ausgabe);
        Assert.Contains("Netz \"FritzEngbersOben\" Kanal 9 Signal 78% WPA2", r.Ausgabe);
        Assert.Equal(Status.Ok, r.Status);
    }

    [Fact]
    public void Verbundener_Adapter_ist_ok()
    {
        Assert.Equal(Status.Ok, Lauf(zustand: "connected", netze: Netze).Status);
    }

    [Fact]
    public void Scan_ohne_Netze_ist_eine_Warnung()
    {
        var r = Lauf(netze: "");
        Assert.Contains("Gefundene WLAN-Netze: 0", r.Ausgabe);
        Assert.Equal(Status.Warnung, r.Status);
        Assert.True(r.Meldet("kein einziges WLAN"));
    }

    [Fact]
    public void Vom_NetworkManager_nicht_verwalteter_Adapter_ist_ein_Fehler()
    {
        var r = Lauf(zustand: "unmanaged");
        Assert.Equal(Status.Fehler, r.Status);
        Assert.True(r.Meldet("nicht verwaltet"));
    }

    [Fact]
    public void Nicht_laufender_NetworkManager_ist_ein_Fehler()
    {
        var r = Lauf(nm: "inactive", netze: Netze);
        Assert.Equal(Status.Fehler, r.Status);
        Assert.True(r.Meldet("NetworkManager läuft nicht"));
    }

    [Fact]
    public void Ausgeschaltetes_Funkmodul_und_nicht_verfuegbarer_Adapter_sind_Warnungen()
    {
        Assert.Equal(Status.Warnung, Lauf(radio: "disabled", netze: Netze).Status);
        Assert.Equal(Status.Warnung, Lauf(zustand: "unavailable", netze: Netze).Status);
    }

    [Fact]
    public void Fehlende_Laenderkennung_ist_nur_ein_Hinweis()
    {
        var r = Lauf(land: "country 00: DFS-UNSET", netze: Netze);
        Assert.Contains("Länderkennung: 00", r.Ausgabe);
        Assert.True(r.Meldet("keine Länderkennung"));
        Assert.Equal(Status.Ok, r.Status);   // Hinweis (Info) ändert das gute Gesamtbild nicht
    }
}
