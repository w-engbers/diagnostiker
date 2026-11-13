#!/bin/sh
# Läuft IM Container (privilegiert) als root. Ersetzt das Live-System (Partition DIAGBOOT) eines vorhandenen Sticks
# durch den Inhalt eines neuen ISO. DIAGDATEN (Programme, Module, Protokolle) wird nicht berührt, ebenso nicht der
# GRUB-Kern für Legacy-BIOS (Partition BIOSBOOT und boot/grub/i386-pc).
#
# Aufruf: basis-aktualisieren.sh <laufwerk> <live.iso>
#   <laufwerk>  ganzes Laufwerk /dev/sdX (mit Partition 2 = DIAGBOOT, Partition 3 = DIAGDATEN) oder eine Abbild-Datei.
#
# Ein Laufwerk wird nur beschrieben, wenn ALLE Bedingungen stimmen (hier unabhängig vom Aufrufer geprüft):
#   - Umgebung BESTAETIGT_LAUFWERK enthält genau den Gerätepfad
#   - ganzes USB-Laufwerk (Loop-Gerät nur zum Test mit DIAGNOSTIKER_TESTLAUFWERK=ja), 4 bis 512 GiB, nicht schreibgeschützt
#   - Partition 2 heißt DIAGBOOT (FAT32), Partition 3 heißt DIAGDATEN
#   - weder Partition ist im Container eingehängt
#
# Reihenfolge (so, dass ein Abbruch möglichst wenig kaputt macht): neue Dateien kopieren (große zuerst, unter Hilfsnamen
# und dann umbenannt), zuletzt grub.cfg ersetzen, erst danach überflüssige alte Dateien in live/ löschen.
# Ein Abbruch zwischen dem Ersetzen von live/filesystem.squashfs und grub.cfg kann den Stick unstartbar machen
# (Kernel und Dateisystem passen dann nicht zusammen); in dem Fall den Vorgang wiederholen.
set -eu

ZIEL=${1:?Aufruf: $0 <laufwerk> <live.iso>}; ISO=${2:?}
[ -f "$ISO" ] || { echo "Kein ISO: $ISO" >&2; exit 2; }
verweigern() { echo "VERWEIGERT: $1" >&2; exit 3; }

i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done

L_ALL=""; L_BOOT=""; M_ISO=/mnt/basis-iso; M_BOOT=/mnt/basis-boot
aufraeumen() {
    set +e
    sync
    for m in "$M_BOOT" "$M_ISO"; do mountpoint -q "$m" && umount "$m"; done
    for l in "$L_BOOT" "$L_ALL"; do [ -n "$l" ] && losetup -d "$l"; done
}
trap aufraeumen EXIT

