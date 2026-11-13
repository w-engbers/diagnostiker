# Diagnostiker

USB-Prüfstick für PCs und Notebooks unter Windows und Linux (Repair-Café, Seniorenhilfe). Schwerpunkt zuerst: Booten, Lesen der Platte,
Netzwerkverbindung. Der Stick wird unter Linux erstellt und lässt sich jederzeit aktualisieren.

**Stand:** Entwurf und Fundament. Das Prüfprogramm stammt aus einem Prototyp, der Stick-Manager und der Basisbau sind noch nicht gebaut.
Architektur und Reihenfolge: [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md).

## Aufbau

```
src/Diagnostiker.Kern   Modul-Lader, Prozess-Ausführung, Bewertung, Sitzungsprotokoll
src/Diagnostiker.Tui    Textoberfläche (Spectre.Console)
module/<gruppe>/*.json  Prüfmodule als Manifeste (Befehl je Betriebssystem, Parameter, Bewertungsregeln)
tests/                  Einheitentests
docs/                   Architektur und weitere Dokumentation
```

## Prüfmodul-Manifest (Kurzreferenz)

- `schritte[]` mit `os` (`windows`/`linux`/leer), `befehl`, `argumente`, `admin`, `timeoutSekunden`, `kodierung`, `hinweis`.
- Platzhalter: `{werkzeuge}` (Werkzeugordner des laufenden Systems) und `{parametername}` (Dialogfragen aus `parameter[]`).
- `regeln[]`: `typ` = `enthaelt` | `regex` | `exitcode` | `exitcodeNicht`; `status` = `Ok`/`Info`/`Warnung`/`Fehler`; `meldung` = Klartext.
  Der schlimmste Status aller zutreffenden Regeln gewinnt, ohne Regel gilt `Info`.
- `zeigeAusgabe: true` übernimmt die Ausgabezeilen ins Ergebnis.
- Konvention: Skripte geben **Marker** aus (`GATEWAY_STUMM`, `DNS_FEHLER` …), die Regeln prüfen die Marker statt sprachabhängiger Ausgabe.

## Aufbau des Sticks

```
basis/debian/   Live-System (Debian 13, live-build im Docker-Container): bauen.sh, Konfiguration
stick/          Stick-Abbild: bauen.sh (Datenpartition zusammenstellen), stick-erstellen.sh (läuft im Container), vorlage/ (Starter)
tests/boot/     Bootprüfung in QEMU/KVM (BIOS und UEFI mit Secure Boot)
```

Ablauf (Voraussetzung: Docker, .NET-SDK 10, für die Bootprüfung `qemu-system-x86` und `ovmf`):

```
basis/debian/bauen.sh                                      # Live-ISO nach build/out/
stick/bauen.sh [GiB]                                       # Stick-Abbild nach build/stick/ (schreibt auf kein Laufwerk)
tests/boot/boot-test.sh build/stick/diagnostiker-stick.img uefi-sb
```

## Stick-Manager

Textoberfläche zum Erstellen des Sticks (nur unter Linux, Docker und .NET-SDK 10 nötig):

```
dotnet run --project src/Diagnostiker.Stick          # Menü: Laufwerke ansehen, Stick erstellen, Abbild bauen
dotnet run --project src/Diagnostiker.Stick -- laufwerke   # nur lesend: Laufwerke und ihre Eignung
```

Angeboten werden nur USB-Laufwerke zwischen 4 und 512 GiB, die das System nicht benutzt. Vor dem Schreiben muss der Gerätename
eingetippt werden, vor jedem Schritt wird erneut geprüft, und `stick/stick-erstellen.sh` prüft das Ziel im Container noch einmal selbst.
Tests: `tests/stick/laufwerk-test.sh` (Loop-Gerät statt echtem Stick) und `dotnet test`.

## Fremdwerkzeuge (Windows)

`werkzeuge/werkzeuge.lock.json` legt fest, welche Fremdprogramme der Stick mitbringt (zurzeit `smartctl.exe` aus smartmontools 7.5, GPL-2.0).
`werkzeuge/holen.sh` lädt sie, prüft GPG-Signatur und feste SHA-256-Summen und legt sie unter `cache/werkzeuge/` ab; `stick/staging.sh` ruft es auf.
Auf dem Stick liegen sie unter `werkzeuge/win-x64/`, dazu Lizenztext und Quelltext unter `werkzeuge/lizenzen/` und `werkzeuge/quellen/`.
Neue Versionen werden nie automatisch übernommen: neuen Eintrag mit geprüften Summen in die Sperrdatei schreiben.

## Bauen und Testen

Voraussetzung: .NET-SDK 10.

```
dotnet test
dotnet run --project src/Diagnostiker.Tui -- --schnell --protokolle <ordner>
```
