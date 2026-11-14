#!/bin/sh
# Windows-11-Tauglichkeit im Live-System (nur lesend). Das Windows-Gegenstück steckt im Manifest windows11.json (PowerShell).
# Testhilfen: DIAGNOSTIKER_PROC (statt /proc) und DIAGNOSTIKER_SYSFS (statt /sys).
. "$(dirname "$0")/../lib/ausgabe.sh"
PROC=${DIAGNOSTIKER_PROC:-/proc}; SYS=${DIAGNOSTIKER_SYSFS:-/sys}
mangel=0
mangel() { warn "$*"; mangel=$((mangel+1)); }
echo "[INFO] Dieser Test läuft im Live-System: Gemessen wird die Hardware, nicht die installierte Windows-Version."
name=$(sed -n 's/^model name[[:space:]]*: *//p' "$PROC/cpuinfo" 2>/dev/null | head -1 | tr -s ' ')
kerne=$(grep -c '^processor' "$PROC/cpuinfo" 2>/dev/null)
mhz=$(sed -n 's/^cpu MHz[[:space:]]*: *\([0-9]*\).*/\1/p' "$PROC/cpuinfo" 2>/dev/null | sort -rn | head -1)
info "Prozessor: ${name:-unbekannt} ($kerne logische Kerne)."
grep -qw lm "$PROC/cpuinfo" 2>/dev/null || mangel "Der Prozessor ist keine 64-Bit-CPU: Windows 11 gibt es nur für 64 Bit."
[ "${kerne:-0}" -ge 2 ] || mangel "Der Prozessor hat weniger als 2 Kerne (Windows 11 verlangt mindestens 2)."
urteil=unklar; gen=
case "$name" in
    *"Core(TM) Ultra"*|*"Ryzen AI"*|*Snapdragon*) urteil=ok ;;
    *"Core(TM) i"[3579]-*)
        n=$(printf '%s' "$name" | sed -n 's/.*Core(TM) i[3579]-\([0-9]\{4,5\}\).*/\1/p')
        # 5 Stellen: erste zwei Ziffern (10510U = 10. Generation); 4 Stellen: bei führender 1 ebenfalls zwei (1165G7 = 11., 1255U = 12.), sonst eine (8250U = 8.)
        case "$n" in 1????|1???) gen=$(printf '%s' "$n" | cut -c1-2) ;; *) gen=$(printf '%s' "$n" | cut -c1) ;; esac
        if [ -n "$gen" ] && [ "$gen" -ge 8 ]; then urteil=ok; else urteil=alt; fi ;;
    *Ryzen*)
        n=$(printf '%s' "$name" | sed -n 's/.*Ryzen \(Threadripper \)\{0,1\}\(PRO \)\{0,1\}\([3579] \)\{0,1\}\(PRO \)\{0,1\}\([0-9]\)[0-9]\{3\}.*/\5/p')
        if [ -n "$n" ]; then gen=$n; if [ "$gen" -ge 2 ]; then urteil=ok; else urteil=alt; fi; fi ;;
esac
case $urteil in
    ok)  ok "Prozessor: gehört nach der Faustregel zu den von Windows 11 unterstützten Generationen." ;;
    alt) case "$name" in *Ryzen*) marke="AMD Ryzen ${gen}000" ;; *) marke="Intel Core der $gen. Generation" ;; esac
         mangel "Prozessor: $marke ist zu alt: Windows 11 unterstützt offiziell Intel Core ab der 8. Generation und AMD Ryzen ab der 2000er-Reihe. Das lässt sich nicht beheben." ;;
    *)   info "Prozessor: nach der Faustregel nicht beurteilbar (Pentium, Celeron, Xeon, Athlon …). Die Prozessorliste von Microsoft entscheidet." ;;
esac
kb=$(sed -n 's/^MemTotal: *\([0-9]*\) kB.*/\1/p' "$PROC/meminfo" 2>/dev/null)
if [ -n "$kb" ]; then
    gb=$(awk -v k="$kb" 'BEGIN { printf "%.1f", k / 1048576 }')
    if [ "$kb" -lt 3800000 ]; then mangel "Arbeitsspeicher: $gb GiB, Windows 11 verlangt mindestens 4 GiB."; else ok "Arbeitsspeicher: $gb GiB (mindestens 4 GiB nötig)."; fi
