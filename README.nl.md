# Diagnostiker

[English](README.md) · [Deutsch](README.de.md) · **Nederlands**

Een opstartbare USB-diagnosestick voor pc's en laptops, bedoeld voor repair cafés en vrijwillige IT-hulp aan ouderen.
Je steekt hem in een computer die niet meer start, of in een computer waarop Windows of Linux draait, en krijgt een duidelijk,
begrijpelijk rapport over de schijf, het opstarten en het netwerk.

> **Status: vroege testversie.** De stick werkt en start op (BIOS en UEFI, Secure Boot blijft ingeschakeld). Het programma om de stick
> te maken draait echter nog op de opdrachtregel, en de interface van de stick is **voorlopig alleen in het Duits**. Een programma met
> venster en meer talen zijn gepland.

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

Je hebt nodig: een USB-stick van **4 tot 512 GiB** (alles erop wordt gewist), ongeveer 1,5 GiB vrije schijfruimte, een internetverbinding
en deze standaardprogramma's van Linux: `sfdisk` (util-linux), `exfatprogs`, `udisks2` en `pkexec`. De .NET SDK 10 is voorlopig nodig om het programma te starten.

```
git clone https://github.com/w-engbers/diagnostiker.git
cd diagnostiker
dotnet run --project src/Diagnostiker.Stick -- paket-erstellen
```

Het programma

1. downloadt het nieuwste releasepakket en **controleert de handtekening** en de controlesommen,
2. toont alleen USB-schijven die het systeem niet gebruikt en vraagt je ter bevestiging de **naam van het apparaat in te typen**,
3. schrijft het image (één keer wordt om je wachtwoord gevraagd), leest het terug en vergelijkt het,
4. kopieert het programma, de controlemodules en de hulpprogramma's naar de datapartitie.

`paket-pruefen` controleert alleen het nieuwste pakket en verandert niets. Met `--quelle <map>` gebruik je een pakket uit een lokale map.

## Releases en handtekeningen

Elke [release](https://github.com/w-engbers/diagnostiker/releases) bevat `boot.img.gz`, `daten.tar.gz`, `manifest.json` en
`manifest.json.sig`. Het manifest is ondertekend (ECDSA P-256), de bijbehorende openbare sleutel zit in het programma en het programma
weigert pakketten waarvan de handtekening of de controlesommen niet kloppen. Details: [docs/AUSLIEFERUNG.md](docs/AUSLIEFERUNG.md) (Duits).

## Broncode en licenties

Deze repository bevat de broncode van alles wat in de releasepakketten wordt meegeleverd. Het project valt onder de
**GNU General Public License, versie 3** ([LICENSE](LICENSE)).

Het live-systeem is gebaseerd op **Debian 13 "trixie"** en bevat veel pakketten met een eigen licentie (meestal GPL en vergelijkbare).
De broncode daarvan is beschikbaar in het Debian-archief (`deb-src`, voor oudere versies [snapshot.debian.org](https://snapshot.debian.org/)).
Het Windows-programma `smartctl` hoort bij smartmontools (GPL-2.0); licentietekst en broncode staan op de stick onder `werkzeuge/`.

## Voor ontwikkelaars

Architectuur, bouwinstructies, het formaat van de controlemodules en de tests staan in het Duits beschreven in [docs/](docs/)
(begin met [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md) en [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)). De tests draai je met `dotnet test`.
