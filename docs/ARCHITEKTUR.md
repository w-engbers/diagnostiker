# Diagnostiker – Architektur (Entwurf)

Stand: 13.11.2026. Entwurf vor der Umsetzung, offene Punkte stehen am Ende.

## 1. Ziel

Ein USB-Stick mit Prüfwerkzeugen für PCs und Notebooks unter Windows und Linux (Repair-Café, Seniorenhilfe).
Schwerpunkte der ersten Ausbaustufe: **Booten, Lesen der Platte, Netzwerkverbindung.**

Anforderungen:
- Der Stick bootet per **BIOS und UEFI**, **Secure Boot bleibt eingeschaltet** (kein Schlüsseleintrag).
- Bei laufendem System des Prüflings startet die Oberfläche direkt vom Stick (Windows und Linux).
- Zeichenorientierte Oberfläche, deutsch, ein Protokoll je Sitzung.
- **Der Stick kann jederzeit neu erstellt und aktualisiert werden**, auch die verwendeten Werkzeuge.
- Standardwerkzeuge vor Eigenbau, ein Bauweg, der auf jedem Linux-Rechner läuft und nachvollziehbar ist.
- Eine Veröffentlichung ist eine **Option**, kein Ziel. Das Repo wird aber von Anfang an so geführt, als wäre es öffentlich.

Festlegung: **Der Stick wird nur unter Linux erstellt und aktualisiert.** Unter Windows läuft nur das Prüfprogramm vom fertigen Stick.

## 2. Die vier Teile

| Teil | Inhalt | Änderungshäufigkeit |
|---|---|---|
| **Prüfprogramm** | Kern, Oberfläche, Prüfmodule (JSON) | oft |
| **Werkzeuge** | Fremdprogramme je Betriebssystem (smartctl, iperf3 …) | beliebig, durch den Hersteller |
| **Basis** | Live-System (Kernel, Firmware, Pakete) | selten |
| **Stick-Manager** | Erstellen, Aktualisieren, Prüfen eines Sticks | neu |

Die Teile haben getrennte Versionen. Ein Update eines Teils darf die anderen nicht berühren.

## 3. Stick-Layout

GPT mit drei Partitionen. Alles außer dem erstmaligen Anlegen besteht aus gewöhnlichen Dateien (FAT32 und exFAT).

| Partition | Dateisystem | Label | Inhalt |
|---|---|---|---|
| 1 (1 MiB) | – | `BIOSBOOT` | GRUB-Kern für Legacy-BIOS |
| 2 | FAT32 | `DIAGBOOT` | Live-System, signierte UEFI-Kette (shim, GRUB, Kernel) |
| 3 | exFAT | `DIAGDATEN` | Prüfprogramm (win-x64, linux-x64), Module, Werkzeuge, Protokolle, Starter, `stick.json` |

`stick.json` auf `DIAGDATEN` hält die Versionen aller vier Teile, die Prüfsummen und das Erstelldatum. Daran erkennt der
Stick-Manager, was zu aktualisieren ist.

**Aktualisieren = Dateien kopieren.** Der Ordner `protokolle/` wird nie angefasst. Nur das Erstanlegen braucht Rohzugriff auf das Laufwerk.

## 4. Basis (Live-System)

- **Debian 13 „trixie“**, gebaut mit `live-build`. Begründung: Microsoft-signierte shim-Kette ohne Schlüsseleintrag, Firmware-Pakete
  (`non-free-firmware`) redistributierbar, bewährtes, deklaratives Werkzeug.
- Kernel aus den **Backports**, falls ein neueres Gerät sonst nicht läuft (Prüfen, Entscheidung offen).
- Die Basis steckt hinter einer Schnittstelle: ein Ordner `basis/<name>/` mit Paketliste, Bootparametern, Hooks und einem Bauskript.
  Ein Wechsel auf eine andere Distribution (zum Beispiel Ubuntu) ersetzt nur diesen Ordner.
- **Der Bau läuft in einem Debian-Container** (Podman oder Docker, privilegiert), nicht auf dem Wirtsrechner. Dadurch ist er unabhängig von
  dessen Distribution und braucht keine VM. Der Wirt braucht nur den Container, `sgdisk`, `mkfs.*`, `qemu-img`.
- Das Prüfprogramm steckt **nicht** im Basis-Abbild, sondern liegt auf `DIAGDATEN` und wird von dort gestartet. Fehlt `DIAGDATEN`, gibt es eine
  eingebaute Rückfallkopie. So braucht ein Programmupdate kein neues Abbild.

## 5. Werkzeuge und Aktualisierung

Alle Fremdwerkzeuge stehen in **einer** Datei, `werkzeuge.lock.json`, im Repo:
Name, Betriebssystem, Version, Quelladresse, SHA-256, Lizenz, Pfad auf dem Stick.

