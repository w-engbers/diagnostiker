using System.Diagnostics;
using System.Text;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft module/boot/windows-start.sh gegen nachgebaute Windows-Partitionen (Ordner statt Laufwerken, lsblk und cryptsetup als Attrappen).
/// Das Öffnen eines echten BitLocker-Laufwerks ist hier nicht Teil des Tests.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class WindowsStartModulTests : IDisposable
{
    const string Schluessel = "123456-234567-345678-456789-567890-678901-789012-890123";

    readonly string _ordner = Directory.CreateTempSubdirectory("winstart").FullName;
    string Bin => Path.Combine(_ordner, "bin");
    string Fs => Path.Combine(_ordner, "fs");
    readonly List<string> _zeilen = [];

    public WindowsStartModulTests() { Directory.CreateDirectory(Bin); Directory.CreateDirectory(Fs); }
    public void Dispose() => Directory.Delete(_ordner, true);

    void Teil(string name, string fs, string groesse = "100G")
        => _zeilen.Add($"NAME=\"{name}\" TYPE=\"part\" FSTYPE=\"{fs}\" SIZE=\"{groesse}\"");

    string Pfad(string rel) { var p = Path.Combine(Fs, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); return p; }
    void Datei(string rel, string inhalt = "x") => File.WriteAllText(Pfad(rel), inhalt);

    void WindowsPartition(string name = "sda2")
    {
        Teil("/dev/" + name, "ntfs");
        Datei($"{name}/Windows/System32/ntoskrnl.exe");
    }

    static byte[] Absturzabbild(uint stoppcode)
    {
        var b = new byte[0x100];
        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(b, 0);
        BitConverter.GetBytes(stoppcode).CopyTo(b, 56);
        return b;
    }

    void Attrappe(string name, string inhalt)
    {
        var p = Path.Combine(Bin, name);
        File.WriteAllText(p, "#!/bin/sh\n" + inhalt + "\n");
        File.SetUnixFileMode(p, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    string Lauf(string? schluessel = null, string? cryptsetup = null)
    {
        File.WriteAllText(Path.Combine(_ordner, "lsblk.txt"), string.Join('\n', _zeilen) + "\n");
        Attrappe("lsblk", $"cat '{_ordner}/lsblk.txt'");
        if (cryptsetup is not null) Attrappe("cryptsetup", cryptsetup);
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(ModulLader.ModulVerzeichnisFinden()!, "boot", "windows-start.sh"));
        psi.Environment["PATH"] = Bin + ":/usr/bin:/bin";
        psi.Environment["DIAGNOSTIKER_EINHAENGEN"] = Fs;
        if (schluessel is not null) psi.Environment["DIAGNOSTIKER_PARAM_SCHLUESSEL"] = schluessel;
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return aus;
    }

    static Status Bewerten(string aus)
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == "boot.windows-start").Schritte.Single(x => x.Os == "linux");
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(s, res);
        return res.Status;
    }

    [Fact]
    public void Ohne_Hinweise_bleibt_es_eine_Information()
    {
        WindowsPartition();
        var aus = Lauf();
        Assert.Contains("Windows-Installation auf /dev/sda2 gefunden", aus);
        Assert.Contains("keine Hinweise auf die Ursache", aus);
        Assert.Equal(Status.Info, Bewerten(aus));
    }

    [Fact]
    public void Startreparatur_Datei_wird_aus_UTF16_gelesen()
    {
        WindowsPartition();
        var inhalt = "Startup Repair diagnosis and repair log\r\nRoot cause found:\r\n  Boot configuration data is missing\r\nTest Performed: Check for updates\r\n  Result: Completed successfully.\r\n  Error code = 0x0\r\nRepair action failed\r\n";
        File.WriteAllBytes(Pfad("sda2/Windows/System32/Logfiles/Srt/SrtTrail.txt"), [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(inhalt)]);
        var aus = Lauf();
        Assert.Contains("Startreparatur ist gelaufen", aus);
        Assert.Contains("Root cause found:", aus);
        Assert.Contains("Repair action failed", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void Absturzabbild_nennt_Stoppcode_und_Bedeutung()
    {
        WindowsPartition();
        File.WriteAllBytes(Pfad("sda2/Windows/Minidump/111426-1.dmp"), Absturzabbild(0x7B));
        var aus = Lauf();
        Assert.Contains("Stoppcode 0x7B INACCESSIBLE_BOOT_DEVICE", aus);
        Assert.Contains("SATA-Modus", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void Unbekannter_Stoppcode_wird_trotzdem_gemeldet()
    {
        WindowsPartition();
        File.WriteAllBytes(Pfad("sda2/Windows/MEMORY.DMP"), Absturzabbild(0x1234));
        var aus = Lauf();
        Assert.Contains("MEMORY.DMP", aus);
        Assert.Contains("Stoppcode 0x1234 unbekannt", aus);
    }

    [Fact]
    public void Ausstehende_Updates_sind_eine_Warnung()
    {
        WindowsPartition();
        Datei("sda2/Windows/WinSxS/pending.xml", "<xml/>");
        var aus = Lauf();
        Assert.Contains("pending.xml", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void BitLocker_ohne_Schluessel_wird_nicht_geprueft_und_gemeldet()
    {
        Teil("/dev/sda3", "BitLocker", "900G");
        var aus = Lauf();
        Assert.Contains("ohne Wiederherstellungsschlüssel ungeprüft", aus);
        Assert.Contains("keine Windows-Installation geprüft", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void BitLocker_mit_Schluessel_wird_entsperrt_geprueft_und_der_Schluessel_nie_ausgegeben()
    {
        Teil("/dev/sda3", "BitLocker", "900G");
        Datei("diag-bl1/Windows/System32/ntoskrnl.exe");
        Datei("diag-bl1/Windows/WinSxS/pending.xml", "<xml/>");
        // Attrappe: Schlüssel kommt über Standardeingabe, in der Schreibweise mit Bindestrichen; nur dann gelingt das Öffnen.
        var aus = Lauf(schluessel: "123456 234567 345678 456789 567890 678901 789012 890123",
            cryptsetup: $"[ \"$1\" = bitlkOpen ] || exit 0\nread -r k\n[ \"$k\" = '{Schluessel}' ] && exit 0\necho 'falscher Schluessel' >&2; exit 2");
        Assert.Contains("Windows-Installation auf /dev/sda3 (entsperrt) gefunden", aus);
        Assert.Contains("pending.xml", aus);
        Assert.DoesNotContain("123456", aus);
        Assert.DoesNotContain("890123", aus);
    }

    [Fact]
    public void Falscher_Schluessel_ist_ein_Fehler_ohne_Preisgabe()
    {
        Teil("/dev/sda3", "BitLocker", "900G");
        var aus = Lauf(schluessel: "111111-222222-333333-444444-555555-666666-777777-888888",
            cryptsetup: "cat >/dev/null; echo 'Keine passende Passphrase' >&2; exit 2");
        Assert.Contains("[FEHLER] Partition /dev/sda3 lässt sich nicht entsperren", aus);
        Assert.DoesNotContain("111111", aus);
        Assert.Equal(Status.Fehler, Bewerten(aus));
    }

    [Fact]
    public void Schluessel_mit_falscher_Laenge_wird_abgelehnt()
    {
        Teil("/dev/sda3", "BitLocker", "900G");
        var aus = Lauf(schluessel: "12345-67", cryptsetup: "exit 0");
        Assert.Contains("nicht 48 Ziffern", aus);
        Assert.DoesNotContain("12345", aus.Replace("48 Ziffern", ""));
    }

    [Fact]
    public void Geheime_Parameter_stehen_weder_in_der_Befehlszeile_noch_im_Protokoll()
    {
        var modul = new ModulDefinition
        {
            Id = "test.geheim", Titel = "t",
            Parameter = [new ParameterDefinition { Name = "schluessel", Geheim = true }, new ParameterDefinition { Name = "offen" }],
            Schritte = [new SchrittDefinition { Os = "linux", Befehl = "sh", Argumente = ["-c", "echo \"[$DIAGNOSTIKER_PARAM_SCHLUESSEL] {offen} {schluessel}\" | wc -c"], ZeigeAusgabe = true }],
        };
        var ausfuehrer = new ModulAusfuehrer(_ordner);
        var erg = ausfuehrer.AusfuehrenAsync(modul, new Dictionary<string, string> { ["schluessel"] = "GEHEIM123", ["offen"] = "sichtbar" }).GetAwaiter().GetResult();
        var s = erg.Schritte.Single();
        Assert.DoesNotContain("GEHEIM123", s.Befehlszeile);
        Assert.Contains("sichtbar", s.Befehlszeile);
        Assert.Contains("{schluessel}", s.Befehlszeile);   // nicht ersetzt
        // Das Programm hat den Wert trotzdem bekommen: "[GEHEIM123] sichtbar {schluessel}" hat 33 Zeichen plus Zeilenende.
        Assert.Equal("34", s.Ausgabe.Trim());
    }
}