fi
# Größte fest eingebaute Platte
groesse=0
for d in "$SYS"/block/sd? "$SYS"/block/nvme?n? "$SYS"/block/mmcblk?; do
    [ -d "$d" ] || continue
    [ "$(cat "$d/removable" 2>/dev/null)" = 1 ] && continue
    s=$(cat "$d/size" 2>/dev/null); [ -n "$s" ] || continue
    [ "$s" -gt "$groesse" ] && groesse=$s
done
gb=$((groesse * 512 / 1000000000))
if [ "$gb" -gt 0 ]; then
    if [ "$gb" -lt 64 ]; then mangel "Größte Platte: $gb GB, Windows 11 verlangt mindestens 64 GB."; else ok "Größte Platte: $gb GB (mindestens 64 GB nötig)."; fi
fi
if [ -d "$SYS/firmware/efi" ]; then
    f="$SYS/firmware/efi/efivars/SecureBoot-8be4df61-93ca-11d2-aa0d-00e098032b8c"
    sb=""
    [ -r "$f" ] && sb=$(od -An -tu1 "$f" | awk '{print $5}')
    case "$sb" in
        1) ok "Firmware: UEFI mit eingeschaltetem Secure Boot." ;;
        0) mangel "Firmware: UEFI, aber Secure Boot ist ausgeschaltet. Windows 11 braucht Secure Boot (im BIOS einschaltbar, wenn der Rechner es kann)." ;;
        *) info "Firmware: UEFI, Secure Boot nicht lesbar." ;;
    esac
else
    mangel "Der Rechner wurde im alten BIOS-Modus (Legacy/CSM) gestartet. Windows 11 braucht UEFI mit Secure Boot: Das geht oft durch Umstellen der Festplatte auf GPT (MBR2GPT) und Aktivieren von UEFI im BIOS, birgt aber Risiken und gehört gesichert. (Ist im BIOS nur der Startweg des Sticks auf Legacy gestellt, kann der Rechner trotzdem UEFI.)"
fi
tpm=""
[ -r "$SYS/class/tpm/tpm0/tpm_version_major" ] && tpm=$(cat "$SYS/class/tpm/tpm0/tpm_version_major")
case "$tpm" in
    2) ok "TPM: Version 2.0 vorhanden." ;;
    1) mangel "TPM: Version 1.2, Windows 11 braucht TPM 2.0." ;;
    *) mangel "Kein TPM-Chip erkannt. Windows 11 braucht TPM 2.0: Bei vielen Rechnern ist es im BIOS nur ausgeschaltet (Intel PTT, AMD fTPM, „Security Device“ aktivieren)." ;;
esac
info "Nicht geprüft: Grafik (DirectX 12/WDDM 2.0) und Bildschirm (mindestens 720p, über 9 Zoll)."
echo
if [ "$mangel" = 0 ] && [ "$urteil" = unklar ]; then
    warn "Gesamteindruck: Alle prüfbaren Voraussetzungen sind erfüllt, der Prozessor ließ sich aber nicht beurteilen. Die Prozessorliste von Microsoft („PC-Integritätsprüfung“) entscheidet."
elif [ "$mangel" = 0 ]; then
    ok "Gesamteindruck: Dieser Rechner erfüllt nach diesem Test die Mindestvoraussetzungen für Windows 11. Endgültige Bestätigung: Microsoft „PC-Integritätsprüfung“ (PC Health Check)."
else
    warn "Gesamteindruck: Windows 11 ist hier voraussichtlich nicht ohne Weiteres möglich: $mangel Voraussetzung(en) nicht erfüllt (siehe oben). Bleibt der Rechner bei Windows 10, gibt es keine regulären Sicherheitsupdates mehr; Linux ist dann eine Alternative."
fi
exit 0
