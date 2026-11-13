using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft das Protokoll-Modul (Linux-Teil) mit Attrappen von journalctl und dmesg. Der Zählfehler
/// („0\n0“ statt 0) fiel am 13.11.2026 am echten Protokoll auf, nicht an einem Test.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class ProtokollModulTests : IDisposable
{
    readonly string _bin = Directory.CreateTempSubdirectory("logtest").FullName;

    public void Dispose() => Directory.Delete(_bin, true);

    void Attrappe(string name, string inhalt)
    {
        var pfad = Path.Combine(_bin, name);
        File.WriteAllText(pfad, "#!/bin/sh\n" + inhalt + "\n");
        File.SetUnixFileMode(pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    (SchrittDefinition Schritt, SchrittErgebnis Ergebnis) Lauf(string journal)
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == "system.protokoll").Schritte.Single(x => x.Os == "linux");
        Attrappe("journalctl", $"cat <<'ENDE'\n{journal}\nENDE");
        Attrappe("dmesg", "exit 0");
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in s.Argumente) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = _bin + ":/usr/bin:/bin";
        psi.Environment["LC_ALL"] = "C";
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = p.ExitCode };
        ModulAusfuehrer.Bewerten(s, res);
        return (s, res);
    }

    [Fact]
    public void Ohne_Fehler_ist_es_in_Ordnung()
    {
        var (_, r) = Lauf("");
        Assert.Equal(Status.Ok, r.Status);
    }

    [Fact]
    public void Datentraegerfehler_sind_ein_Fehler_und_werden_gezaehlt()
    {
        var (_, r) = Lauf("kernel: Buffer I/O error on dev sdc1, logical block 4\nkernel: device offline error, dev sdc, sector 2080\nkernel: ntfs3(sdc1): ntfs_set_state failed, -5.\nkernel: kvm_amd: CPU 8 isn't AMD");
        Assert.Equal(Status.Fehler, r.Status);
        Assert.Contains("Datenträger-Fehler im Protokoll: 2 ", r.Ausgabe);
        Assert.Contains("Dateisystem-Fehler im Protokoll: 1 ", r.Ausgabe);
        Assert.Contains("Sonstige Fehlermeldungen: 1 ", r.Ausgabe);
    }

    [Fact]
    public void Harmlose_Meldungen_bleiben_Info()
    {
        var (_, r) = Lauf("kernel: x86/cpu: SGX disabled or unsupported by BIOS.");
        Assert.Equal(Status.Info, r.Status);
    }

    [Fact]
    public void Hardwarefehler_sind_eine_Warnung()
    {
        var (_, r) = Lauf("kernel: mce: [Hardware Error]: Machine check events logged");
        Assert.Equal(Status.Warnung, r.Status);
    }
}
