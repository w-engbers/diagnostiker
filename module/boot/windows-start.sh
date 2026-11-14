#!/bin/sh
# Sucht NUR LESEND nach Spuren, warum eine Windows-Installation des Rechners nicht startet (Linux-Live-System, Root nötig).
# Gelesen werden Dateien der Windows-Partition, die ohne das Windows-Ereignisprotokoll Aufschluss geben:
#   Startreparatur (SrtTrail.txt), Absturzabbilder (Minidump, MEMORY.DMP: Stoppcode), ausstehende Updates (pending.xml),
#   Update- und Setup-Protokolle (CBS.log, setuperr.log), Startprotokoll (ntbtlog.txt), der freie Platz und die Fehler im
#   Windows-Ereignisprotokoll System.evtx (mit evtxexport aus libevtx-utils).
# BitLocker-Laufwerke werden mit dem Wiederherstellungsschlüssel (Umgebungsvariable DIAGNOSTIKER_PARAM_SCHLUESSEL) nur lesend
# geöffnet; der Schlüssel erscheint in keiner Ausgabe und wird nicht gespeichert, die Zuordnung wird danach wieder geschlossen.
# Ausgabe: Zeilen mit [OK], [INFO], [WARNUNG] oder [FEHLER]; die Regeln des Manifests werten diese Marken aus.
#
# Testhilfen: DIAGNOSTIKER_EINHAENGEN=<Ordner> verwendet <Ordner>/<Gerätename> statt zu mounten (auch für /dev/mapper/…).
LC_ALL=C
export LC_ALL

NF=0     # Zahl der Fehler und Warnungen
ok()   { echo "[OK] $*"; }
info() { echo "[INFO] $*"; }
warn() { echo "[WARNUNG] $*"; NF=$((NF+1)); }
fehl() { echo "[FEHLER] $*"; NF=$((NF+1)); }

if [ "$(id -u)" != 0 ] && [ -z "$DIAGNOSTIKER_EINHAENGEN" ]; then echo KEINE_RECHTE; exit 0; fi

. "$(dirname "$0")/../lib/windows.sh"

