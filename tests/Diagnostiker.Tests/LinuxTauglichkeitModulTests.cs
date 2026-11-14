using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft module/umstieg/hardware.sh mit nachgebautem /proc, /sys und einer Attrappe von lspci.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxTauglichkeitModulTests : IDisposable
{
    readonly string _ordner = Directory.CreateTempSubdirectory("hwtest").FullName;
    string Bin => Path.Combine(_ordner, "bin");
    string Proc => Path.Combine(_ordner, "proc");
    string Sys => Path.Combine(_ordner, "sys");
    string _lspci = "";
    string _firmware = "";

    public LinuxTauglichkeitModulTests()
    {
        Directory.CreateDirectory(Bin); Directory.CreateDirectory(Proc); Directory.CreateDirectory(Sys);
        Datei(Proc, "cpuinfo", "processor\t: 0\nmodel name\t: Testprozessor\nflags\t\t: fpu lm sse2\n\nprocessor\t: 1\nmodel name\t: Testprozessor\nflags\t\t: fpu lm sse2\n");
        Datei(Proc, "meminfo", "MemTotal:       16000000 kB\n");
        Datei(Proc, "asound/cards", " 0 [PCH            ]: HDA-Intel - HDA Intel PCH\n");
        Datei(Proc, "bus/input/devices", "N: Name=\"SYNA Touchpad\"\n");
        Datei(Sys, "class/video4linux/video0/name", "Integrated Camera: Integrated C\n");
        Datei(Sys, "class/video4linux/video1/name", "Integrated Camera: Integrated I\n");
        Datei(Sys, "power/mem_sleep", "s2idle [deep]\n");
        Datei(Sys, "block/nvme0n1/queue/rotational", "0\n");
        Datei(Sys, "block/nvme0n1/removable", "0\n");
        Datei(Sys, "class/power_supply/BAT0/energy_full", "45000000\n");
        Datei(Sys, "class/power_supply/BAT0/energy_full_design", "50000000\n");
        Lspci(Geraet("VGA compatible controller [0300]", "Intel Corporation [8086]", "TigerLake GT2 [9a49]", "i915"),
              Geraet("Network controller [0280]", "Intel Corporation [8086]", "Wi-Fi 6 AX201 [a0f0]", "iwlwifi"),
              Geraet("Audio device [0403]", "Intel Corporation [8086]", "Smart Sound [a0c8]", "snd_hda_intel"),
              Geraet("Host bridge [0600]", "Intel Corporation [8086]", "Host Bridge [9a14]", ""));
    }

    public void Dispose() => Directory.Delete(_ordner, true);

    static void Datei(string wurzel, string rel, string inhalt)
    {
        var p = Path.Combine(wurzel, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, inhalt);
    }

    static string Geraet(string klasse, string hersteller, string name, string treiber) =>
        $"Slot:\t00:00.0\nClass:\t{klasse}\nVendor:\t{hersteller}\nDevice:\t{name}\n" + (treiber.Length > 0 ? $"Driver:\t{treiber}\n" : "");

    void Lspci(params string[] geraete) => _lspci = string.Join("\n", geraete);

    void Attrappe(string name, string inhalt)
    {
        var p = Path.Combine(Bin, name);
        File.WriteAllText(p, "#!/bin/sh\n" + inhalt + "\n");
        File.SetUnixFileMode(p, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    string Lauf()
    {
        File.WriteAllText(Path.Combine(_ordner, "lspci.txt"), _lspci + "\n");
        File.WriteAllText(Path.Combine(_ordner, "kern.txt"), _firmware);
        Attrappe("lspci", $"cat '{_ordner}/lspci.txt'");
        Attrappe("journalctl", $"cat '{_ordner}/kern.txt'");
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(ModulLader.ModulVerzeichnisFinden()!, "umstieg", "hardware.sh"));
        psi.Environment["PATH"] = Bin + ":/usr/bin:/bin";
        psi.Environment["DIAGNOSTIKER_SYSFS"] = Sys;
        psi.Environment["DIAGNOSTIKER_PROC"] = Proc;
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return aus;
    }

    static Status Bewerten(string aus)
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == "umstieg.hardware").Schritte.Single(x => x.Os == "linux");
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(s, res);
        return res.Status;
    }

    [Fact]
    public void Gesunder_Laptop_laeuft_gut()
    {
        var aus = Lauf();
        Assert.Contains("[OK] Grafik: Intel Corporation TigerLake GT2, Treiber i915.", aus);
        Assert.Contains("[OK] Netzwerk: Intel Corporation Wi-Fi 6 AX201, Treiber iwlwifi.", aus);
        Assert.Contains("[OK] Kamera erkannt: Integrated Camera.", aus);   // beide Geräteknoten, ein Name
        Assert.Contains("[OK] Touchpad erkannt.", aus);
        Assert.Contains("[OK] Akku BAT0: 90 %", aus);
        Assert.Contains("klassischer Standby", aus);
        Assert.Contains("[OK] Gesamteindruck", aus);
        Assert.DoesNotContain("Gerät ohne Treiber", aus);    // die Host-Bridge ist normal
        Assert.Equal(Status.Ok, Bewerten(aus));
    }

    [Fact]
    public void Grafik_ohne_Beschleunigung_ist_eine_Warnung_und_nouveau_eine_Information()
    {
        Lspci(Geraet("VGA compatible controller [0300]", "NVIDIA Corporation [10de]", "GK107 [0fc6]", "nouveau"));
        Assert.Contains("[INFO] Grafik: NVIDIA Corporation GK107 (Nvidia) läuft mit dem freien Treiber nouveau", Lauf());
        Lspci(Geraet("VGA compatible controller [0300]", "Vendor X [1234]", "Chip [0001]", "simpledrm"));
        var aus = Lauf();
        Assert.Contains("[WARNUNG] Grafik", aus);
        Assert.Contains("ohne Beschleunigung", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void WLAN_ohne_Treiber_und_unbekanntes_Geraet_werden_gemeldet()
    {
        Lspci(Geraet("Network controller [0280]", "Realtek [10ec]", "RTL8922AE [8922]", ""),
              Geraet("Unassigned class [ff00]", "Firma [abcd]", "Zusatzchip [0001]", ""));
        var aus = Lauf();
        Assert.Contains("[WARNUNG] Netzwerk: Realtek RTL8922AE hat keinen Treiber", aus);
        Assert.Contains("[INFO] Gerät ohne Treiber: Unassigned class – Firma Zusatzchip", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void Wenig_Speicher_32_Bit_und_verschlissener_Akku_werden_bewertet()
    {
        File.WriteAllText(Path.Combine(Proc, "meminfo"), "MemTotal:        2000000 kB\n");
        File.WriteAllText(Path.Combine(Proc, "cpuinfo"), "processor\t: 0\nmodel name\t: Alt\nflags\t\t: fpu sse2\n");
        File.WriteAllText(Path.Combine(Sys, "class/power_supply/BAT0/energy_full"), "20000000\n");
        var aus = Lauf();
        Assert.Contains("[WARNUNG] Arbeitsspeicher: 1.9 GiB", aus);
        Assert.Contains("[FEHLER] Der Prozessor kann nur 32 Bit", aus);
        Assert.Contains("[WARNUNG] Akku BAT0: nur noch 40 %", aus);
        Assert.Equal(Status.Fehler, Bewerten(aus));
    }

    [Fact]
    public void Festplatte_und_fehlender_Ton_sind_Warnungen()
    {
        File.WriteAllText(Path.Combine(Sys, "block/nvme0n1/queue/rotational"), "1\n");
        File.Delete(Path.Combine(Proc, "asound/cards"));
        var aus = Lauf();
        Assert.Contains("ist eine Festplatte (HDD)", aus);
        Assert.Contains("[WARNUNG] Ton: Kein Audiogerät erkannt", aus);
    }

    [Fact]
    public void Fehlende_Firmware_wird_aufgelistet()
    {
        _firmware = "iwlwifi 0000:00:14.3: Direct firmware load for iwlwifi-so-a0-gf-a0-89.ucode failed with error -2\nr8169 0000:03:00.0: Direct firmware load for rtl_nic/rtl8168h-2.fw failed with error -2\n";
        var aus = Lauf();
        Assert.Contains("Firmware konnte nicht geladen werden", aus);
        Assert.Contains("rtl_nic/rtl8168h-2.fw", aus);
    }
}
