# Diagnostiker

[English](README.md) · [Deutsch](README.de.md) · **Nederlands**

Een opstartbare USB-diagnosestick voor pc's en laptops. Hij is ontstaan uit de behoeften van repair cafés en vrijwillige IT-hulp aan ouderen,
maar is bedoeld voor iedereen die moet uitzoeken waarom een computer problemen geeft. Je steekt hem in een computer die niet meer start, of in een computer waarop Windows of Linux draait, en krijgt een duidelijk,
begrijpelijk rapport over de schijf, het opstarten en het netwerk.

> **Status: vroege testversie.** De stick werkt en start op (BIOS en UEFI, Secure Boot blijft ingeschakeld) en heeft een programma met venster om hem te maken.
> Het programma om de stick te maken is er in het Engels, Duits en Nederlands (kies de taal bovenaan het venster). Het controleprogramma op de stick zelf
> is **voorlopig alleen in het Duits**; meer talen zijn gepland.

## Wat de stick controleert

- **Opstarten:** start de computer wel, firmwaremodus, opstartproblemen van Windows
- **Schijf:** SMART-gezondheid van alle schijven, overzicht, versleuteling (BitLocker/LUKS)
- **Netwerk:** adapters, bedrade en wifi-verbinding, Bluetooth, bereikbaarheid van internet
- **Systeem:** hardware-inventaris, recente meldingen in het systeemlogboek
- **Overstappen:** geschiktheid voor Windows 11, printers, hardware-overzicht

De stick **verandert de gecontroleerde computer niet**, tenzij je een uitdrukkelijke vraag bevestigt. Elke sessie schrijft een rapport
(`bericht.md` met ruwe gegevens) naar de stick.

Zo start hij:

- **Computer start niet:** kies de stick in het opstartmenu van de computer. Een klein live-systeem op basis van Debian start het programma vanzelf.
- **Computer draait:** open de stick en start `Start-Windows.cmd` (Windows) of `start-linux.sh` (Linux).

## Een stick maken

De stick wordt **alleen onder Linux** gemaakt (er is directe toegang tot het USB-station nodig). Het controleprogramma draait ook onder Windows, maar dan vanaf de kant-en-klare stick.

1. Download van de [nieuwste versie](https://github.com/w-engbers/diagnostiker/releases/latest) het bestand `diagnostiker-stick-manager-linux-x64.tar.gz`, pak het uit en start `diagnostiker-stick-gui`.
2. Het venster leidt je in vier stappen door het proces:
   1. Het downloadt het nieuwste pakket en **controleert de handtekening** en de controlesommen.
   2. Je kiest een USB-stick. Alleen USB-schijven van 4 tot 512 GiB die het systeem niet gebruikt, worden getoond.
   3. Ter bevestiging **typ je de naam van het apparaat in**. Alles op die stick wordt gewist.
   4. Het image wordt geschreven (één keer wordt om je wachtwoord gevraagd), teruggelezen en vergeleken, daarna kopieert het programma het controleprogramma en de controlemodules naar de datapartitie.

Je hebt nodig: een Linux-desktop, een USB-stick van **4 tot 512 GiB**, ongeveer 1,5 GiB vrije schijfruimte, een internetverbinding en de standaardprogramma's
`sfdisk` (util-linux), `exfatprogs`, `udisks2` en `pkexec`. De meeste desktopdistributies hebben ze al; ontbrekende kun je met de pakketbeheerder installeren.
De stick-manager zelf is niet ondertekend: wie zeker wil zijn, vergelijkt het bestand met `SHA256SUMS` uit dezelfde versie.
De pakketten die hij downloadt zijn wel ondertekend, en het programma controleert ze voordat er iets wordt geschreven.

*Opdrachtregel in plaats van venster* (vereist de .NET SDK 10):

```
git clone https://github.com/w-engbers/diagnostiker.git
cd diagnostiker
dotnet run --project src/Diagnostiker.Stick -- paket-erstellen
```

`paket-pruefen` controleert alleen het nieuwste pakket en verandert niets. Met `--quelle <map>` gebruik je een pakket uit een lokale map (bijvoorbeeld zonder internet); het venster begrijpt dezelfde optie.

## Releases en handtekeningen

Elke [release](https://github.com/w-engbers/diagnostiker/releases) bevat de stick-manager, het pakket (`boot.img.gz`, `daten.tar.gz`, `manifest.json`,
`manifest.json.sig`), de lijst met Debian-pakketten (`debian-pakete.txt`) en `SHA256SUMS`. Het manifest is ondertekend (ECDSA P-256), de bijbehorende openbare sleutel
zit in het programma en het programma weigert pakketten waarvan de handtekening of de controlesommen niet kloppen. Details: [docs/AUSLIEFERUNG.md](docs/AUSLIEFERUNG.md) (Duits).

## Broncode en licenties

Deze repository bevat de broncode van alles wat in de releasepakketten wordt meegeleverd. Het project valt onder de
**GNU General Public License, versie 3** ([LICENSE](LICENSE)).

Het live-systeem is gebaseerd op **Debian 13 "trixie"** en bevat veel pakketten met een eigen licentie (meestal GPL en vergelijkbare).
Elke release somt ze op in `debian-pakete.txt` (ook op de stick onder `werkzeuge/lizenzen/`); de broncode is beschikbaar in het Debian-archief
(`apt-get source <pakket>=<versie>`, voor oudere versies [snapshot.debian.org](https://snapshot.debian.org/)).
Het Windows-programma `smartctl` hoort bij smartmontools (GPL-2.0); licentietekst en broncode staan op de stick onder `werkzeuge/`.

## Voor ontwikkelaars

Architectuur, bouwinstructies, het formaat van de controlemodules en de tests staan in het Duits beschreven in [docs/](docs/)
(begin met [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md) en [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)). De tests draai je met `dotnet test`.