# Stoppcode eines Absturzabbilds (64-Bit-Kopf "PAGEDU64": Stoppcode als 32-Bit-Zahl bei Byte 56)
stoppcode() {
    [ "$(head -c 8 "$1" 2>/dev/null)" = "PAGEDU64" ] || return 1
    z=$(od -An -t u4 -j 56 -N 4 "$1" 2>/dev/null | tr -d ' ')
    [ -n "$z" ] || return 1
    printf '0x%X' "$z"
}
stopp_name() {
    case "$1" in
        0x7B)  echo "INACCESSIBLE_BOOT_DEVICE|Der Startlaufwerk-Treiber oder -Controller fehlt oder ist falsch eingestellt (SATA-Modus AHCI/RAID im BIOS geändert?), oder die Platte ist defekt." ;;
        0xEF)  echo "CRITICAL_PROCESS_DIED|Ein wichtiger Windows-Prozess ist abgestürzt (oft beschädigte Systemdateien oder ein fehlerhaftes Update)." ;;
        0x50)  echo "PAGE_FAULT_IN_NONPAGED_AREA|Ungültiger Speicherzugriff (Arbeitsspeicher, Treiber oder Antivirus)." ;;
        0x1E)  echo "KMODE_EXCEPTION_NOT_HANDLED|Ein Treiber hat einen Ausnahmefehler ausgelöst." ;;
        0x3B)  echo "SYSTEM_SERVICE_EXCEPTION|Fehler beim Aufruf eines Systemdienstes (oft Treiber)." ;;
        0xA)   echo "IRQL_NOT_LESS_OR_EQUAL|Fehlerhafter Speicherzugriff durch einen Treiber oder defekten Arbeitsspeicher." ;;
        0xD1)  echo "DRIVER_IRQL_NOT_LESS_OR_EQUAL|Fehlerhafter Speicherzugriff durch einen Treiber." ;;
        0x1A)  echo "MEMORY_MANAGEMENT|Fehler in der Speicherverwaltung (Arbeitsspeicher prüfen)." ;;
        0x24)  echo "NTFS_FILE_SYSTEM|Dateisystemfehler auf einer NTFS-Platte (Platte und Dateisystem prüfen)." ;;
        0x7A)  echo "KERNEL_DATA_INPAGE_ERROR|Daten ließen sich nicht von der Platte laden (Platte, Kabel oder Controller)." ;;
        0x7E)  echo "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED|Ein Systemthread (meist ein Treiber) ist abgestürzt." ;;
        0x7F)  echo "UNEXPECTED_KERNEL_MODE_TRAP|Prozessorfehler oder defekter Arbeitsspeicher, auch Überhitzung." ;;
        0x9F)  echo "DRIVER_POWER_STATE_FAILURE|Ein Treiber hat beim Energiesparen versagt (Ruhezustand, Standby)." ;;
        0xC2)  echo "BAD_POOL_CALLER|Ein Treiber hat den Speicher falsch benutzt." ;;
        0xF4)  echo "CRITICAL_OBJECT_TERMINATION|Ein wichtiger Prozess oder Thread wurde beendet, oft durch einen Plattenfehler." ;;
        0x124) echo "WHEA_UNCORRECTABLE_ERROR|Nicht korrigierbarer Hardwarefehler (Prozessor, Arbeitsspeicher, PCIe, Überhitzung)." ;;
        0x133) echo "DPC_WATCHDOG_VIOLATION|Ein Treiber hat zu lange gebraucht (oft SSD oder Treiber)." ;;
        0x139) echo "KERNEL_SECURITY_CHECK_FAILURE|Beschädigte Datenstruktur im Kernel (Treiber oder Arbeitsspeicher)." ;;
        *)     echo "unbekannt|Stoppcode nachschlagen (Microsoft: „Bug Check Code Reference“)." ;;
    esac
}

