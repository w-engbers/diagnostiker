using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft das SMART-Modul mit einer Attrappe von smartctl: Die Shell-Logik des Manifests läuft wirklich,
/// aber ohne echte Datenträger. Der Fehlalarm "WHEN_FAILED" aus der Spaltenüberschrift fiel am 13.11.2026 an echten Platten auf.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public class SmartModulTests : IDisposable
{
    readonly string _ordner = Directory.CreateTempSubdirectory("smarttest").FullName;

    public SmartModulTests()
    {
        // Attrappe: Verhalten je nach Gerätename.
        File.WriteAllText(Path.Combine(_ordner, "smartctl"), """
            #!/bin/sh
            if [ "$1" = "--scan" ]; then
              printf '%s\n' '/dev/sda -d sat # x' '/dev/sdb -d sat # x' '/dev/sdc -d sat # x' '/dev/sdd -d scsi # x' '/dev/nvme0 -d nvme # x'
              exit 0
            fi
            for a in "$@"; do dev=$a; done
            if [ "$1" = "-i" ]; then echo "Device Model:     Testplatte $dev"; exit 0; fi
            HEAD='ID# ATTRIBUTE_NAME          FLAG     VALUE WORST THRESH TYPE      UPDATED  WHEN_FAILED RAW_VALUE'
            case "$dev" in
              /dev/sda) echo 'SMART overall-health self-assessment test result: PASSED'; echo "$HEAD"
                        echo '  5 Reallocated_Sector_Ct   0x0033   100   100   010    Pre-fail  Always       -       0' ;;
              /dev/sdb) echo 'SMART overall-health self-assessment test result: FAILED!'; echo "$HEAD" ;;
              /dev/sdc) echo 'SMART overall-health self-assessment test result: PASSED'; echo "$HEAD"
                        echo '  5 Reallocated_Sector_Ct   0x0033   100   100   010    Pre-fail  Always       -       12' ;;
              /dev/sdd) echo 'Smartctl open device: /dev/sdd failed: Unable to detect device type'; exit 1 ;;
              /dev/nvme0) echo 'SMART overall-health self-assessment test result: PASSED'; echo 'Media and Data Integrity Errors:    3' ;;
            esac
            """.Replace("\r", ""));
        File.WriteAllText(Path.Combine(_ordner, "lsblk"), "#!/bin/sh\necho 100G\n");
        File.SetUnixFileMode(Path.Combine(_ordner, "smartctl"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(Path.Combine(_ordner, "lsblk"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void Dispose() => Directory.Delete(_ordner, true);

    SchrittDefinition Schritt()
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        return module.Single(m => m.Id == "platte.smart-alle").Schritte.Single(s => s.Os == "linux");
    }

    string Ausfuehren(SchrittDefinition s)
    {
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in s.Argumente) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = _ordner + ":/usr/bin:/bin";
        psi.Environment["LC_ALL"] = "C";
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return aus;
    }

    [Fact]
    public void Jede_Platte_bekommt_ihr_eigenes_Urteil()
    {
        var aus = Ausfuehren(Schritt());
        string Zeile(string dev) => aus.Split('\n').Single(z => z.StartsWith(dev + " "));

        Assert.Contains("SMART in Ordnung", Zeile("/dev/sda"));            // trotz "WHEN_FAILED" in der Tabellenüberschrift
        Assert.Contains("AUSFALL", Zeile("/dev/sdb"));
        Assert.Contains("defekte Sektoren", Zeile("/dev/sdc"));
        Assert.Contains("nicht abfragbar", Zeile("/dev/sdd"));
        Assert.Contains("Integritätsfehler", Zeile("/dev/nvme0"));
        Assert.Contains("Testplatte", Zeile("/dev/sda"));                  // Modell erscheint
    }

    [Fact]
    public void Gesamtbewertung_folgt_der_schlimmsten_Platte()
    {
        var s = Schritt();
        var res = new SchrittErgebnis { Ausgabe = Ausfuehren(s), ExitCode = 0 };
        ModulAusfuehrer.Bewerten(s, res);
        Assert.Equal(Status.Fehler, res.Status);
        Assert.Contains(res.Meldungen, m => m.Contains("/dev/sdb") && m.Contains("AUSFALL"));   // Platte steht in der Meldung
    }

    [Theory]
    [InlineData("/dev/sda: SMART in Ordnung\n", Status.Ok)]
    [InlineData("/dev/sdc: Umgelagerte oder defekte Sektoren – beobachten\n", Status.Warnung)]
    [InlineData("/dev/sdd: SMART nicht abfragbar (RAID, USB-Gehäuse oder Stick)\n", Status.Info)]
    [InlineData("KEINE_DATENTRAEGER\n", Status.Warnung)]
    [InlineData("SMARTCTL_FEHLT\n", Status.Uebersprungen)]
    public void Bewertung_je_Zeile(string ausgabe, Status erwartet)
    {
        var s = Schritt();
        var res = new SchrittErgebnis { Ausgabe = ausgabe, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(s, res);
        Assert.Equal(erwartet, res.Status);
    }

    [Fact]
    public void Einzelmodul_meldet_bei_gesunder_ATA_Platte_keinen_Ausfall()
    {
        const string ata = """
            SMART overall-health self-assessment test result: PASSED
            ID# ATTRIBUTE_NAME          FLAG     VALUE WORST THRESH TYPE      UPDATED  WHEN_FAILED RAW_VALUE
              5 Reallocated_Sector_Ct   0x0033   100   100   010    Pre-fail  Always       -       0
            """;
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        foreach (var os in new[] { "linux", "windows" })
        {
            var s = module.Single(m => m.Id == "platte.smart").Schritte.Single(x => x.Os == os);
            var res = new SchrittErgebnis { Ausgabe = ata, ExitCode = 0 };
            ModulAusfuehrer.Bewerten(s, res);
            Assert.Equal(Status.Ok, res.Status);
        }
    }
}
