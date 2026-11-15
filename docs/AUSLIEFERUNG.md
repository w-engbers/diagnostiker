# Diagnostiker – Auslieferung (Pakete, Signatur, Release)

Stand 15.11.2026. Herausgeberseite fertig, Anwenderseite (Herunterladen, Prüfen, Schreiben ohne Docker) noch nicht gebaut.

## Ziel

Ausgeliefert wird ein **Paket**, aus dem ein Frontend unter Linux einen Stick erstellt oder aktualisiert, ohne Docker und ohne
Bauumgebung. Die Pakete liegen als Release-Dateien im öffentlichen GitHub-Repo `w-engbers/diagnostiker` (GitHub erlaubt je Datei
höchstens 2 GiB, das Boot-Abbild hat gepackt etwa 1,2 GiB). Lizenz: GPL-3.0 (`LICENSE`).

## Paketformat (Version 1)

| Datei | Inhalt |
|-------|--------|
| `manifest.json` | Format, Version, Erstellzeit, Größe der Bootpartition (`BootMib`), je Datei: Name, Art (`boot`/`daten`), Größe, SHA-256, entpackte Größe, optional `naechsterSchluessel` |
| `manifest.json.sig` | Signatur über die gesamte `manifest.json`: ECDSA P-256 mit SHA-256, Base64, IEEE P1363 (r‖s, 64 Bytes) |
| `boot.img.gz` | Rohabbild von Sektor 0 bis zum Ende von DIAGBOOT (GPT mit BIOS-Boot-Partition und FAT32-Live-System), GPT-Sicherungskopie entfernt |
| `daten.tar.gz` | Inhalt von DIAGDATEN (Programm, Module, Werkzeuge, Startskripte, `LIES-MICH.txt`, `stick.json`) |

Signatur und Gzip stammen aus der .NET-Standardbibliothek, beim Anwender wird kein Zusatzwerkzeug gebraucht. Das Manifest wird als Ganzes
signiert, die Dateien sind über ihre SHA-256-Summen im Manifest gebunden. Quelltext: `src/Diagnostiker.StickKern/Paket/`
(`Manifest`, `Signatur`, `PaketBauer`, `PaketPruefer`), Tests in `tests/Diagnostiker.Tests/PaketTests.cs`.

## Schlüssel und Vertrauen

- Der **öffentliche** Schlüssel ist ins Programm eingebettet (`Paket/oeffentlicher-schluessel.txt`).
- Der **private** Schlüssel liegt nur auf dem Rechner des Herausgebers: `~/.config/diagnostiker-release/signatur.key` (Rechte 600).
  Er gehört nie ins Repo und nie in Chats oder Tickets. **Eine Kopie offline ablegen**, ohne sie sind keine neuen Pakete signierbar.
- **Schlüsselwechsel:** Nennt ein gültig signiertes Manifest einen `naechsterSchluessel`, vertraut das Programm danach auch diesem.
  Ein ungültiger Nachfolgeschlüssel wird abgelehnt. So lässt sich der Schlüssel erneuern, ohne alle Programme neu zu verteilen.
- Signieren läuft im Release-Skript **ohne Rückfrage**.

## Herausgeber: Paket bauen und veröffentlichen

```
release/bauen.sh --version 2026.11.15                      # nur bauen: build/release/<version>/
release/bauen.sh --version 2026.11.15 --veroeffentlichen w-engbers/diagnostiker
```

Schritte: (1) `stick/staging.sh` stellt den Inhalt von DIAGDATEN zusammen, (2) `stick/boot-abbild-bauen.sh` baut im Container das
Boot-Abbild aus dem Live-ISO, (3) `paket-bauen` packt und signiert, (4) optional `gh release create v<version>`.
Der Schlüsselpfad lässt sich mit `DIAGNOSTIKER_SCHLUESSEL` ändern. Dauer etwa 76 s (ohne ISO-Bau).

Einzelbefehle des Stick-Managers: `dotnet run --project src/Diagnostiker.Stick -- schluessel-erzeugen <datei>` (verweigert das
Überschreiben) und `… -- paket-bauen …`.

## Stick aus dem Boot-Abbild (belegt, Anwenderseite noch nicht gebaut)

Das Abbild wird auf das gesamte Laufwerk geschrieben, danach: GPT-Sicherungskopie ans Laufwerksende verlegen, Partition 3 (exFAT,
`DIAGDATEN`) über den Rest anlegen, GUIDs neu würfeln, `mkfs.exfat`. exFAT lässt sich nicht vergrößern, daher wird DIAGDATEN immer
frisch in der echten Größe angelegt. Getestet: Start in QEMU mit BIOS und mit UEFI und Secure Boot (`tests/boot/boot-test.sh`).

Geplant für das Frontend: Herunterladen, Signatur und Summen prüfen, ein kleiner Helfer mit Administratorrechten über `pkexec`
(wiederholt dieselben Sicherheitsregeln wie `stick-erstellen.sh`: nur USB, 4 bis 512 GiB, nicht eingehängt, nicht das Systemlaufwerk),
danach ohne Root `udisksctl mount` und Entpacken von `daten.tar.gz`.

## Offen

- Anwenderseite (siehe oben), Ende-zu-Ende-Test mit Loop-Gerät und QEMU, Test am echten Stick.
- Fenster-Frontend (Avalonia, nur Linux) und Sprachgrundlage (Texte statt deutscher Textstücke in den Bewertungsregeln).
- Entscheidung zur Sprache der Oberfläche (nur Deutsch oder Englisch dazu).
