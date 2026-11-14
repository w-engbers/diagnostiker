#!/bin/sh
# Sucht NUR LESEND nach Spuren, warum eine Windows-Installation des Rechners nicht startet (Linux-Live-System, Root nötig).
# Gelesen werden Dateien der Windows-Partition, die ohne das Windows-Ereignisprotokoll Aufschluss geben:
#   Startreparatur (SrtTrail.txt), Absturzabbilder (Minidump, MEMORY.DMP: Stoppcode), ausstehende Updates (pending.xml),
#   Update- und Setup-Protokolle (CBS.log, setuperr.log), Startprotokoll (ntbtlog.txt) und der freie Platz.
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

val() { printf ' %s\n' "$1" | sed -n "s/.* $2=\"\\([^\"]*\\)\".*/\\1/p" | head -1; }

tmp=$(mktemp -d)
GEHAENGT=0; OFFEN=""
aushaengen() { [ "$GEHAENGT" = 1 ] && umount "$tmp/m" 2>/dev/null; GEHAENGT=0; }
schliessen() { [ -n "$OFFEN" ] && cryptsetup close "$OFFEN" 2>/dev/null; OFFEN=""; }
trap 'aushaengen; schliessen; rm -rf "$tmp"' EXIT

einhaengen() { # Gerät -> W (nur lesend)
    W=
    if [ -n "$DIAGNOSTIKER_EINHAENGEN" ]; then
        W="$DIAGNOSTIKER_EINHAENGEN/$(basename "$1")"
        [ -d "$W" ] && return 0
        W=; return 1
    fi
    mp=$(findmnt -rno TARGET -S "$1" 2>/dev/null | head -1)
    if [ -n "$mp" ]; then W=$mp; return 0; fi
    mkdir -p "$tmp/m"
    if mount -t ntfs-3g -o ro "$1" "$tmp/m" 2>/dev/null; then GEHAENGT=1; W="$tmp/m"; return 0; fi
    return 1
}

# pfad_ci <Ordner> <Name> [<Name> …]: Pfad ohne Beachtung der Groß-/Kleinschreibung (Windows: "Windows", "System32" …)
pfad_ci() {
    p=$1; shift
    for teil in "$@"; do
        n=$(find "$p" -maxdepth 1 -iname "$teil" 2>/dev/null | head -1)
        [ -n "$n" ] || return 1
        p=$n
    done
    printf '%s' "$p"
}
datum() { stat -c %y "$1" 2>/dev/null | cut -c1-16; }

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

lsblk_aus=$(lsblk -Pnpo NAME,TYPE,FSTYPE,SIZE 2>/dev/null)
[ -n "$lsblk_aus" ] || { echo "KEINE_DATENTRAEGER"; exit 0; }

# Wiederherstellungsschlüssel: nur Ziffern, 48 Stück, in Gruppen zu je 6 (Schreibweise, die cryptsetup erwartet)
SCHLUESSEL=$(printf '%s' "${DIAGNOSTIKER_PARAM_SCHLUESSEL:-}" | tr -cd '0-9')
SCHLUESSEL_VORHANDEN=0; FORMAT_OK=1
if [ -n "$SCHLUESSEL" ]; then
    SCHLUESSEL_VORHANDEN=1
    if [ "${#SCHLUESSEL}" = 48 ]; then
        SCHLUESSEL=$(printf '%s' "$SCHLUESSEL" | sed 's/\(.\{6\}\)/\1-/g; s/-$//')
    else
        FORMAT_OK=0
    fi
fi

gefunden=0; verschluesselt_zu=0; nr=0

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

    if [ "$hinweise" = 0 ]; then
        info "Auf $dev keine Hinweise auf die Ursache gefunden (keine Startreparatur-Datei, keine Absturzabbilder, keine ausstehenden Updates, genug freier Platz). Das Windows-Ereignisprotokoll ist noch nicht ausgewertet."
    fi
    return 0
}

echo "$lsblk_aus" > "$tmp/alle"
while read -r z; do
    typ=$(val "$z" TYPE); fs=$(val "$z" FSTYPE); dev=$(val "$z" NAME); groesse=$(val "$z" SIZE)
    [ "$typ" = part ] || continue
    nr=$((nr+1))
    case "$fs" in
        ntfs) einhaengen "$dev" && { analysiere "$W" "$dev"; aushaengen; } ;;
        BitLocker)
            if [ "$SCHLUESSEL_VORHANDEN" = 0 ]; then
                verschluesselt_zu=$((verschluesselt_zu+1))
                info "Partition $dev ($groesse) ist mit BitLocker verschlüsselt und bleibt ohne Wiederherstellungsschlüssel ungeprüft."
            elif [ "$FORMAT_OK" = 0 ]; then
                fehl "Der eingegebene Wiederherstellungsschlüssel hat nicht 48 Ziffern (6 Gruppen zu je 6 Ziffern, acht Gruppen): bitte neu eingeben."
            else
                name="diag-bl$nr"
                if fehlermeldung=$(printf '%s' "$SCHLUESSEL" | cryptsetup bitlkOpen --readonly --key-file=- "$dev" "$name" 2>&1); then
                    OFFEN=$name
                    if einhaengen "/dev/mapper/$name"; then
                        analysiere "$W" "$dev (entsperrt)" || info "Auf $dev (entsperrt) liegt keine Windows-Installation."
                        aushaengen
                    else
                        warn "Die entsperrte Partition $dev lässt sich nicht einhängen (Dateisystem beschädigt?)."
                    fi
                    schliessen
                else
                    fehl "Partition $dev lässt sich nicht entsperren: Der Schlüssel passt nicht zu diesem Laufwerk oder es ist beschädigt ($(printf '%s' "$fehlermeldung" | head -1 | cut -c1-120))."
                fi
            fi ;;
    esac
done < "$tmp/alle"

if [ "$gefunden" = 0 ]; then
    echo
    if [ "$verschluesselt_zu" -gt 0 ]; then
        warn "Es wurde keine Windows-Installation geprüft: Die BitLocker-Partition bleibt ohne Wiederherstellungsschlüssel verschlossen. Das Modul mit dem Schlüssel erneut ausführen (er steht im Microsoft-Konto des Besitzers: account.microsoft.com/devices/recoverykey)."
    else
        info "Keine Windows-Installation auf den Datenträgern gefunden."
    fi
fi
exit 0
