# Diagnostiker

[English](README.md) · **Deutsch** · [Nederlands](README.nl.md)

Ein startfähiger USB-Prüfstick für PCs und Notebooks. Er ist aus den Bedürfnissen von Repair-Cafés und der ehrenamtlichen IT-Hilfe für Senioren
entstanden, soll aber jedem helfen, der herausfinden muss, warum ein Rechner Probleme macht. Er wird in einen Rechner gesteckt, der nicht mehr startet, oder in einen, auf dem Windows oder Linux läuft, und liefert einen
klaren, verständlichen Bericht zu Platte, Startvorgang und Netzwerk.

> **Stand: frühe Testversion.** Der Stick funktioniert und startet (BIOS und UEFI, Secure Boot bleibt eingeschaltet) und hat ein Erstellwerkzeug mit Fenster.
> Das Erstellwerkzeug gibt es auf Englisch, Deutsch und Niederländisch (die Sprache wählt man oben im Fenster). Das Prüfprogramm auf dem Stick selbst
> gibt es **vorerst nur auf Deutsch**, weitere Sprachen sind geplant.

## Was der Stick prüft

- **Start:** Startet der Rechner überhaupt, Firmware-Modus, Startprobleme von Windows
- **Platte:** SMART-Gesundheit aller Laufwerke, Übersicht, Verschlüsselung (BitLocker/LUKS)
- **Netzwerk:** Adapter, Kabel- und WLAN-Verbindung, Bluetooth, Erreichbarkeit des Internets
- **System:** Hardware-Inventar, jüngste Einträge im Systemprotokoll
- **Umstieg:** Eignung für Windows 11, Drucker, Hardware-Übersicht

Der Stick **verändert den geprüften Rechner nicht**, es sei denn, eine ausdrückliche Rückfrage wird bestätigt. Jede Sitzung schreibt einen Bericht
(`bericht.md` samt Rohdaten) auf den Stick.

So startet er:

- **Rechner startet nicht:** Den Stick im Boot-Menü des Rechners auswählen. Ein kleines Live-System auf Debian-Basis startet das Programm von selbst.
- **Rechner läuft:** Den Stick öffnen und `Start-Windows.cmd` (Windows) oder `start-linux.sh` (Linux) starten.

## Einen Stick erstellen

Der Stick wird **nur unter Linux** erstellt (es wird Rohzugriff auf das USB-Laufwerk gebraucht). Das Prüfprogramm läuft auch unter Windows, aber vom fertigen Stick.

1. Von der [neuesten Version](https://github.com/w-engbers/diagnostiker/releases/latest) die Datei `diagnostiker-stick-manager-linux-x64.tar.gz` laden, entpacken und `diagnostiker-stick-gui` starten.
2. Das Fenster führt in vier Schritten durch den Vorgang:
   1. Es lädt das neueste Paket und **prüft die Signatur** und die Prüfsummen.
   2. Man wählt einen USB-Stick. Angeboten werden nur USB-Laufwerke von 4 bis 512 GiB, die das System nicht benutzt.
   3. Zur Bestätigung **tippt man den Gerätenamen ein**. Alles auf diesem Stick wird gelöscht.
   4. Das Abbild wird geschrieben (einmal wird nach dem Passwort gefragt), zurückgelesen und verglichen, danach kopiert das Programm Prüfprogramm und Prüfmodule auf die Datenpartition.

Nötig sind: ein Linux-Desktop, ein USB-Stick mit **4 bis 512 GiB**, etwa 1,5 GiB freier Platz, eine Internetverbindung und die Standardwerkzeuge
`sfdisk` (util-linux), `exfatprogs`, `udisks2` und `pkexec`. Die meisten Desktop-Distributionen haben sie, fehlende lassen sich mit der Paketverwaltung nachinstallieren.
Der Stick-Manager selbst ist nicht signiert: Wer sicher gehen will, vergleicht die Datei mit `SHA256SUMS` aus derselben Version.
Die Pakete, die er lädt, sind signiert, und das Programm prüft sie, bevor etwas geschrieben wird.

*Eingabezeile statt Fenster* (braucht das .NET-SDK 10):

```
git clone https://github.com/w-engbers/diagnostiker.git
cd diagnostiker
dotnet run --project src/Diagnostiker.Stick -- paket-erstellen
```

`paket-pruefen` prüft nur das neueste Paket und ändert nichts. Mit `--quelle <Ordner>` lässt sich ein Paket aus einem lokalen Ordner verwenden (zum Beispiel ohne Internet); das Fenster versteht dieselbe Angabe.

## Releases und Signaturen

Jedes [Release](https://github.com/w-engbers/diagnostiker/releases) enthält den Stick-Manager, das Paket (`boot.img.gz`, `daten.tar.gz`, `manifest.json`,
`manifest.json.sig`), die Liste der Debian-Pakete (`debian-pakete.txt`) und `SHA256SUMS`. Das Manifest ist signiert (ECDSA P-256), der passende öffentliche Schlüssel
steckt im Programm, und das Programm lehnt Pakete ab, deren Signatur oder Prüfsummen nicht stimmen. Einzelheiten: [docs/AUSLIEFERUNG.md](docs/AUSLIEFERUNG.md).

## Quelltext und Lizenzen

Dieses Repository enthält den Quelltext von allem, was in den Release-Paketen ausgeliefert wird. Das Projekt steht unter der
**GNU General Public License, Version 3** ([LICENSE](LICENSE)).

Das Live-System basiert auf **Debian 13 „trixie“** und enthält viele Pakete mit eigenen Lizenzen (überwiegend GPL und ähnliche).
Jedes Release listet sie in `debian-pakete.txt` auf (auch auf dem Stick unter `werkzeuge/lizenzen/`); ihr Quelltext steht im Debian-Archiv bereit
(`apt-get source <Paket>=<Version>`, für ältere Versionen [snapshot.debian.org](https://snapshot.debian.org/)).
Das Windows-Programm `smartctl` gehört zu smartmontools (GPL-2.0), Lizenztext und Quelltext liegen auf dem Stick unter `werkzeuge/`.

## Für Entwickler

Architektur, Bauanleitung, das Format der Prüfmodule und die Tests sind auf Deutsch in [docs/](docs/) beschrieben
(Einstieg: [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md) und [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)). Die Tests laufen mit `dotnet test`.
