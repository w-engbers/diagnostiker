# Diagnostiker – Auslieferung (Pakete, Signatur, Release)

Stand 15.11.2026. Herausgeberseite und Anwenderseite (Herunterladen, Prüfen, Schreiben ohne Docker) sind gebaut und getestet; ein Test an einem echten Stick steht noch aus. Das Fenster-Frontend folgt.

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

## Anwender: Stick aus dem Paket erstellen

```
dotnet run --project src/Diagnostiker.Stick -- paket-pruefen [--quelle <Ordner|https-Adresse>]    # nur lesen: Version und Signatur
dotnet run --project src/Diagnostiker.Stick -- paket-erstellen [--quelle <Ordner|https-Adresse>]  # auch im Menü: „Stick erstellen (fertiges Paket laden)“
```

Ohne `--quelle` wird die neueste Version aus `github.com/w-engbers/diagnostiker` (GitHub Releases) geladen. Nötig sind nur
Linux-Standardwerkzeuge (util-linux mit `sfdisk`, `exfatprogs`, `udisks2`, `pkexec`), kein Docker.

Ablauf (`Paket/PaketHolen.cs`, `PaketStickErsteller.cs`, `stick/paket-schreiben.sh`):

1. **Holen und prüfen:** Manifest und Signatur lesen, Signatur gegen die vertrauten Schlüssel prüfen (eingebauter Schlüssel plus
   die in `~/.config/diagnostiker/vertrauen.txt` gespeicherten aus Schlüsselwechseln). Bei „neueste Version“ wird danach auf genau diese
   Version festgelegt. Dateien kommen in `~/.cache/diagnostiker/pakete/<version>/` (Fortsetzen mit HTTP-Range, nur HTTPS), jede Datei wird
   gegen Größe und SHA-256 des Manifests geprüft, falsche werden gelöscht. Ältere Versionen im Zwischenspeicher werden entfernt.
2. **Laufwerk wählen und bestätigen:** gleiche Sicherungen wie beim Docker-Weg (`ZielVorbereitung.cs`): nur USB, 4 bis 512 GiB, nichts vom System
   benutzt, Gerätename eintippen, vor dem Schreiben erneute Prüfung, Aushängen.
3. **Schreiben mit Administratorrechten:** ein eingebettetes Skript (`paket-schreiben.sh`) läuft über `pkexec /bin/bash -c <Skript> bash <Argumente>`
   (nicht aus einer vom Benutzer beschreibbaren Datei, keine Umgebungsvariablen, die pkexec verwirft). Es prüft alle Regeln noch einmal selbst
   (Gerätename, USB, Größe, nicht eingehängt, kein Verweis als Abbild, Prüfsumme des Abbilds), schreibt das Abbild (`gzip -dc | dd`), liest zurück und
   vergleicht (`cmp`, entdeckt defekte Sticks), verlegt die GPT-Sicherungskopie ans Ende (`sfdisk --relocate`), legt Partition 3 an, würfelt GUIDs neu
   und formatiert `DIAGDATEN` mit `mkfs.exfat`. Ausgabe der letzten Zeile: `SCHREIBEN_FERTIG <partition>`.
4. **Füllen ohne Root:** `udisksctl mount`, `daten.tar.gz` entpacken (eigene Schleife: keine Unix-Rechte auf exFAT, nur Dateien und Ordner, Pfade
   außerhalb des Ziels und Verweise werden abgelehnt), prüfen, dass `stick.json` da ist, `sync`, aushängen.

Hinweis zu `TarFile.CreateFromDirectory`: Es lässt versteckte Dateien (Punkt-Dateien) stillschweigend weg. Der Paketbauer packt deshalb mit eigener Schleife.

Tests: `dotnet test` (Attrappen für Laufwerke, Befehle und Netz, `PaketAnwenderTests.cs`) und `tests/stick/paket-test.sh <paketordner>`
(echter Helfer in einem Container mit nur Anwenderwerkzeugen, Loop-Gerät statt Stick, Verweigerungen, Tabelle, GUIDs, Start in QEMU mit BIOS und
UEFI/Secure Boot).

## Offen

- Test mit einem echten Stick (nur Werner kann ihn ausführen), erstes Release auf GitHub.
- Sicherung gegen das Einspielen einer älteren, gültig signierten Version (Rückfallangriff) ist nicht eingebaut.
- Aktualisieren eines vorhandenen Sticks aus einem Paket (heute nur Neuerstellen; der Docker-Weg `aktualisieren` bleibt).
- Fenster-Frontend (Avalonia, nur Linux) und Sprachgrundlage (Texte statt deutscher Textstücke in den Bewertungsregeln).
- Entscheidung zur Sprache der Oberfläche (nur Deutsch oder Englisch dazu).