if [ -b "$ZIEL" ]; then
    case "$ZIEL" in /dev/*) ;; *) verweigern "kein Gerätepfad" ;; esac
    NAME=${ZIEL#/dev/}
    echo "$NAME" | grep -Eq '^[a-z][a-z0-9]*$' || verweigern "ungültiger Gerätename"
    [ "${BESTAETIGT_LAUFWERK:-}" = "$ZIEL" ] || verweigern "keine Bestätigung für $ZIEL"
    [ -e "/sys/class/block/$NAME" ] || verweigern "unbekanntes Gerät"
    [ ! -e "/sys/class/block/$NAME/partition" ] || verweigern "$ZIEL ist eine Partition, kein ganzes Laufwerk"
    TYP=$(lsblk -dn -o TYPE "$ZIEL" | tr -d ' ')
    TRAN=$(lsblk -dn -o TRAN "$ZIEL" | tr -d ' ')
    [ "$TYP" = disk ] || { [ "$TYP" = loop ] && [ "${DIAGNOSTIKER_TESTLAUFWERK:-}" = ja ]; } || verweigern "Typ $TYP nicht erlaubt"
    [ "$TYP" = loop ] || [ "$TRAN" = usb ] || verweigern "kein USB-Laufwerk (Anschluss: ${TRAN:-unbekannt})"
    [ "$(cat /sys/class/block/$NAME/ro)" = 0 ] || verweigern "schreibgeschützt"
    GROESSE=$(blockdev --getsize64 "$ZIEL")
    [ "$GROESSE" -ge $((4 * 1024 * 1024 * 1024)) ] || verweigern "kleiner als 4 GiB"
    [ "$GROESSE" -le $((512 * 1024 * 1024 * 1024)) ] || verweigern "größer als 512 GiB"
    grep -q "^$ZIEL" /proc/mounts && verweigern "$ZIEL oder eine Partition ist im Container eingehängt"
    GANZ=$ZIEL
else
    [ -f "$ZIEL" ] || verweigern "Ziel ist weder Laufwerk noch Datei"
    [ "${ZIEL#/dev/}" = "$ZIEL" ] || verweigern "Pfad unter /dev, aber kein Blockgerät"
    L_ALL=$(losetup -f --show "$ZIEL"); GANZ=$L_ALL
fi

# Die Partitionstabelle muss die erwartete sein.
name_von() { sgdisk -i "$1" "$GANZ" 2>/dev/null | sed -n "s/^Partition name: '\(.*\)'$/\1/p"; }
[ "$(name_von 2)" = DIAGBOOT ]  || verweigern "Partition 2 heißt nicht DIAGBOOT: das ist kein Diagnostiker-Stick"
[ "$(name_von 3)" = DIAGDATEN ] || verweigern "Partition 3 heißt nicht DIAGDATEN: das ist kein Diagnostiker-Stick"

teil() { sgdisk -i "$1" "$GANZ" | awk '/^First sector:/ {s=$3} /^Partition size:/ {n=$3} END {print s, n}'; }
set -- $(teil 2); B_START=$1; B_LEN=$2
L_BOOT=$(losetup -f --show -o $((B_START * 512)) --sizelimit $((B_LEN * 512)) "$GANZ")
[ "$(blkid -o value -s TYPE "$L_BOOT")" = vfat ] || verweigern "DIAGBOOT ist kein FAT-Dateisystem"
[ "$(blkid -o value -s LABEL "$L_BOOT")" = DIAGBOOT ] || verweigern "DIAGBOOT hat die falsche Bezeichnung"

mkdir -p "$M_ISO" "$M_BOOT"
mount -o loop,ro "$ISO" "$M_ISO"
mount "$L_BOOT" "$M_BOOT"
[ -f "$M_BOOT/boot/grub/grub.cfg" ] && [ -d "$M_BOOT/live" ] || verweigern "DIAGBOOT enthält kein Live-System (live/, boot/grub/grub.cfg)"

# Neue Dateien: alles aus dem ISO außer isolinux, efi.img und grub.cfg (kommt zuletzt).
LISTE=/tmp/neue-dateien
(cd "$M_ISO" && find . -type f ! -path './isolinux/*' ! -path ./efi.img ! -path ./boot/grub/efi.img ! -path ./boot/grub/grub.cfg | sort) > "$LISTE"
ANZAHL=$(wc -l < "$LISTE")
echo "== Live-System aktualisieren ($ANZAHL Dateien prüfen)"
GEKOPIERT=0
while IFS= read -r f; do
    q="$M_ISO/$f"; z="$M_BOOT/$f"
    if [ -f "$z" ] && [ "$(stat -c %s "$q")" = "$(stat -c %s "$z")" ] && cmp -s "$q" "$z"; then continue; fi
    mkdir -p "$(dirname "$z")"
    cp "$q" "$z.neu"
    mv -f "$z.neu" "$z"
    GEKOPIERT=$((GEKOPIERT + 1))
    echo "   kopiert: $f"
done < "$LISTE"
echo "   $GEKOPIERT Datei(en) kopiert"

# grub.cfg zuletzt, mit denselben Anpassungen wie beim Erstellen (ISO sucht sich selbst, der Stick findet das Live-System per Label).
echo "== GRUB-Konfiguration"
CFG_NEU="$M_BOOT/boot/grub/grub.cfg.neu"
cp "$M_ISO/boot/grub/grub.cfg" "$CFG_NEU"
sed -i 's/ findiso=\${iso_path}//' "$CFG_NEU"
sed -i 's#boot=live #boot=live live-media=/dev/disk/by-label/DIAGBOOT #' "$CFG_NEU"
grep -q 'live-media=' "$CFG_NEU" || { rm -f "$CFG_NEU"; echo "FEHLER: grub.cfg ohne live-media" >&2; exit 1; }
mv -f "$CFG_NEU" "$M_BOOT/boot/grub/grub.cfg"

# Überflüssige alte Dateien nur in live/ entfernen (alte Kernel-Versionen). Alles andere bleibt unberührt.
echo "== Alte Dateien in live/ entfernen"
(cd "$M_BOOT" && find live -type f | sort) | while IFS= read -r f; do
    grep -qxF "./$f" "$LISTE" || { rm -f "$M_BOOT/$f"; echo "   entfernt: $f"; }
done
find "$M_BOOT" -name '*.neu' -delete

sync
df -h "$M_BOOT" | sed 's/^/   /'
echo BASIS_FERTIG
