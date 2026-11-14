#!/bin/sh
# Linux-Tauglichkeit eines Rechners: Was läuft im Live-System, wo gibt es Einschränkungen? (nur lesend)
# Gedacht für die Beratung vor einem Umstieg von Windows auf Linux. Geprüft werden Prozessor, Arbeitsspeicher, Grafik, Netzwerk,
# Ton, Kamera, Touchpad, Akku, Standby, fehlende Firmware und Geräte ohne Treiber.
# Ausgabe: Zeilen mit [OK], [INFO], [WARNUNG] oder [FEHLER]; die Regeln des Manifests werten diese Marken aus.
#
# Testhilfen: DIAGNOSTIKER_SYSFS (statt /sys) und DIAGNOSTIKER_PROC (statt /proc).
LC_ALL=C
export LC_ALL
SYS=${DIAGNOSTIKER_SYSFS:-/sys}
PROC=${DIAGNOSTIKER_PROC:-/proc}

ok()   { echo "[OK] $*"; }
info() { echo "[INFO] $*"; }
warn() { echo "[WARNUNG] $*"; PROBLEME=$((PROBLEME+1)); }
fehl() { echo "[FEHLER] $*"; PROBLEME=$((PROBLEME+1)); }
PROBLEME=0

# ---- Prozessor und Arbeitsspeicher ----------------------------------------------------------------------------------
if [ -r "$PROC/cpuinfo" ]; then
    modell=$(sed -n 's/^model name[[:space:]]*: *//p' "$PROC/cpuinfo" | head -1)
    kerne=$(grep -c '^processor' "$PROC/cpuinfo")
    info "Prozessor: ${modell:-unbekannt} ($kerne logische Kerne)."
    grep -qw lm "$PROC/cpuinfo" || fehl "Der Prozessor kann nur 32 Bit: Aktuelle Linux-Systeme laufen darauf nur eingeschränkt (Spezialsysteme nötig)."
fi
if [ -r "$PROC/meminfo" ]; then
    kb=$(sed -n 's/^MemTotal: *\([0-9]*\) kB.*/\1/p' "$PROC/meminfo")
    if [ -n "$kb" ]; then
        gb=$(awk -v k="$kb" 'BEGIN { printf "%.1f", k / 1048576 }')
        if [ "$kb" -lt 3500000 ]; then warn "Arbeitsspeicher: $gb GiB: Das ist wenig für einen aktuellen Desktop mit Browser. Eine leichte Oberfläche (z. B. Xfce, LXQt) wird empfohlen, mehr Speicher wäre besser."
        elif [ "$kb" -lt 7500000 ]; then info "Arbeitsspeicher: $gb GiB: reicht für normale Nutzung."
        else ok "Arbeitsspeicher: $gb GiB."; fi
    fi
fi

