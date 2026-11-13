using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft module/boot/startfaehigkeit.sh gegen nachgebaute Datenträger: lsblk, efibootmgr, blkid und die Dateisysteme
/// werden durch Attrappen und Ordner ersetzt (DIAGNOSTIKER_EINHAENGEN), es wird nichts gemountet.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class StartfaehigkeitModulTests : IDisposable
{
    const string EspTyp = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b";
    const string DatenTyp = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
    const string EspId = "aaaaaaaa-0000-0000-0000-000000000001";

    readonly string _ordner = Directory.CreateTempSubdirectory("starttest").FullName;
    string Bin => Path.Combine(_ordner, "bin");
    string Fs => Path.Combine(_ordner, "fs");
    readonly List<string> _zeilen = [];
    string _efibootmgr = "";
    string _blkidBekannt = "";

    public StartfaehigkeitModulTests() { Directory.CreateDirectory(Bin); Directory.CreateDirectory(Fs); }
    public void Dispose() => Directory.Delete(_ordner, true);

    void Zeile(string name, string typ, string pk = "", string fs = "", string label = "", string pt = "", string flags = "", string pu = "", string pttype = "")
        => _zeilen.Add($"NAME=\"{name}\" TYPE=\"{typ}\" PKNAME=\"{pk}\" FSTYPE=\"{fs}\" LABEL=\"{label}\" PARTTYPE=\"{pt}\" PARTFLAGS=\"{flags}\" PARTUUID=\"{pu}\" SIZE=\"100G\" MODEL=\"Testplatte\" TRAN=\"sata\" PTTYPE=\"{pttype}\"");

    void Datei(string relativ, string inhalt = "x")
    {
        var pfad = Path.Combine(Fs, relativ);
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        File.WriteAllText(pfad, inhalt);
    }

    void Attrappe(string name, string inhalt)
    {
        var pfad = Path.Combine(Bin, name);
        File.WriteAllText(pfad, "#!/bin/sh\n" + inhalt + "\n");
        File.SetUnixFileMode(pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Gesunde Windows-Platte (GPT): EFI-Partition mit Bootmanager und BCD, NTFS mit Systemdateien.</summary>
    void GesundesWindows()
    {
        Zeile("/dev/sda", "disk", pttype: "gpt");
        Zeile("/dev/sda1", "part", "/dev/sda", "vfat", pt: EspTyp, pu: EspId);
        Zeile("/dev/sda2", "part", "/dev/sda", "ntfs", pt: DatenTyp, pu: "aaaaaaaa-0000-0000-0000-000000000002");
        Datei("sda1/EFI/Microsoft/Boot/bootmgfw.efi"); Datei("sda1/EFI/Microsoft/Boot/BCD");
        Datei("sda2/Windows/System32/ntoskrnl.exe"); Datei("sda2/Windows/System32/winload.efi"); Datei("sda2/Windows/System32/config/SYSTEM");
        _efibootmgr = $"BootCurrent: 0001\nBootOrder: 0001\nBoot0001* Windows Boot Manager\tHD(1,GPT,{EspId},0x800,0x32000)/File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)";
    }

    string Lauf(string modus = "UEFI")
    {
        File.WriteAllText(Path.Combine(_ordner, "lsblk.txt"), string.Join('\n', _zeilen) + "\n");
        File.WriteAllText(Path.Combine(_ordner, "efi.txt"), _efibootmgr + "\n");
        Attrappe("lsblk", $"cat '{_ordner}/lsblk.txt'");
        Attrappe("efibootmgr", $"cat '{_ordner}/efi.txt'");
        Attrappe("blkid", $"""
            case "$1" in
              -U) case " {_blkidBekannt} " in *" $2 "*) echo /dev/sdx1 ;; esac ;;
            esac
            """);
        Attrappe("sgdisk", "echo 'No problems found. 0 free sectors available.'");
        Attrappe("fsck.fat", "exit 0");
        Attrappe("ntfsinfo", "echo 'Volume Flags: 0x0000'");
        Attrappe("dumpe2fs", "echo 'Filesystem state: clean'");
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(ModulLader.ModulVerzeichnisFinden()!, "boot", "startfaehigkeit.sh"));
        psi.Environment["PATH"] = Bin + ":/usr/bin:/bin";
        psi.Environment["DIAGNOSTIKER_EINHAENGEN"] = Fs;
        psi.Environment["DIAGNOSTIKER_MODUS"] = modus;
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return aus;
    }

    static Status Bewerten(string aus)
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == "boot.startfaehigkeit").Schritte.Single(x => x.Os == "linux");
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(s, res);
        return res.Status;
    }

    [Fact]
    public void Gesundes_Windows_ist_in_Ordnung()
    {
        GesundesWindows();
        var aus = Lauf();
        Assert.Equal(Status.Ok, Bewerten(aus));
        Assert.Contains("[OK] Auf /dev/sda sieht alles startfähig aus", aus);
        Assert.DoesNotContain("[FEHLER]", aus);
        Assert.DoesNotContain("[WARNUNG]", aus);
    }

    [Fact]
    public void Fehlende_BCD_ist_ein_Fehler()
    {
        GesundesWindows();
        File.Delete(Path.Combine(Fs, "sda1/EFI/Microsoft/Boot/BCD"));
        var aus = Lauf();
        Assert.Equal(Status.Fehler, Bewerten(aus));
        Assert.Contains("BCD", aus);
    }

    [Fact]
    public void Fehlender_Windows_Lader_und_leere_Registrierung_sind_Fehler()
    {
        GesundesWindows();
        File.Delete(Path.Combine(Fs, "sda2/Windows/System32/winload.efi"));
        File.WriteAllText(Path.Combine(Fs, "sda2/Windows/System32/config/SYSTEM"), "");
        var aus = Lauf();
        Assert.Equal(Status.Fehler, Bewerten(aus));
        Assert.Contains("winload.efi", aus);
        Assert.Contains("config/SYSTEM", aus);
    }

    [Fact]
    public void Groß_und_Kleinschreibung_der_Windows_Ordner_spielt_keine_Rolle()
    {
        GesundesWindows();
        Directory.Move(Path.Combine(Fs, "sda2/Windows"), Path.Combine(Fs, "sda2/WINDOWS"));
        Directory.Move(Path.Combine(Fs, "sda2/WINDOWS/System32"), Path.Combine(Fs, "sda2/WINDOWS/SYSTEM32"));
        Assert.Equal(Status.Ok, Bewerten(Lauf()));
    }

    [Fact]
    public void Leere_EFI_Partition_ist_ein_Fehler()
    {
        GesundesWindows();
        Directory.Delete(Path.Combine(Fs, "sda1/EFI"), true);
        Directory.CreateDirectory(Path.Combine(Fs, "sda1"));
        var aus = Lauf();
        Assert.Equal(Status.Fehler, Bewerten(aus));
        Assert.Contains("ist leer", aus);
    }

    [Fact]
    public void Eintrag_ins_Leere_und_leerer_NVRAM_werden_gemeldet()
    {
        GesundesWindows();
        _efibootmgr = "BootCurrent: 0001\nBoot0001* Alter Eintrag\tHD(1,GPT,bbbbbbbb-0000-0000-0000-00000000ffff,0x800,0x32000)/File(\\EFI\\Microsoft\\Boot\\bootmgfw.efi)";
        var aus = Lauf();
        Assert.Equal(Status.Warnung, Bewerten(aus));
        Assert.Contains("Alter Eintrag", aus);
        Assert.Contains("zeigt auf eine Partition, die es nicht mehr gibt", aus);

        _efibootmgr = "BootCurrent: 0000";
        aus = Lauf();
        Assert.Contains("keine Boot-Menü-Einträge", aus);
    }

    [Fact]
    public void Eintrag_auf_fehlende_Datei_ist_ein_Fehler()
    {
        GesundesWindows();
        _efibootmgr = $"Boot0001* Windows Boot Manager\tHD(1,GPT,{EspId},0x800,0x32000)/File(\\EFI\\Microsoft\\Boot\\gibtsnicht.efi)";
        var aus = Lauf();
        Assert.Equal(Status.Fehler, Bewerten(aus));
        Assert.Contains("gibtsnicht.efi", aus);
    }

    [Fact]
    public void Windows_auf_GPT_im_BIOS_Modus_ist_eine_Warnung()
    {
        GesundesWindows();
        var aus = Lauf("BIOS");
        Assert.Equal(Status.Warnung, Bewerten(aus));
        Assert.Contains("nur im UEFI-Modus", aus);
    }

    [Fact]
    public void GPT_ohne_EFI_Partition_im_UEFI_Modus_ist_ein_Fehler()
    {
        Zeile("/dev/sda", "disk", pttype: "gpt");
        Zeile("/dev/sda1", "part", "/dev/sda", "ntfs", pt: DatenTyp, pu: "aaaaaaaa-0000-0000-0000-000000000002");
        Datei("sda1/Windows/System32/ntoskrnl.exe"); Datei("sda1/Windows/System32/winload.efi"); Datei("sda1/Windows/System32/config/SYSTEM");
        var aus = Lauf();
        Assert.Equal(Status.Fehler, Bewerten(aus));
        Assert.Contains("keine EFI-Systempartition", aus);
    }

    [Fact]
    public void Linux_ohne_Kernel_und_fstab_mit_fehlendem_Laufwerk_sind_Fehler()
    {
        Zeile("/dev/sda", "disk", pttype: "gpt");
        Zeile("/dev/sda1", "part", "/dev/sda", "ext4", pt: DatenTyp, pu: "aaaaaaaa-0000-0000-0000-000000000003");
        Datei("sda1/etc/os-release", "PRETTY_NAME=\"Testlinux\"\n");
        Datei("sda1/etc/fstab", "UUID=weg-1234 / ext4 defaults 0 1\nUUID=da-5678 /daten ext4 defaults 0 2\nUUID=weg-9999 /extra ext4 nofail 0 2\n");
        _blkidBekannt = "da-5678";
        var aus = Lauf("BIOS");
        Assert.Equal(Status.Fehler, Bewerten(aus));
        Assert.Contains("kein Kernel", aus);
        Assert.Contains("UUID=weg-1234 für / existiert nicht", aus);
        Assert.DoesNotContain("UUID=da-5678", aus);     // vorhanden
        Assert.DoesNotContain("UUID=weg-9999", aus);    // nofail
    }

    [Fact]
    public void Gesundes_Linux_ist_in_Ordnung()
    {
        Zeile("/dev/sda", "disk", pttype: "gpt");
        Zeile("/dev/sda1", "part", "/dev/sda", "", pt: "21686148-6449-6e6f-744e-656564454649");
        Zeile("/dev/sda2", "part", "/dev/sda", "ext4", pt: DatenTyp, pu: "aaaaaaaa-0000-0000-0000-000000000003");
        Datei("sda2/etc/os-release", "PRETTY_NAME=\"Testlinux\"\n");
        Datei("sda2/etc/fstab", "UUID=da-5678 / ext4 defaults 0 1\n");
        Datei("sda2/boot/vmlinuz-6.1"); Datei("sda2/boot/initrd.img-6.1"); Datei("sda2/boot/grub/grub.cfg");
        _blkidBekannt = "da-5678";
        var aus = Lauf("BIOS");
        Assert.Equal(Status.Ok, Bewerten(aus));
        Assert.Contains("Testlinux", aus);
    }

    [Fact]
    public void Der_Stick_selbst_wird_nicht_geprueft()
    {
        GesundesWindows();
        Zeile("/dev/sdb", "disk", pttype: "gpt");
        Zeile("/dev/sdb2", "part", "/dev/sdb", "vfat", "DIAGBOOT", pt: DatenTyp);
        Zeile("/dev/sdb3", "part", "/dev/sdb", "exfat", "DIAGDATEN", pt: DatenTyp);
        var aus = Lauf();
        Assert.DoesNotContain("/dev/sdb:", aus);
        Assert.Contains("/dev/sda:", aus);
    }

    [Fact]
    public void Verschluesselte_Partition_wird_nur_gemeldet()
    {
        Zeile("/dev/sda", "disk", pttype: "gpt");
        Zeile("/dev/sda1", "part", "/dev/sda", "BitLocker", pt: DatenTyp);
        var aus = Lauf();
        Assert.Contains("BitLocker", aus);
        Assert.DoesNotContain("[FEHLER]", aus);
    }

    [Fact]
    public void Ohne_Root_wird_nichts_versucht()
    {
        if (Environment.UserName == "root") return;
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(ModulLader.ModulVerzeichnisFinden()!, "boot", "startfaehigkeit.sh"));
        psi.Environment.Remove("DIAGNOSTIKER_EINHAENGEN");
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.Contains("KEINE_RECHTE", aus);
        Assert.DoesNotContain("[FEHLER]", aus);
    }
}
