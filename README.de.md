# Diagnostiker

[English](README.md) · **Deutsch** · [Nederlands](README.nl.md)

Ein startfähiger USB-Prüfstick für PCs und Notebooks, gedacht für Repair-Cafés und die ehrenamtliche IT-Hilfe für Senioren.
Er wird in einen Rechner gesteckt, der nicht mehr startet, oder in einen, auf dem Windows oder Linux läuft, und liefert einen
klaren, verständlichen Bericht zu Platte, Startvorgang und Netzwerk.

> **Stand: frühe Testversion.** Der Stick funktioniert und startet (BIOS und UEFI, Secure Boot bleibt eingeschaltet). Das Werkzeug zum
> Erstellen läuft aber noch in der Eingabezeile, und die Oberfläche des Sticks gibt es **vorerst nur auf Deutsch**. Ein Werkzeug mit Fenster
> und weitere Sprachen sind geplant.

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

Nötig sind: ein USB-Stick mit **4 bis 512 GiB** (alles darauf wird gelöscht), etwa 1,5 GiB freier Platz, eine Internetverbindung
und diese Standardwerkzeuge von Linux: `sfdisk` (util-linux), `exfatprogs`, `udisks2` und `pkexec`. Das .NET-SDK 10 wird vorerst zum Starten des Erstellwerkzeugs gebraucht.

```
git clone https://github.com/w-engbers/diagnostiker.git
cd diagnostiker
dotnet run --project src/Diagnostiker.Stick -- paket-erstellen
```

Das Programm

1. lädt das neueste Release-Paket und **prüft die Signatur** und die Prüfsummen,
2. zeigt nur USB-Laufwerke, die das System nicht benutzt, und verlangt zur Bestätigung das **Eintippen des Gerätenamens**,
3. schreibt das Abbild (einmal wird nach dem Passwort gefragt), liest es zurück und vergleicht es,
4. kopiert Programm, Prüfmodule und Werkzeuge auf die Datenpartition.

`paket-pruefen` prüft nur das neueste Paket und ändert nichts. Mit `--quelle <Ordner>` lässt sich ein Paket aus einem lokalen Ordner verwenden.

## Releases und Signaturen

Jedes [Release](https://github.com/w-engbers/diagnostiker/releases) enthält `boot.img.gz`, `daten.tar.gz`, `manifest.json` und
`manifest.json.sig`. Das Manifest ist signiert (ECDSA P-256), der passende öffentliche Schlüssel steckt im Programm, und das Programm
lehnt Pakete ab, deren Signatur oder Prüfsummen nicht stimmen. Einzelheiten: [docs/AUSLIEFERUNG.md](docs/AUSLIEFERUNG.md).

## Quelltext und Lizenzen

Dieses Repository enthält den Quelltext von allem, was in den Release-Paketen ausgeliefert wird. Das Projekt steht unter der
**GNU General Public License, Version 3** ([LICENSE](LICENSE)).

Das Live-System basiert auf **Debian 13 „trixie“** und enthält viele Pakete mit eigenen Lizenzen (überwiegend GPL und ähnliche).
Deren Quelltext steht im Debian-Archiv bereit (`deb-src`, für ältere Versionen [snapshot.debian.org](https://snapshot.debian.org/)).
Das Windows-Programm `smartctl` gehört zu smartmontools (GPL-2.0), Lizenztext und Quelltext liegen auf dem Stick unter `werkzeuge/`.

## Für Entwickler

Architektur, Bauanleitung, das Format der Prüfmodule und die Tests sind auf Deutsch in [docs/](docs/) beschrieben
(Einstieg: [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md) und [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)). Die Tests laufen mit `dotnet test`.