# ---- Geräte und Treiber (lspci) -------------------------------------------------------------------------------------
if command -v lspci >/dev/null 2>&1; then
    lspci -vmmnnk 2>/dev/null > /tmp/hw-lspci.$$
    # Je Gerät eine Zeile: Klasse|Gerät|Treiber
    awk 'BEGIN { RS = ""; FS = "\n" }
        { k = d = v = t = ""
          for (i = 1; i <= NF; i++) {
              if ($i ~ /^Class:/)  { k = $i; sub(/^Class:[ \t]*/, "", k) }
              if ($i ~ /^Vendor:/) { v = $i; sub(/^Vendor:[ \t]*/, "", v) }
              if ($i ~ /^Device:/) { d = $i; sub(/^Device:[ \t]*/, "", d) }
              if ($i ~ /^Driver:/) { t = $i; sub(/^Driver:[ \t]*/, "", t) }
          }
          print k "|" v " " d "|" t }' /tmp/hw-lspci.$$ > /tmp/hw-geraete.$$
    rm -f /tmp/hw-lspci.$$

    # Grafik
    grep -E '^(VGA|3D|Display)' /tmp/hw-geraete.$$ | while IFS='|' read -r klasse name treiber; do
        name=$(printf '%s' "$name" | sed 's/ \[[0-9a-f]\{4\}\]//g')
        case "$treiber" in
            "")                      echo "[WARNUNG] Grafik: $name hat keinen Treiber: Es bleibt nur ein Notbetrieb ohne Beschleunigung." ;;
            simpledrm|simplefb|efifb|vesafb) echo "[WARNUNG] Grafik: $name läuft nur mit dem einfachen Bildschirmtreiber ($treiber), ohne Beschleunigung und eventuell in niedriger Auflösung." ;;
            nouveau)                 echo "[INFO] Grafik: $name (Nvidia) läuft mit dem freien Treiber nouveau: für Büroarbeit und Video genug, für Spiele und 3D-Anwendungen ist der proprietäre Nvidia-Treiber besser (nachinstallierbar)." ;;
            *)                       echo "[OK] Grafik: $name, Treiber $treiber." ;;
        esac
    done > /tmp/hw-ausgabe.$$
    # Netzwerk (Ethernet 0200, WLAN 0280)
    grep -E '^(Ethernet|Network)' /tmp/hw-geraete.$$ | while IFS='|' read -r klasse name treiber; do
        name=$(printf '%s' "$name" | sed 's/ \[[0-9a-f]\{4\}\]//g')
        if [ -z "$treiber" ]; then echo "[WARNUNG] Netzwerk: $name hat keinen Treiber (Firmware oder Treiber fehlt, eventuell nicht unterstützt)."
        else echo "[OK] Netzwerk: $name, Treiber $treiber."; fi
    done >> /tmp/hw-ausgabe.$$
    # Ton
    grep -E '^(Audio|Multimedia audio)' /tmp/hw-geraete.$$ | while IFS='|' read -r klasse name treiber; do
        name=$(printf '%s' "$name" | sed 's/ \[[0-9a-f]\{4\}\]//g')
        if [ -z "$treiber" ]; then echo "[WARNUNG] Ton: $name hat keinen Treiber."
        else echo "[OK] Ton: $name, Treiber $treiber."; fi
    done >> /tmp/hw-ausgabe.$$
    # Übrige Geräte ohne Treiber (Brücken, Speicher- und Systemkomponenten sind normal und bleiben außen vor)
    grep -vE '^(VGA|3D|Display|Ethernet|Network|Audio|Multimedia audio|Host bridge|PCI bridge|ISA bridge|Bridge|SMBus|RAM memory|System peripheral|Non-Essential|Signal processing|IOMMU|Performance counters|Encryption)' /tmp/hw-geraete.$$ | awk -F'|' '$3 == ""' | while IFS='|' read -r klasse name treiber; do
        name=$(printf '%s' "$name" | sed 's/ \[[0-9a-f]\{4\}\]//g')
        klasse=$(printf '%s' "$klasse" | sed 's/ \[[0-9a-f]\{4\}\]//')
        echo "[INFO] Gerät ohne Treiber: $klasse – $name (muss nicht schlimm sein, z. B. ein Zusatzchip)."
    done >> /tmp/hw-ausgabe.$$
    cat /tmp/hw-ausgabe.$$
    PROBLEME=$((PROBLEME + $(grep -c '^\[WARNUNG\]' /tmp/hw-ausgabe.$$)))
    rm -f /tmp/hw-geraete.$$ /tmp/hw-ausgabe.$$
else
    info "lspci fehlt: Geräte und Treiber lassen sich nicht auflisten."
fi

# ---- Ton (Karten im Kernel) -----------------------------------------------------------------------------------------
if [ -r "$PROC/asound/cards" ]; then
    n=$(grep -c '^ *[0-9]' "$PROC/asound/cards")
    if [ "$n" -gt 0 ]; then ok "Ton: $n Audiogerät(e) erkannt."; else warn "Ton: Kein Audiogerät erkannt (Lautsprecher und Kopfhörer funktionieren so nicht)."; fi
else
    warn "Ton: Kein Audiogerät erkannt (Lautsprecher und Kopfhörer funktionieren so nicht)."
fi

# ---- Kamera, Touchpad -----------------------------------------------------------------------------------------------
kam=$(for d in "$SYS"/class/video4linux/video*; do [ -r "$d/name" ] && cat "$d/name"; done 2>/dev/null | sed 's/:.*//' | sort -u | head -3 | tr '\n' ',' | sed 's/,$//; s/,/, /g')
if [ -n "$kam" ]; then ok "Kamera erkannt: $kam."; else info "Keine Kamera erkannt (bei einem Desktop-PC normal)."; fi
if [ -r "$PROC/bus/input/devices" ]; then
    if grep -qiE 'touchpad|synaptics|elan|glidepoint|trackpad' "$PROC/bus/input/devices"; then ok "Touchpad erkannt."
    else info "Kein Touchpad erkannt (bei einem Desktop-PC normal)."; fi
