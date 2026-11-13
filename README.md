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

## Bauen und Testen

Voraussetzung: .NET-SDK 10.

```
dotnet test
dotnet run --project src/Diagnostiker.Tui -- --schnell --protokolle <ordner>
```
