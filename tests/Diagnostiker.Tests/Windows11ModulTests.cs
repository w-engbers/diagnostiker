using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft den Linux-Teil von module/umstieg/windows11.sh mit nachgebautem /proc und /sys. Der PowerShell-Teil wurde im
/// PowerShell-Container mit nachgebauten CIM-Objekten geprüft (gut, alt, AMD zu alt, schon Windows 11, Celeron unklar).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class Windows11ModulTests : IDisposable
{
    readonly string _ordner = Directory.CreateTempSubdirectory("w11test").FullName;
    string Proc => Path.Combine(_ordner, "proc");
    string Sys => Path.Combine(_ordner, "sys");

    public Windows11ModulTests() { Gut(); }
    public void Dispose() => Directory.Delete(_ordner, true);

    static void Datei(string wurzel, string rel, string inhalt)
    {
        var p = Path.Combine(wurzel, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, inhalt);
    }

    void Cpu(string name, int kerne = 4, string flags = "fpu lm sse2")
    {
        var text = "";
        for (var i = 0; i < kerne; i++) text += $"processor\t: {i}\nmodel name\t: {name}\ncpu MHz\t\t: 2400.000\nflags\t\t: {flags}\n\n";
        Datei(Proc, "cpuinfo", text);
    }

    void Gut()
    {
        Cpu("Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz");
        Datei(Proc, "meminfo", "MemTotal:       8000000 kB\n");
        Datei(Sys, "block/nvme0n1/size", "500000000\n");     // 256 GB
        Datei(Sys, "block/nvme0n1/removable", "0\n");
        Directory.CreateDirectory(Path.Combine(Sys, "firmware/efi/efivars"));
        File.WriteAllBytes(Path.Combine(Sys, "firmware/efi/efivars/SecureBoot-8be4df61-93ca-11d2-aa0d-00e098032b8c"), [6, 0, 0, 0, 1]);
        Datei(Sys, "class/tpm/tpm0/tpm_version_major", "2\n");
    }

    string Lauf()
    {
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(ModulLader.ModulVerzeichnisFinden()!, "umstieg", "windows11.sh"));
        psi.Environment["DIAGNOSTIKER_PROC"] = Proc;
        psi.Environment["DIAGNOSTIKER_SYSFS"] = Sys;
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return aus;
    }

    static Status Bewerten(string aus)
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == "umstieg.windows11").Schritte.Single(x => x.Os == "linux");
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(s, res);
        return res.Status;
    }

    [Fact]
    public void Geeigneter_Rechner_erfuellt_alles()
    {
        var aus = Lauf();
        Assert.Contains("[OK] Prozessor: gehört nach der Faustregel", aus);
        Assert.Contains("[OK] Firmware: UEFI mit eingeschaltetem Secure Boot.", aus);
        Assert.Contains("[OK] TPM: Version 2.0 vorhanden.", aus);
        Assert.Contains("[OK] Gesamteindruck", aus);
        Assert.Equal(Status.Ok, Bewerten(aus));
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i5-6200U CPU @ 2.30GHz", "Intel Core der 6. Generation ist zu alt")]
    [InlineData("Intel(R) Core(TM) i7-7700 CPU @ 3.60GHz", "Intel Core der 7. Generation ist zu alt")]
    [InlineData("AMD Ryzen 5 1600 Six-Core Processor", "AMD Ryzen 1000 ist zu alt")]
    public void Zu_alter_Prozessor_ist_nicht_behebbar(string name, string erwartet)
    {
        Cpu(name);
        var aus = Lauf();
        Assert.Contains(erwartet, aus);
        Assert.Contains("Gesamteindruck: Windows 11 ist hier voraussichtlich nicht", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-1165G7 @ 2.80GHz")]    // 4 Stellen, führende 1: 11. Generation
    [InlineData("Intel(R) Core(TM) i7-1065G7 CPU @ 1.30GHz")]  // 10. Generation
    [InlineData("12th Gen Intel(R) Core(TM) i5-1235U")]
    [InlineData("Intel(R) Core(TM) i5-10400F CPU @ 2.90GHz")]
    [InlineData("AMD Ryzen 5 3600 6-Core Processor")]
    [InlineData("AMD Ryzen AI 7 350 w/ Radeon 860M")]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H")]
    public void Neue_Prozessoren_sind_in_Ordnung(string name)
    {
        Cpu(name);
        Assert.Contains("[OK] Prozessor: gehört nach der Faustregel", Lauf());
    }

    [Fact]
    public void Unklarer_Prozessor_verlangt_eine_Nachpruefung()
    {
        Cpu("Intel(R) Celeron(R) N4000 CPU @ 1.10GHz", kerne: 2);
        var aus = Lauf();
        Assert.Contains("nach der Faustregel nicht beurteilbar", aus);
        Assert.Contains("[WARNUNG] Gesamteindruck: Alle prüfbaren Voraussetzungen sind erfüllt, der Prozessor ließ sich aber nicht beurteilen", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void Legacy_BIOS_ohne_TPM_wenig_Speicher_kleine_Platte()
    {
        Directory.Delete(Path.Combine(Sys, "firmware"), true);
        Directory.Delete(Path.Combine(Sys, "class"), true);
        File.WriteAllText(Path.Combine(Proc, "meminfo"), "MemTotal:       2000000 kB\n");
        File.WriteAllText(Path.Combine(Sys, "block/nvme0n1/size"), "60000000\n");   // 30 GB
        var aus = Lauf();
        Assert.Contains("alten BIOS-Modus", aus);
        Assert.Contains("Kein TPM-Chip erkannt", aus);
        Assert.Contains("Arbeitsspeicher: 1.9 GiB, Windows 11 verlangt mindestens 4 GiB", aus);
        Assert.Contains("Größte Platte: 30 GB", aus);
        Assert.Contains("4 Voraussetzung(en) nicht erfüllt", aus);
    }

    [Fact]
    public void Secure_Boot_aus_und_TPM_1_2_werden_gemeldet()
    {
        File.WriteAllBytes(Path.Combine(Sys, "firmware/efi/efivars/SecureBoot-8be4df61-93ca-11d2-aa0d-00e098032b8c"), [6, 0, 0, 0, 0]);
        File.WriteAllText(Path.Combine(Sys, "class/tpm/tpm0/tpm_version_major"), "1\n");
        var aus = Lauf();
        Assert.Contains("Secure Boot ist ausgeschaltet", aus);
        Assert.Contains("TPM: Version 1.2", aus);
    }
}
