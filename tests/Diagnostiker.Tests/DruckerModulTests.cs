using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft module/umstieg/drucker.sh mit einer nachgebauten Windows-Partition und Attrappen von hivexsh, hivexget und lpinfo.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class DruckerModulTests : IDisposable
{
    readonly string _ordner = Directory.CreateTempSubdirectory("druckertest").FullName;
    string Bin => Path.Combine(_ordner, "bin");
    string Fs => Path.Combine(_ordner, "fs");
    readonly List<(string Name, string Treiber, string Port)> _drucker = [];
    string _modelle = "";
    string _geraete = "";
    bool _mitWindows = true;

    public DruckerModulTests()
    {
        Directory.CreateDirectory(Bin); Directory.CreateDirectory(Fs);
        _modelle = string.Join('\n',
            "drv:///brlaser.drv/brl2350dw.ppd Brother HL-L2350DW, using brlaser v6",
            "drv:///hpcups.drv/hp-laserjet_pro_m15w.ppd HP LaserJet Pro M15w, hpcups 3.23.12",
            "epson-escpr-en.ppd Epson XP-420 Series, Epson Inkjet Printer Driver (ESC/P-R) for Linux");
    }

    public void Dispose() => Directory.Delete(_ordner, true);

    void Drucker(string name, string treiber, string port) => _drucker.Add((name, treiber, port));

    void Attrappe(string name, string inhalt)
    {
        var p = Path.Combine(Bin, name);
        File.WriteAllText(p, "#!/bin/sh\n" + inhalt + "\n");
        File.SetUnixFileMode(p, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    string Lauf()
    {
        if (_mitWindows)
        {
            var hive = Path.Combine(Fs, "sda2/Windows/System32/config/SYSTEM");
            Directory.CreateDirectory(Path.GetDirectoryName(hive)!);
            File.WriteAllText(hive, "x");
            File.WriteAllText(Path.Combine(Fs, "sda2/Windows/System32/ntoskrnl.exe"), "x");
        }
        File.WriteAllText(Path.Combine(_ordner, "lsblk.txt"), _mitWindows ? "NAME=\"/dev/sda2\" TYPE=\"part\" FSTYPE=\"ntfs\" SIZE=\"100G\"\n" : "NAME=\"/dev/sda2\" TYPE=\"part\" FSTYPE=\"ext4\" SIZE=\"100G\"\n");
        File.WriteAllText(Path.Combine(_ordner, "namen.txt"), string.Join('\n', _drucker.Select(d => d.Name)) + "\n");
        File.WriteAllText(Path.Combine(_ordner, "werte.txt"), string.Join('\n', _drucker.SelectMany(d => new[] { $"{d.Name}|Printer Driver|{d.Treiber}", $"{d.Name}|Port|{d.Port}" })) + "\n");
        File.WriteAllText(Path.Combine(_ordner, "modelle.txt"), _modelle.Length == 0 ? "" : _modelle + "\n");
        File.WriteAllText(Path.Combine(_ordner, "geraete.txt"), _geraete + "\n");
        Attrappe("systemctl", "exit 0");   // den Druckdienst startet der Test nicht wirklich
        Attrappe("sleep", "exit 0");
        Attrappe("lsblk", $"cat '{_ordner}/lsblk.txt'");
        // Attrappe von hivexsh -u <hive> mit Befehlen auf der Standardeingabe: cd <Pfad oder Unterschlüssel>, ls, lsval (Werte als "Name"="Wert")
        Attrappe("hivexsh", $$"""
            case "$1" in -u) ;; *) echo "hivexsh: Option -u fehlt (Hive ist unsauber)" >&2; exit 1 ;; esac
            pfad=""
            while IFS= read -r z; do
              case "$z" in
                "cd "*) a=${z#cd }; case "$a" in \\*) pfad=$a ;; *) pfad="$pfad\\$a" ;; esac ;;
                ls) cat '{{_ordner}}/namen.txt' ;;
                lsval)
                  case "$pfad" in
                    *\\Select) echo '"Current"=dword:00000001' ;;
                    *) n="${pfad##*\\}"; grep -F "$n|" '{{_ordner}}/werte.txt' | while IFS='|' read -r nn k w; do printf '"%s"="%s"\n' "$k" "$w"; done ;;
                  esac ;;
              esac
            done
            """);
        Attrappe("lpinfo", $"case \"$1\" in -m) cat '{_ordner}/modelle.txt' ;; -v) cat '{_ordner}/geraete.txt' ;; esac");
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(ModulLader.ModulVerzeichnisFinden()!, "umstieg", "drucker.sh"));
        psi.Environment["PATH"] = Bin + ":/usr/bin:/bin";
        psi.Environment["DIAGNOSTIKER_EINHAENGEN"] = Fs;
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return aus;
    }

    static Status Bewerten(string aus)
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == "umstieg.drucker").Schritte.Single(x => x.Os == "linux");
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(s, res);
        return res.Status;
    }

    [Fact]
    public void Bekannte_Drucker_bekommen_einen_Treiber_und_virtuelle_werden_ignoriert()
    {
        Drucker("Brother HL-L2350DW series", "Brother HL-L2350DW series", "USB001");
        Drucker("HP LaserJet Pro M15w", "HP LaserJet Pro M15 PCL-6", "USB002");
        Drucker("Microsoft Print to PDF", "Microsoft Print To PDF", "PORTPROMPT:");
        var aus = Lauf();
        Assert.Contains("gelesen: 3 Druckerwarteschlange(n), davon 1 virtuelle", aus);
        Assert.Contains("[OK] Drucker „Brother HL-L2350DW series“ (USB): Treiber im Live-System vorhanden: Brother HL-L2350DW, using brlaser", aus);
        Assert.Contains("[OK] Drucker „HP LaserJet Pro M15w“ (USB): Treiber im Live-System vorhanden: HP LaserJet Pro M15w", aus);
        Assert.DoesNotContain("Drucker „Microsoft Print to PDF“", aus);   // virtuelle Drucker werden nicht bewertet
        Assert.Equal(Status.Ok, Bewerten(aus));
    }

    [Fact]
    public void Unbekannter_USB_Drucker_ist_eine_Warnung_Netzwerkdrucker_nur_ein_Hinweis()
    {
        Drucker("Fabrikat ZX-9000", "Fabrikat ZX-9000", "USB003");
        Drucker("Netzdrucker Q-7000", "Netzdrucker Q-7000", "IP_192.168.0.7");
        var aus = Lauf();
        Assert.Contains("[WARNUNG] Drucker „Fabrikat ZX-9000“ (USB): kein passender Treiber in der Datenbank gefunden", aus);
        Assert.Contains("[INFO] Drucker „Netzdrucker Q-7000“ (Netzwerk): kein eigener Treiber in der Datenbank gefunden, aber Netzwerkdrucker arbeiten meist ohne Treiber", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void Reine_Windows_Drucker_werden_als_kritisch_markiert()
    {
        Drucker("Canon LBP2900", "Canon LBP2900 GDI", "USB001");
        var aus = Lauf();
        Assert.Contains("ein reiner Windows-Drucker (GDI/Host-based", aus);
        Assert.Equal(Status.Warnung, Bewerten(aus));
    }

    [Fact]
    public void Modellnummer_muss_als_eigenes_Wort_passen_M15_ist_nicht_M1522n()
    {
        _modelle = "foomatic:HP-LaserJet_M1522n.ppd HP LaserJet M1522n MFP Foomatic/Postscript\ndrv:///hpcups.drv/hp-laserjet_pro_m15w.ppd HP LaserJet Pro M15w, hpcups 3.23.12";
        Drucker("HP LaserJet Pro M15w", "HP LaserJet Pro M15 PCL-6", "USB002");
        var aus = Lauf();
        Assert.Contains("Treiber im Live-System vorhanden: HP LaserJet Pro M15w, hpcups", aus);
        Assert.DoesNotContain("M1522n", aus);

        _drucker.Clear();
        _modelle = "foomatic:HP-LaserJet_M1522n.ppd HP LaserJet M1522n MFP Foomatic/Postscript";
        Drucker("HP LaserJet Pro M15w", "HP LaserJet Pro M15 PCL-6", "USB002");
        aus = Lauf();
        Assert.Contains("[WARNUNG] Drucker „HP LaserJet Pro M15w“", aus);
        Assert.Contains("generischen PCL- oder PostScript-Treiber", aus);
    }

    [Fact]
    public void Nur_virtuelle_Drucker_ergibt_eine_Information()
    {
        Drucker("Microsoft Print to PDF", "Microsoft Print To PDF", "PORTPROMPT:");
        Drucker("OneNote (Desktop)", "Send to Microsoft OneNote 16 Driver", "nul:");
        var aus = Lauf();
        Assert.Contains("keine echten Drucker eingerichtet", aus);
        Assert.Equal(Status.Info, Bewerten(aus));
    }

    [Fact]
    public void Angeschlossene_Drucker_werden_aufgelistet()
    {
        _mitWindows = false;
        _geraete = "direct usb://Brother/HL-L2350DW%20series?serial=ABC\nnetwork dnssd://Meine%20Tinte%20XP-999._ipp._tcp.local/?uuid=1\nnetwork socket://192.168.5.5\ndirect hp\n";
        var aus = Lauf();
        Assert.Contains("[OK] Gefunden: Brother HL-L2350DW series (USB): Treiber im Live-System vorhanden", aus);
        Assert.Contains("[INFO] Gefunden: Meine Tinte XP-999 (Netzwerk (Bonjour)): spricht IPP und arbeitet meist ohne Treiber", aus);
        Assert.Contains("[WARNUNG] Gefunden: 192.168.5.5 (Netzwerk)", aus);
        Assert.Contains("Keine Windows-Installation", aus);
    }

    [Fact]
    public void Ohne_Treiberdatenbank_wird_das_gesagt()
    {
        Drucker("Brother HL-L2350DW series", "Brother HL-L2350DW series", "USB001");
        _modelle = "";
        var aus = Lauf();
        Assert.Contains("Treiberdatenbank für Drucker (CUPS) ist im Live-System nicht verfügbar", aus);
    }
}
