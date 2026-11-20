using System.Diagnostics;
using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft die Bluetooth-Module mit Attrappen von rfkill, systemctl, bluetoothctl und dmesg sowie einem nachgebauten /sys.
/// Die Shell-Logik der Manifeste läuft wirklich; echte Bluetooth-Hardware ist dafür nicht nötig (und noch nicht gemessen).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class BluetoothModulTests : IDisposable
{
    readonly string _wurzel = Directory.CreateTempSubdirectory("bttest").FullName;
    string Bin => Path.Combine(_wurzel, "bin");
    string Sys => Path.Combine(_wurzel, "sys");

    public BluetoothModulTests() { Directory.CreateDirectory(Bin); Directory.CreateDirectory(Path.Combine(Sys, "class", "bluetooth")); Directory.CreateDirectory(Path.Combine(Sys, "bus", "usb", "devices")); }
    public void Dispose() => Directory.Delete(_wurzel, true);

    void Attrappe(string name, string inhalt)
    {
        var pfad = Path.Combine(Bin, name);
        File.WriteAllText(pfad, "#!/bin/sh\n" + inhalt);
        File.SetUnixFileMode(pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    void Controller(string name = "hci0", string treiber = "btusb")
    {
        var hci = Directory.CreateDirectory(Path.Combine(Sys, "class", "bluetooth", name, "device")).FullName;
        var drv = Directory.CreateDirectory(Path.Combine(_wurzel, "drivers", treiber)).FullName;
        File.CreateSymbolicLink(Path.Combine(hci, "driver"), drv);
    }

    void UsbBluetoothGeraet()
    {
        var d = Directory.CreateDirectory(Path.Combine(Sys, "bus", "usb", "devices", "1-1:1.0")).FullName;
        File.WriteAllText(Path.Combine(d, "bInterfaceClass"), "e0\n");
        File.WriteAllText(Path.Combine(d, "bInterfaceSubClass"), "01\n");
    }

    (string Ausgabe, Status Status, SchrittErgebnis Ergebnis) Lauf(string modulId, string nm = "active", string powered = "yes",
        string rfkill = "0: hci0: Bluetooth\n\tSoft blocked: no\n\tHard blocked: no", string dmesg = "", string scan = "", string? dmesgNachher = null)
    {
        Attrappe("systemctl", $"echo {nm}\n");
        Attrappe("rfkill", $"cat <<'EOF'\n{rfkill}\nEOF\n");
        // Mit dmesgNachher liefert die Attrappe beim ersten Aufruf dmesg, danach dmesgNachher (der Adapter ist zwischendurch abgestürzt).
        var zaehler = Path.Combine(_wurzel, "dmesg-aufrufe");
        Attrappe("dmesg", dmesgNachher is null ? $"cat <<'EOF'\n{dmesg}\nEOF\n"
            : $"n=$(cat {zaehler} 2>/dev/null || echo 0); echo $((n+1)) > {zaehler}\nif [ \"$n\" -eq 0 ]; then cat <<'EOF'\n{dmesg}\nEOF\nelse cat <<'EOF'\n{dmesgNachher}\nEOF\nfi\n");
        Attrappe("bluetoothctl", $$"""
            case "$*" in
              show) printf 'Controller 00:11:22:33:44:55 (public)\n\tPowered: {{powered}}\n' ;;
              *scan*) cat <<'EOF'
            {{scan}}
            EOF
              ;;
            esac
            """);

        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var schritt = module.Single(m => m.Id == modulId).Schritte.Single(s => s.Os == "linux");
        var psi = new ProcessStartInfo("sh") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in schritt.Argumente) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = Bin + ":/usr/bin:/bin";
        psi.Environment["DIAGNOSTIKER_SYSFS"] = Sys;
        using var p = Process.Start(psi)!;
        var aus = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var res = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 };
        ModulAusfuehrer.Bewerten(schritt, res);
        return (aus, res.Status, res);
    }

    static bool Meldet(SchrittErgebnis r, string text) => r.Meldungen.Any(m => m.Contains(text));
    const string Adapter = "netz.bluetooth";
    const string Suche = "netz.bluetooth-suche";

    [Fact]
    public void Ohne_Adapter_ist_es_nur_eine_Information()
    {
        var r = Lauf(Adapter);
        Assert.Contains("KEIN_ADAPTER_BT", r.Ausgabe);
        Assert.Equal(Status.Info, r.Status);
    }

    [Fact]
    public void USB_Bluetooth_ohne_Controller_ist_ein_Fehler()
    {
        UsbBluetoothGeraet();
        var r = Lauf(Adapter);
        Assert.Contains("BT_HARDWARE_OHNE_CONTROLLER", r.Ausgabe);
        Assert.Equal(Status.Fehler, r.Status);
        Assert.True(Meldet(r.Ergebnis, "Treiber oder Firmware"));
    }

    [Fact]
    public void Gesunder_Adapter_ist_ok_und_nennt_den_Treiber()
    {
        Controller();
        var r = Lauf(Adapter);
        Assert.Contains("BT-Controller hci0: Treiber btusb", r.Ausgabe);
        Assert.Contains("Dienst bluetooth: active", r.Ausgabe);
        Assert.Contains("Controller eingeschaltet: yes", r.Ausgabe);
        Assert.Equal(Status.Ok, r.Status);
    }

    [Fact]
    public void Verbindungsobjekte_wie_hci0_1_zaehlen_nicht_als_Controller()
    {
        Controller();
        Directory.CreateDirectory(Path.Combine(Sys, "class", "bluetooth", "hci0:256"));
        Assert.Single(Lauf(Adapter).Ausgabe.Split('\n'), z => z.StartsWith("BT-Controller"));
    }

    [Fact]
    public void Ausgeschalteter_Controller_ist_eine_Warnung()
    {
        Controller();
        var r = Lauf(Adapter, powered: "no");
        Assert.Equal(Status.Warnung, r.Status);
        Assert.True(Meldet(r.Ergebnis, "ausgeschaltet"));
    }

    [Fact]
    public void Nicht_laufender_Dienst_ist_ein_Fehler()
    {
        Controller();
        Assert.Equal(Status.Fehler, Lauf(Adapter, nm: "inactive").Status);
    }

    [Fact]
    public void Software_und_Hardwaresperre_sind_Warnungen()
    {
        Controller();
        Assert.Equal(Status.Warnung, Lauf(Adapter, rfkill: "0: hci0: Bluetooth\n\tSoft blocked: yes\n\tHard blocked: no").Status);
        Assert.Equal(Status.Warnung, Lauf(Adapter, rfkill: "0: hci0: Bluetooth\n\tSoft blocked: no\n\tHard blocked: yes").Status);
    }

    [Fact]
    public void Firmwarefehler_im_Kernelprotokoll_ist_eine_Warnung_normale_Firmwaremeldung_nicht()
    {
        Controller();
        var normal = Lauf(Adapter, dmesg: "[    5.0] Bluetooth: hci0: Intel Bluetooth firmware file: intel/ibt-0040-1050.sfi\n[    5.2] Bluetooth: hci0: Intel firmware patch completed and activated");
        Assert.DoesNotContain("FIRMWARE_FEHLER", normal.Ausgabe);
        Assert.Equal(Status.Ok, normal.Status);

        var fehler = Lauf(Adapter, dmesg: "[    4.9] bluetooth hci0: Direct firmware load for intel/ibt-0040-1050.sfi failed with error -2\n[    5.0] Bluetooth: hci0: Failed to load firmware file (-2)");
        Assert.Contains("FIRMWARE_FEHLER", fehler.Ausgabe);
        Assert.Contains("Kernel:", fehler.Ausgabe);
        Assert.Equal(Status.Warnung, fehler.Status);
    }

    // Die Kernelzeilen stammen aus einem echten Lauf (18.11.2026): Ein Realtek-Adapter (RTL8761B), der bei jedem LE-Scan hängen blieb.
    const string AufhaengerKernel = """
        [ 1812.1] Bluetooth: hci4: RTL: RTL: Read reg16 failed (-110)
        [ 1812.2] Bluetooth: hci4: RTL: fw version 0xdfc6d922
        [ 1920.3] Bluetooth: hci4: command tx timeout
        [ 1920.3] Bluetooth: hci4: Resetting usb device.
        [ 1923.0] Bluetooth: hci5: command 0xfc61 tx timeout
        [ 1923.0] Bluetooth: hci5: Resetting usb device.
        [ 2010.1] Bluetooth: hci5: command 0x2005 tx timeout
        [ 2010.1] Bluetooth: hci5: Opcode 0x200b failed: -110
        [ 2010.1] Bluetooth: hci5: Resetting usb device.
        """;

    [Fact]
    public void Adapter_der_sich_wiederholt_aufhaengt_ist_ein_Fehler_und_kein_Firmwareproblem()
    {
        Controller();
        var r = Lauf(Adapter, dmesg: AufhaengerKernel);
        Assert.Contains("BT_INSTABIL", r.Ausgabe);
        Assert.Contains("Zurücksetzungen des Adapters im Kernel-Protokoll: 3", r.Ausgabe);
        Assert.DoesNotContain("FIRMWARE_FEHLER", r.Ausgabe);            // die irreführende Diagnose „fehlende Firmware“ entfällt
        Assert.Equal(Status.Fehler, r.Status);
        Assert.True(Meldet(r.Ergebnis, "hängt sich wiederholt auf"));
    }

    [Fact]
    public void Eine_einzelne_Zuruecksetzung_ist_nur_eine_Warnung()
    {
        Controller();
        var r = Lauf(Adapter, dmesg: "[ 12.0] Bluetooth: hci0: command tx timeout\n[ 12.0] Bluetooth: hci0: Resetting usb device.");
        Assert.Contains("BT_ZURUECKGESETZT", r.Ausgabe);
        Assert.DoesNotContain("BT_INSTABIL", r.Ausgabe);
        Assert.Equal(Status.Warnung, r.Status);
    }

    [Fact]
    public void Fremde_Zuruecksetzungen_ohne_Bluetooth_zaehlen_nicht()
    {
        Controller();
        var r = Lauf(Adapter, dmesg: "[ 12.0] usb 1-6: Resetting usb device for something else\n[ 13.0] xhci_hcd: reset");
        Assert.DoesNotContain("BT_", r.Ausgabe);
        Assert.Equal(Status.Ok, r.Status);
    }

    // ---- Suche ----

    [Fact]
    public void Suche_ohne_Controller_ist_eine_Information()
    {
        var r = Lauf(Suche);
        Assert.Contains("KEIN_CONTROLLER", r.Ausgabe);
        Assert.Equal(Status.Info, r.Status);
    }

    [Fact]
    public void Suche_zeigt_Geraete_mit_Namen_und_nur_der_Herstellerkennung()
    {
        Controller();
        var r = Lauf(Suche, scan: "[NEW] Device AA:BB:CC:DD:EE:FF Pixel 8\n[NEW] Device 11:22:33:44:55:66 Kopfhörer");
        Assert.Contains("Gefundene Bluetooth-Geräte: 2", r.Ausgabe);
        Assert.Contains("Gerät \"Pixel 8\" (Hersteller-Kennung AA:BB:CC)", r.Ausgabe);
        Assert.DoesNotContain("DD:EE:FF", r.Ausgabe);          // die volle Adresse gehört nicht in den Bericht
        Assert.Equal(Status.Ok, r.Status);
    }

    [Fact]
    public void Geraete_ohne_Namen_zeigen_nicht_die_volle_Adresse_als_Namen()
    {
        Controller();
        // BlueZ nennt ein Gerät ohne Namen wie seine Adresse (mit Bindestrichen): Das darf nicht im Bericht landen.
        var r = Lauf(Suche, scan: "[NEW] Device 6B:05:D5:FE:26:43 6B-05-D5-FE-26-43\n[NEW] Device 3D:93:E2:E2:F9:AA 3D-93-E2-E2-F9-AA\n[NEW] Device AA:BB:CC:DD:EE:FF Pixel 8");
        Assert.Contains("Gefundene Bluetooth-Geräte: 3", r.Ausgabe);
        Assert.Contains("Gerät \"(ohne Namen)\" (Hersteller-Kennung 6B:05:D5)", r.Ausgabe);
        Assert.Contains("Gerät \"Pixel 8\" (Hersteller-Kennung AA:BB:CC)", r.Ausgabe);
        Assert.DoesNotContain("26-43", r.Ausgabe);
        Assert.DoesNotContain("F9-AA", r.Ausgabe);
        Assert.DoesNotContain("26:43", r.Ausgabe);
        Assert.Equal(Status.Ok, r.Status);
    }

    [Fact]
    public void Suche_ohne_Treffer_ist_nur_eine_Information_und_nicht_moegliche_Suche_eine_Warnung()
    {
        Controller();
        var leer = Lauf(Suche, scan: "");
        Assert.Contains("Gefundene Bluetooth-Geräte: 0", leer.Ausgabe);
        Assert.Equal(Status.Info, leer.Status);

        var unmoeglich = Lauf(Suche, scan: "Failed to start discovery: org.bluez.Error.NotReady");
        Assert.Contains("SUCHE_NICHT_MOEGLICH", unmoeglich.Ausgabe);
        Assert.Equal(Status.Warnung, unmoeglich.Status);
    }

    [Fact]
    public void Suche_erkennt_einen_Adapter_der_waehrend_der_Suche_abstuerzt()
    {
        Controller();
        var vorher = "[ 1.0] Bluetooth: hci0: RTL: fw version 0xdfc6d922";
        var r = Lauf(Suche, dmesg: vorher, dmesgNachher: vorher + "\n[ 9.0] Bluetooth: hci0: command 0x2005 tx timeout\n[ 9.0] Bluetooth: hci0: Resetting usb device.",
            scan: "Failed to start discovery: org.bluez.Error.InProgress");
        Assert.Contains("SUCHE_ADAPTER_HING", r.Ausgabe);
        Assert.Contains("SUCHE_LAEUFT_SCHON", r.Ausgabe);
        Assert.DoesNotContain("SUCHE_NICHT_MOEGLICH", r.Ausgabe);       // InProgress heißt nicht „Controller ausgeschaltet“
        Assert.Equal(Status.Fehler, r.Status);
        Assert.True(Meldet(r.Ergebnis, "abgestürzt"));
    }

    [Fact]
    public void Suche_mit_schon_laufender_Suche_ohne_Absturz_ist_nur_eine_Information()
    {
        Controller();
        var r = Lauf(Suche, scan: "Failed to start discovery: org.bluez.Error.InProgress");
        Assert.Contains("SUCHE_LAEUFT_SCHON", r.Ausgabe);
        Assert.DoesNotContain("SUCHE_ADAPTER_HING", r.Ausgabe);
        Assert.Equal(Status.Info, r.Status);
        Assert.True(Meldet(r.Ergebnis, "andere Bluetooth-Suche"));
    }

    [Fact]
    public void Suche_gehoert_nicht_zur_Schnellpruefung_der_Adapter_schon()
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        Assert.False(module.Single(m => m.Id == Suche).Schnellpruefung);
        Assert.True(module.Single(m => m.Id == Adapter).Schnellpruefung);
    }

    [Fact]
    public void Windows_Regeln_bewerten_Geraetestatus()
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var s = module.Single(m => m.Id == Adapter).Schritte.Single(x => x.Os == "windows");
        SchrittErgebnis Bewerte(string aus) { var r = new SchrittErgebnis { Ausgabe = aus, ExitCode = 0 }; ModulAusfuehrer.Bewerten(s, r); return r; }

        Assert.Equal(Status.Ok, Bewerte("BT-Gerät: Intel(R) Wireless Bluetooth(R) (Status OK)\n").Status);
        Assert.Equal(Status.Warnung, Bewerte("BT-Gerät: Generic Bluetooth Radio (Status Error)\nBT_GERAET_FEHLER\n").Status);
        Assert.Equal(Status.Info, Bewerte("KEIN_ADAPTER_BT\n").Status);
    }
}