Der Stick-Manager kann:
1. **Herunterladen** der Werkzeuge nach Lock-Datei, Prüfsumme prüfen, in einen Zwischenspeicher (Cache) ablegen.
2. **Auf neuere Versionen prüfen** (Quelle konfigurierbar) und die Lock-Datei fortschreiben, mit Vorschau der Änderungen.
3. **Lizenzliste erzeugen** (Grundlage für Quellenangebot und Hinweise, falls veröffentlicht wird).

Bevorzugt werden Werkzeuge aus dem Betriebssystem oder dem Paketmanager (Linux) bzw. aus offiziellen Veröffentlichungen der Hersteller
(Windows). Nichts wird ohne Prüfsumme eingebunden.

## 6. Stick-Manager

Textoberfläche, derselbe Aufbau wie das Prüfprogramm (.NET, Spectre.Console, geteilter Kern). Funktionen:

- **Stick erstellen:** Laufwerk wählen (Modell, Größe, Name), Systemplatten werden nicht angeboten, doppelte Bestätigung, dann Abbild
  schreiben und `DIAGDATEN` vergrößern. Vorlage: ein fertiges Abbild, geschrieben mit Standardwerkzeugen (`dd`/`sfdisk`).
- **Stick aktualisieren:** erkennt den eingesteckten Stick, zeigt je Teil „aktuell/veraltet“, aktualisiert einzeln oder alles. Rührt `protokolle/` nicht an.
- **Werkzeuge:** siehe Abschnitt 5.
- **Abbild bauen:** startet den Container-Bau der Basis, zeigt Fortschritt und Ergebnis.
- **Selbsttest:** Prüfsummen auf dem Stick gegen `stick.json`.

Root-Rechte nur dort, wo nötig (Schreiben des Laufwerks), über `sudo`/`pkexec`, nie für das ganze Programm.

## 7. Prüfprogramm (aus dem Prototyp übernommen)

Kern (`Diagnostiker.Kern`), Oberfläche (`Diagnostiker.Tui`), Prüfmodule als JSON-Manifeste (`module/`), Marker-Konvention für Bewertungsregeln,
ein Protokollordner je Sitzung (`session.json`, `bericht.md`, `roh/`). Siehe `README.md` und `docs/PRUEFMODULE.md` (folgt).

## 8. Tests

- **Einheitentests** für Kern und Bewertung (`dotnet test`).
- **Bootprüfung automatisiert** mit QEMU/KVM und OVMF (auch mit Secure-Boot-Variante), serielle Konsole: das Abbild muss bis ins Menü starten.
- **Echte Hardware** nach festem Plan (alt, neu, Secure Boot, WLAN, NVMe, RAID/VMD), Ergebnisse im Repo als Tabelle.

## 9. Umsetzungsreihenfolge

1. **Fundament:** Repo, SDK, Tests unter Linux grün, `.gitignore`, Prüfprogramm baut für beide Systeme.
2. **Basisbau im Container:** Abbild reproduzierbar, QEMU-Bootprüfung.
3. **Stick-Layout und `stick.json`,** Abbild schreiben, Aktualisieren per Kopie.
4. **Stick-Manager** (erstellen, aktualisieren, Werkzeuge).
5. **Prüfmodule** ausbauen (Startfähigkeit des Prüflings, SMART für alle Platten, WLAN, Bluetooth, Akku, RAM/CPU).
6. **Echte Hardware,** Secure-Boot-Prüfung, Windows-Seite am Stick.

## 10. Offene Punkte und Risiken

- **Secure Boot auf neuen Rechnern:** Die Zertifikate der Microsoft-„UEFI CA 2011“ laufen aus, neuere Geräte vertrauen teils nur noch der CA 2023.
  Prüfen, ob die im Debian-shim enthaltene Signatur beide abdeckt. Nur an echten Geräten zu klären.
- **Autostart im Live-System:** In der Prototyp-Kopie fehlte `root/.profile` (Punktdatei beim Übertragen verloren). Im Neubau ersetzen wir sie
  durch eine systemd-Einheit oder Live-Config-Hook, die nicht von einer Punktdatei abhängt.
- **Podman oder Docker** ist auf dem Entwicklungsrechner noch nicht installiert. Fallback: Debian-`live-build` direkt im Chroot.
- **Kernel-Aktualität** der Basis (Backports) und **Firmware** für die neueste Hardware.
- **Herunterfahren** des Live-Systems dauert im Prototyp rund 96 s, Ursache offen.
- **Einbindung von `DIAGDATEN` im Live-System:** Rückfallkopie und Reihenfolge noch zu entwerfen.
- **Ersatz für „Stock-ISO plus Datenpartition“** (Basis-Update nur durch Austausch einer offiziellen ISO): Idee, nicht geprüft. Kleiner Test, ob sich
  Autostart und Paketumfang so erreichen lassen.
- **Lizenzen** der Werkzeuge und der Debian-Firmware, Quellenangebot, falls veröffentlicht wird.
- **Windows-Seite:** unsignierte EXE (SmartScreen), nur am echten Stick zu prüfen.
