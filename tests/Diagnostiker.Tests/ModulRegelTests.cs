using Diagnostiker.Kern;

namespace Diagnostiker.Tests;

/// <summary>Prüft die Bewertungsregeln der echten Manifeste gegen typische Ausgaben (am 13.11.2026 am Laptop mit
/// Legacy-BIOS und ohne Netzwerkverbindung beobachtet).</summary>
public class ModulRegelTests
{
    static Status Linux(string modulId, string schrittTitel, string ausgabe, int exitCode = 0)
    {
        var (module, _) = ModulLader.Laden(ModulLader.ModulVerzeichnisFinden()!);
        var schritt = module.Single(m => m.Id == modulId).Schritte.Single(s => s.Os == "linux" && s.Titel == schrittTitel);
        var ergebnis = new SchrittErgebnis { Ausgabe = ausgabe, ExitCode = exitCode };
        ModulAusfuehrer.Bewerten(schritt, ergebnis);
        return ergebnis.Status;
    }

    const string Efi = "UEFI-Boot-Einträge der Firmware";

    [Fact]
    public void Bios_Start_ist_bei_den_UEFI_Eintraegen_nur_eine_Information()
    {
        Assert.Equal(Status.Info, Linux("boot.firmware", Efi, "NICHT_UEFI\n"));
    }

    [Fact]
    public void Uefi_Start_mit_Eintraegen_ist_ok_und_ohne_Lesbarkeit_eine_Warnung()
    {
        Assert.Equal(Status.Ok, Linux("boot.firmware", Efi, "BootCurrent: 0001\nBootOrder: 0001\nBoot0001* debian\n"));
        Assert.Equal(Status.Warnung, Linux("boot.firmware", Efi, "efibootmgr: irgendein Fehler\n", exitCode: 2));
        Assert.Equal(Status.Warnung, Linux("boot.firmware", Efi, "No BootOrder is set; firmware will attempt recovery\n"));
    }

    [Fact]
    public void Kein_Adapter_verbunden_ist_eine_Warnung_mit_Hinweis_aber_kein_Fehler()
    {
        const string ausgabe = "ADAPTER|enp2s0|operstate=down|treiber=tg3\nADAPTER|wlp3s0|operstate=down|treiber=ath9k\nNIRGENDS_VERBUNDEN\n";
        Assert.Equal(Status.Warnung, Linux("netz.adapter", "Adapter", ausgabe));
    }

    [Fact]
    public void Ein_verbundener_Adapter_genuegt_fuer_Ok_und_fehlender_Adapter_ist_ein_Fehler()
    {
        Assert.Equal(Status.Ok, Linux("netz.adapter", "Adapter",
            "ADAPTER|enp2s0|operstate=down|treiber=tg3\nADAPTER|wlp3s0|operstate=up|treiber=ath9k\n"));
        Assert.Equal(Status.Fehler, Linux("netz.adapter", "Adapter", "KEIN_ADAPTER\n"));
    }

    [Theory]
    [InlineData("IP-Adresse und Router")]
    [InlineData("Internet per IP-Adresse (ohne DNS)")]
    [InlineData("Namensauflösung (DNS)")]
    public void Ohne_verbundenen_Adapter_werden_die_Verbindungsschritte_uebersprungen(string schritt)
    {
        Assert.Equal(Status.Uebersprungen, Linux("netz.verbindung", schritt, "KEIN_LINK\n"));
    }

    [Fact]
    public void Verbundener_Adapter_ohne_Router_bleibt_ein_Fehler()
    {
        Assert.Equal(Status.Fehler, Linux("netz.verbindung", "IP-Adresse und Router", "KEIN_GATEWAY\n"));
        Assert.Equal(Status.Fehler, Linux("netz.verbindung", "Internet per IP-Adresse (ohne DNS)", "INTERNET_IP_FEHLT\n"));
        Assert.Equal(Status.Fehler, Linux("netz.verbindung", "Namensauflösung (DNS)", "DNS_FEHLER\n"));
    }
}