fi

# ---- Akku -----------------------------------------------------------------------------------------------------------
for b in "$SYS"/class/power_supply/BAT*; do
    [ -d "$b" ] || continue
    voll=$(cat "$b/energy_full" "$b/charge_full" 2>/dev/null | head -1)
    ausl=$(cat "$b/energy_full_design" "$b/charge_full_design" 2>/dev/null | head -1)
    if [ -n "$voll" ] && [ -n "$ausl" ] && [ "$ausl" -gt 0 ]; then
        proz=$((voll * 100 / ausl)); [ "$proz" -gt 100 ] && proz=100
        if [ "$proz" -lt 60 ]; then warn "Akku $(basename "$b"): nur noch $proz % der ursprünglichen Kapazität (stark verschlissen, unabhängig vom Betriebssystem)."
        else ok "Akku $(basename "$b"): $proz % der ursprünglichen Kapazität."; fi
    else
        info "Akku $(basename "$b") vorhanden (Zustand nicht lesbar)."
    fi
done

# ---- Standby -------------------------------------------------------------------------------------------------------
if [ -r "$SYS/power/mem_sleep" ]; then
    s=$(cat "$SYS/power/mem_sleep")
    case "$s" in
        *deep*) info "Standby: $s: klassischer Standby (S3) wird unterstützt." ;;
        *s2idle*) info "Standby: $s: nur „Modern Standby“ (s2idle): funktioniert meist, der Akku entleert sich im Standby aber schneller." ;;
        *) info "Standby: $s." ;;
    esac
fi

# ---- Fehlende Firmware ---------------------------------------------------------------------------------------------
if command -v dmesg >/dev/null 2>&1 || command -v journalctl >/dev/null 2>&1; then
    log=$(journalctl -k -b --no-pager -q 2>/dev/null)
    [ -n "$log" ] || log=$(dmesg 2>/dev/null)
    fw=$(printf '%s\n' "$log" | sed -n 's/.*Direct firmware load for \([^ ]*\) failed.*/\1/p' | grep -v '^/*amdtee/' | sort -u | head -6)
    if [ -n "$fw" ]; then
        info "Firmware konnte nicht geladen werden (kann harmlos sein, wenn das Gerät eine ältere Datei geladen hat):"
        printf '%s\n' "$fw" | sed 's/^/    /'
    fi
fi

# ---- Start und Plattentyp ------------------------------------------------------------------------------------------
if [ -d "$SYS/firmware/efi" ]; then info "Startmodus UEFI: Linux lässt sich im UEFI-Modus installieren."; else info "Startmodus BIOS (Legacy): Linux lässt sich so installieren, ein altes Windows 10 im BIOS-Modus bleibt erhalten."; fi
if [ -d "$SYS/class/tpm" ] && ls "$SYS"/class/tpm/* >/dev/null 2>&1; then info "TPM-Chip vorhanden."; fi
for d in "$SYS"/block/sd? "$SYS"/block/nvme?n? "$SYS"/block/mmcblk?; do
    [ -d "$d" ] || continue
    [ -r "$d/removable" ] && [ "$(cat "$d/removable")" = 1 ] && continue
    n=$(basename "$d")
    [ -r "$d/queue/rotational" ] || continue
    if [ "$(cat "$d/queue/rotational")" = 1 ]; then warn "Datenträger $n ist eine Festplatte (HDD): Linux läuft darauf, aber spürbar langsam. Eine SSD macht den Umstieg deutlich angenehmer."
    else ok "Datenträger $n ist ein Halbleiterlaufwerk (SSD/NVMe)."; fi
done

echo
if [ "$PROBLEME" = 0 ]; then
    ok "Gesamteindruck: Dieser Rechner läuft nach diesem Test gut mit Linux. Fehlt noch: Drucker und Zusatzgeräte des Besitzers."
else
    warn "Gesamteindruck: Linux läuft grundsätzlich, es gibt aber $PROBLEME Punkt(e) mit Einschränkungen (siehe oben)."
fi
exit 0