# Fehler im Ereignisprotokoll System: Stand der letzten 14 Tage VOR dem letzten Eintrag im Protokoll (die Uhr des Prüfsystems
# stimmt nicht unbedingt). Gelesen wird die XML-Ausgabe von evtxexport; je Ereignis zählen Zeit, Stufe, Quelle, Kennung, erster Datenwert.
ereignisse() { # Ordner System32, Gerät
    evtx=$(pfad_ci "$1" winevt logs system.evtx) || { info "Kein Ereignisprotokoll System.evtx auf $2 gefunden."; return 0; }
    if ! command -v evtxexport >/dev/null 2>&1; then info "Ereignisprotokoll nicht ausgewertet: evtxexport (libevtx-utils) fehlt im Live-System."; return 0; fi
    evtxexport -f xml "$evtx" 2>/dev/null | awk '
        /<Provider Name=/ { match($0, /Name="[^"]*"/); prov = substr($0, RSTART + 6, RLENGTH - 7) }
        /<EventID/        { x = $0; sub(/.*<EventID[^>]*>/, "", x); sub(/<\/EventID>.*/, "", x); id = x }
        /<Level>/         { x = $0; sub(/.*<Level>/, "", x); sub(/<\/Level>.*/, "", x); lvl = x }
        /<TimeCreated/    { match($0, /SystemTime="[^"]*"/); t = substr($0, RSTART + 12, RLENGTH - 13) }
        /<Data[ >]/ && erster == "" { x = $0; sub(/<Data[^>]*>/, "", x); sub(/<\/Data>.*/, "", x); if (x != "") erster = x }
        /<\/Event>/       { if (t != "") { gsub(/\t/, " ", erster); print t "\t" lvl "\t" prov "\t" id "\t" substr(erster, 1, 160) } prov = id = lvl = t = erster = "" }
    ' > "$tmp/ereignisse.tsv"
    if [ ! -s "$tmp/ereignisse.tsv" ]; then
        warn "Das Ereignisprotokoll System.evtx auf $2 lässt sich nicht lesen oder ist leer (beschädigt?)."
        return 0
    fi
    neu=$(cut -f1 "$tmp/ereignisse.tsv" | sort | tail -1 | cut -c1-10)
    ab=$(date -u -d "$neu -14 days" +%Y-%m-%d 2>/dev/null || echo "$neu")
    gesamt=$(wc -l < "$tmp/ereignisse.tsv")
    info "Ereignisprotokoll System.evtx: $gesamt Einträge, letzter vom $neu (ausgewertet: Fehler seit $ab; alle Uhrzeiten in UTC, also im Winter eine Stunde, im Sommer zwei Stunden hinter der Ortszeit)."
    # Nur Stufe 1 (kritisch) und 2 (Fehler) im Zeitraum, dazu die Windows-Meldung über ein unerwartetes Ausschalten (Stufe egal)
    awk -F'\t' -v ab="$ab" '($1 >= ab) && ($2 == 1 || $2 == 2 || ($3 == "Microsoft-Windows-Kernel-Power" && $4 == 41) || ($3 == "EventLog" && $4 == 6008))' "$tmp/ereignisse.tsv" > "$tmp/fehler.tsv"
    [ -s "$tmp/fehler.tsv" ] || { ok "Keine Fehler im Ereignisprotokoll der letzten 14 Tage vor dem letzten Eintrag."; return 0; }

    zeit_letzte() { sort | tail -1 | cut -c1-16 | tr T ' ' | sed 's/$/ UTC/'; }
    n=$(awk -F'\t' '$3 == "Microsoft-Windows-Kernel-Power" && $4 == 41' "$tmp/fehler.tsv" | wc -l)
    if [ "$n" -gt 0 ]; then
        z=$(awk -F'\t' '$3 == "Microsoft-Windows-Kernel-Power" && $4 == 41 {print $1}' "$tmp/fehler.tsv" | zeit_letzte)
        warn "Unerwartetes Ausschalten oder Neustart (Kernel-Power 41): $n Mal, zuletzt $z. Typisch bei Absturz, Stromausfall, Überhitzung oder Ausschalten per Taste."
    fi
    # Abstürze mit Stoppcode (WER-SystemErrorReporting 1001: der erste Datenwert nennt den Stoppcode)
    awk -F'\t' '$3 == "Microsoft-Windows-WER-SystemErrorReporting" && $4 == 1001' "$tmp/fehler.tsv" | sort | tail -3 | while IFS="$(printf '\t')" read -r t l p i d; do
        code=$(printf '%s' "$d" | grep -oiE '0x[0-9a-f]{1,8}' | head -1 | sed 's/^0[xX]0*/0x/' | tr 'a-f' 'A-F' | sed 's/^0X/0x/')
        [ "$code" = "0x" ] && code=0x0
        zeile=$(stopp_name "$code")
        echo "[WARNUNG] Absturz (Bluescreen) am $(printf '%s' "$t" | cut -c1-16 | tr T ' ') UTC: Stoppcode $code ${zeile%%|*} – ${zeile#*|}"
    done > "$tmp/abstuerze.txt"
    if [ -s "$tmp/abstuerze.txt" ]; then cat "$tmp/abstuerze.txt"; NF=$((NF+$(wc -l < "$tmp/abstuerze.txt"))); fi
    # Datenträger: Fehler auf Harddisk0 (meist die Systemplatte) oder ohne Angabe gegen Zusatzlaufwerke (Harddisk1 …, oft USB) trennen;
    # der erste Datenwert dieser Ereignisse nennt das Gerät, z. B. \Device\Harddisk1\DR1.
    plattenmuster='^(disk|stornvme|storahci|storport|volmgr|partmgr|iaStor.*|vhdmp)$'
    n=$(awk -F'\t' -v m="$plattenmuster" '$3 ~ m && ($2 == 1 || $2 == 2) && $5 !~ /Harddisk[1-9]/' "$tmp/fehler.tsv" | wc -l)
    if [ "$n" -gt 0 ]; then
        z=$(awk -F'\t' -v m="$plattenmuster" '$3 ~ m && ($2 == 1 || $2 == 2) && $5 !~ /Harddisk[1-9]/ {print $1}' "$tmp/fehler.tsv" | zeit_letzte)
        warn "Datenträger- und Controllerfehler im Ereignisprotokoll: $n Meldung(en), zuletzt $z. Platte, Kabel und Anschluss prüfen."
    fi
    n=$(awk -F'\t' -v m="$plattenmuster" '$3 ~ m && ($2 == 1 || $2 == 2) && $5 ~ /Harddisk[1-9]/' "$tmp/fehler.tsv" | wc -l)
    if [ "$n" -gt 0 ]; then
        z=$(awk -F'\t' -v m="$plattenmuster" '$3 ~ m && ($2 == 1 || $2 == 2) && $5 ~ /Harddisk[1-9]/ {print $1}' "$tmp/fehler.tsv" | zeit_letzte)
        warn "Fehler auf Zusatzlaufwerken (Harddisk1 und höher) im Ereignisprotokoll: $n Meldung(en), zuletzt $z. Meist ein USB-Gerät (Stick, externe Platte), das ohne Abmelden entfernt wurde oder wackelt."
    fi
    for gruppe in 'Ntfs|ReFS:Dateisystemfehler (NTFS)' 'Microsoft-Windows-WHEA-Logger:Hardwarefehler (WHEA: Prozessor, Speicher, PCIe)'; do
        muster=${gruppe%%:*}; titel=${gruppe#*:}
        n=$(awk -F'\t' -v m="^($muster)$" '$3 ~ m && ($2 == 1 || $2 == 2)' "$tmp/fehler.tsv" | wc -l)
        if [ "$n" -gt 0 ]; then
            z=$(awk -F'\t' -v m="^($muster)$" '$3 ~ m && ($2 == 1 || $2 == 2) {print $1}' "$tmp/fehler.tsv" | zeit_letzte)
            warn "$titel im Ereignisprotokoll: $n Meldung(en), zuletzt $z."
        fi
    done
    # Dienste und Treiber, die nicht starten konnten (Service Control Manager): auch auf gesunden Systemen häufig, daher nur ein Hinweis
    n=$(awk -F'\t' '$3 == "Service Control Manager" && ($4 == 7000 || $4 == 7001 || $4 == 7026 || $4 == 7023 || $4 == 7031 || $4 == 7034)' "$tmp/fehler.tsv" | wc -l)
    [ "$n" -gt 0 ] && info "Dienste oder Treiber konnten nicht starten oder sind abgestürzt (Service Control Manager): $n Meldung(en) (kommt auch auf gesunden Systemen vor)."
    # Die häufigsten Fehlerquellen, wenn sonst nichts auffällt
    info "Häufigste Fehlerquellen im Zeitraum (Anzahl, Quelle, Kennung):"
    awk -F'\t' '$2 == 1 || $2 == 2 {print $3 " (" $4 ")"}' "$tmp/fehler.tsv" | sort | uniq -c | sort -rn | head -5 | sed 's/^ *//; s/^/    /'
}

# ---- Spuren einer Windows-Installation ---------------------------------------------------------------------------------

analysiere() { # Einhängepunkt, Gerät
    w=$1; dev=$2
    wdir=$(pfad_ci "$w" windows) || return 1
    pfad_ci "$wdir" system32 >/dev/null || return 1
    gefunden=$((gefunden+1))
    echo
    info "Windows-Installation auf $dev gefunden."
    s32=$(pfad_ci "$wdir" system32)
    hinweise=0

    # 1. Startreparatur
    srt=$(pfad_ci "$s32" logfiles srt srttrail.txt) && {
        hinweise=$((hinweise+1))
        warn "Die automatische Startreparatur ist gelaufen (SrtTrail.txt vom $(datum "$srt"))."
        txt=$(iconv -f UTF-16 -t UTF-8 "$srt" 2>/dev/null || cat "$srt")
        printf '%s\n' "$txt" | tr -d '\r' | grep -iE 'root cause|ursache|error code|fehlercode|failed|fehlgeschlagen' | sed 's/^ *//' | head -8 | cut -c1-200 | sed 's/^/    /'
    }

    # 2. Absturzabbilder
    md=$(pfad_ci "$wdir" minidump) && {
        for d in $(ls -t "$md"/*.dmp "$md"/*.DMP 2>/dev/null | head -3); do
            code=$(stoppcode "$d") || continue
            hinweise=$((hinweise+1))
            info_zeile=$(stopp_name "$code")
            warn "Absturzabbild vom $(datum "$d"): Stoppcode $code ${info_zeile%%|*} – ${info_zeile#*|}"
        done
    }
    mem=$(pfad_ci "$wdir" memory.dmp) && {
        code=$(stoppcode "$mem") && {
            hinweise=$((hinweise+1))
            info_zeile=$(stopp_name "$code")
            warn "Vollständiges Absturzabbild (MEMORY.DMP) vom $(datum "$mem"): Stoppcode $code ${info_zeile%%|*} – ${info_zeile#*|}"
        }
    }

    # 3. Ausstehende Updates
    sxs=$(pfad_ci "$wdir" winsxs pending.xml) && [ -s "$sxs" ] && {
        hinweise=$((hinweise+1))
        warn "Windows-Updates sind angefangen, aber nicht abgeschlossen (WinSxS/pending.xml): eine häufige Ursache für Startschleifen nach einem Update."
    }

    # 4. Update- und Setup-Protokolle
    cbs=$(pfad_ci "$wdir" logs cbs cbs.log) && {
        z=$(tail -n 20000 "$cbs" 2>/dev/null | grep -a ' Error ' | tail -3 | cut -c1-200)
        [ -n "$z" ] && { hinweise=$((hinweise+1)); info "Letzte Fehler im Update-Protokoll (CBS.log, $(datum "$cbs")):"; printf '%s\n' "$z" | sed 's/^/    /'; }
    }
    se=$(pfad_ci "$wdir" panther setuperr.log) && [ -s "$se" ] && {
        z=$(tail -n 5 "$se" | cut -c1-200)
        hinweise=$((hinweise+1)); info "Fehler beim Windows-Setup oder -Update (setuperr.log, $(datum "$se")):"; printf '%s\n' "$z" | sed 's/^/    /'
    }

    # 5. Startprotokoll (nur vorhanden, wenn die Startprotokollierung eingeschaltet war)
    nt=$(pfad_ci "$wdir" ntbtlog.txt) && {
        z=$(grep -a 'Did not load driver' "$nt" 2>/dev/null | tail -5 | cut -c1-160)
        [ -n "$z" ] && { hinweise=$((hinweise+1)); info "Treiber, die beim letzten protokollierten Start nicht geladen wurden (ntbtlog.txt, $(datum "$nt")):"; printf '%s\n' "$z" | sed 's/^/    /'; }
    }

    # 6. Freier Platz
    frei=$(df -Pk "$w" 2>/dev/null | awk 'NR==2 {print $4}')
    if [ -n "$frei" ] && [ "$frei" -lt 2097152 ]; then
        hinweise=$((hinweise+1))
        warn "Auf der Windows-Partition $dev sind weniger als 2 GiB frei ($((frei / 1024)) MiB): Updates und Start können daran scheitern."
    fi

    vorher=$NF
    ereignisse "$s32" "$dev"
    hinweise=$((hinweise + NF - vorher))

    if [ "$hinweise" = 0 ]; then
        info "Auf $dev keine Hinweise auf die Ursache gefunden (keine Startreparatur-Datei, keine Absturzabbilder, keine ausstehenden Updates, genug freier Platz, keine Fehler im Ereignisprotokoll)."
    fi
    return 0
}

windows_durchlaufen analysiere
windows_abschluss
exit 0
