using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft das Modul „Verschlüsselung“: die Regeln für die Windows-Ausgabe (mit festen Beispielzeilen, das PowerShell-Skript selbst
/// wurde mit nachgebauten CIM-Objekten im PowerShell-Container geprüft) und den Linux-Teil mit einer Attrappe von lsblk.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class VerschluesselungModulTests : IDisposable
{
    readonly string _bin = Directory.CreateTempSubdirectory("verschltest").FullName;
    public void Dispose() => Directory.Delete(_bin, true);

    static SchrittDefinition Schritt(string os)
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        return module.Single(m => m.Id == "platte.verschluesselung").Schritte.Single(s => s.Os == os);
    }

    static Status Bewerten(SchrittDefinition s, string aus)
    {
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(s, res);
        return res.Status;
    }

    [Theory]
    [InlineData("Laufwerk C: verschlüsselt, Schutz aktiv (Methode XTS-AES 128).", Status.Ok)]
    [InlineData("Laufwerk C: verschlüsselt, Schutz ausgesetzt (Methode AES 128): Windows hat BitLocker vorübergehend angehalten.", Status.Warnung)]
    [InlineData("Laufwerk D: verschlüsselt und gesperrt (Methode XTS-AES 128): ohne Schlüssel nicht lesbar.", Status.Warnung)]
    [InlineData("Laufwerk C: nicht verschlüsselt.", Status.Info)]
    [InlineData("Laufwerk C: wird gerade verschlüsselt (42 %).", Status.Info)]
    [InlineData("Laufwerk C: Verschlüsselung angehalten (42 %).", Status.Warnung)]
    [InlineData("BITLOCKER_NICHT_ABFRAGBAR", Status.Info)]
    public void Windows_Ausgabe_wird_richtig_bewertet(string zeile, Status erwartet) =>
        Assert.Equal(erwartet, Bewerten(Schritt("windows"), zeile));

    [Fact]
    public void Fehlendes_Wiederherstellungskennwort_macht_den_Befund_zur_Warnung()
    {
        var aus = "Laufwerk C: verschlüsselt, Schutz aktiv (Methode XTS-AES 128).\nLaufwerk C: kein Wiederherstellungskennwort vorhanden: Bei Problemen sind die Daten ohne Schlüssel verloren.";
        Assert.Equal(Status.Warnung, Bewerten(Schritt("windows"), aus));
    }

    string LinuxLauf(string lsblkAusgabe)
    {
        var pfad = Path.Combine(_bin, "lsblk");
        File.WriteAllText(pfad, $"#!/bin/sh\ncat <<'ENDE'\n{lsblkAusgabe}\nENDE\n");
        File.SetUnixFileMode(pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var s = Schritt("linux");
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in s.Argumente) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = _bin + ":/usr/bin:/bin";
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return aus;
    }

    [Fact]
    public void Linux_erkennt_BitLocker_und_LUKS_Partitionen()
    {
        var aus = LinuxLauf("/dev/nvme0n1 disk 953.9G disk\n/dev/nvme0n1p3 BitLocker 900G part\n/dev/sdb1 crypto_LUKS 100G part\n/dev/sdc1 ext4 50G part");
        Assert.Contains("/dev/nvme0n1p3 (900G): mit BitLocker verschlüsselt", aus);
        Assert.Contains("/dev/sdb1 (100G): mit LUKS verschlüsselt", aus);
        Assert.DoesNotContain("/dev/sdc1", aus);
        Assert.Contains("Wiederherstellungsschlüssel", aus);
        Assert.Equal(Status.Info, Bewerten(Schritt("linux"), aus));
    }

    [Fact]
    public void Linux_ohne_verschluesselte_Partitionen_meldet_das()
    {
        var aus = LinuxLauf("/dev/sda1 ext4 50G part\n/dev/sda2 ntfs 50G part");
        Assert.Contains("Keine verschlüsselten Partitionen", aus);
        Assert.Equal(Status.Info, Bewerten(Schritt("linux"), aus));
    }
}
