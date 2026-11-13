#!/bin/sh
# Läuft IM Container (privilegiert) als root. Legt einen Stick an:
#   p1  1 MiB    BIOSBOOT   (GRUB-Kern für Legacy-BIOS auf GPT)
#   p2  FAT32    DIAGBOOT   Live-System (Inhalt des ISO) + signierte UEFI-Kette + GRUB für BIOS
#   p3  exFAT    DIAGDATEN  Programme, Module, Werkzeuge, Protokolle, Starter, stick.json
#
# Aufruf: stick-erstellen.sh <ziel> <groesse-GiB> <live.iso> <staging-ordner> [boot-MiB]
#   <ziel>  eine Abbild-Datei (wird neu angelegt, <groesse-GiB> gilt) ODER ein Laufwerk /dev/sdX (Größe gibt das Laufwerk vor).
#
# Ein Laufwerk wird nur beschrieben, wenn ALLE Bedingungen stimmen (hier noch einmal geprüft, unabhängig vom aufrufenden Programm):
#   - Umgebung BESTAETIGT_LAUFWERK enthält genau den Gerätepfad (der Aufrufer setzt sie erst nach der Bestätigung durch den Menschen)
#   - einfacher Gerätename /dev/<name>, blockorientiertes Gerät, ganzes Laufwerk (keine Partition)
#   - USB-Anschluss (nur zu Testzwecken mit DIAGNOSTIKER_TESTLAUFWERK=ja auch ein Loop-Gerät)
#   - Größe zwischen 4 und 512 GiB, nicht schreibgeschützt
#   - kein Laufwerk, das im Container selbst benutzt wird
#
# exFAT lässt sich nicht nachträglich vergrößern. Darum wird DIAGDATEN beim Erstellen auf die tatsächliche
# Größe des Ziels angelegt und nicht aus einer Vorlage übernommen.
set -eu

ZIEL=${1:?}; GIB=${2:?}; ISO=${3:?}; STAGING=${4:?}; BOOT_MIB=${5:-2560}
[ -f "$ISO" ] && [ -d "$STAGING" ] || { echo "Aufruf: $0 <ziel> <GiB> <iso> <staging> [bootMiB]" >&2; exit 2; }

# Im Container fehlen die Einträge für Loop-Geräte, die der Host erst später anlegt (der Host benutzt oft schon viele).
i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done

verweigern() { echo "VERWEIGERT: $1" >&2; exit 3; }

if [ -b "$ZIEL" ]; then
    MODUS=laufwerk
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
    echo "== Laufwerk $ZIEL ($((GROESSE / 1024 / 1024 / 1024)) GiB, Typ $TYP) wird vollständig neu angelegt"
elif [ -e "$ZIEL" ] && [ ! -f "$ZIEL" ]; then
    verweigern "Ziel ist weder Laufwerk noch normale Datei"
else
    MODUS=datei
    [ "${ZIEL#/dev/}" = "$ZIEL" ] || verweigern "Pfad unter /dev, aber kein Blockgerät"
fi

M_ISO=/mnt/stick-iso; M_BOOT=/mnt/stick-boot; M_DATA=/mnt/stick-data
L_ALL=""; L_BOOT=""; L_DATA=""
aufraeumen() {
    set +e
    sync
    for m in "$M_DATA" "$M_BOOT" "$M_ISO"; do mountpoint -q "$m" && umount "$m"; done
    for l in "$L_DATA" "$L_BOOT" "$L_ALL"; do [ -n "$l" ] && losetup -d "$l"; done
}
trap aufraeumen EXIT

if [ $MODUS = datei ]; then
    echo "== Abbild anlegen: ${GIB} GiB"
    rm -f "$ZIEL"
    truncate -s "${GIB}G" "$ZIEL"
    L_ALL=$(losetup -f --show "$ZIEL")
    GANZ=$L_ALL
else
    GANZ=$ZIEL
    wipefs -a "$GANZ" >/dev/null
fi

echo "== Partitionieren (GPT)"
sgdisk -Z "$GANZ" >/dev/null
sgdisk -n 1:2048:4095       -t 1:EF02 -c 1:BIOSBOOT  "$GANZ" >/dev/null
sgdisk -n 2:0:+${BOOT_MIB}M -t 2:EF00 -c 2:DIAGBOOT  "$GANZ" >/dev/null
sgdisk -n 3:0:0             -t 3:0700 -c 3:DIAGDATEN "$GANZ" >/dev/null

# Start und Größe je Partition aus der Tabelle lesen. Loop-Geräte mit Versatz statt Partitionsgeräten,
# denn Partitionsgeräte erscheinen in einem Container nicht zuverlässig.
teil() { # <nr> -> "<start-sektor> <sektoren>"
    sgdisk -i "$1" "$GANZ" | awk '/^First sector:/ {s=$3} /^Partition size:/ {n=$3} END {print s, n}'
}
set -- $(teil 2); B_START=$1; B_LEN=$2
set -- $(teil 3); D_START=$1; D_LEN=$2

L_BOOT=$(losetup -f --show -o $((B_START * 512)) --sizelimit $((B_LEN * 512)) "$GANZ")
L_DATA=$(losetup -f --show -o $((D_START * 512)) --sizelimit $((D_LEN * 512)) "$GANZ")

echo "== Dateisysteme"
mkfs.vfat -F32 -n DIAGBOOT "$L_BOOT" >/dev/null
mkfs.exfat -L DIAGDATEN   "$L_DATA" >/dev/null

mkdir -p "$M_ISO" "$M_BOOT" "$M_DATA"
mount -o loop,ro "$ISO" "$M_ISO"
mount "$L_BOOT" "$M_BOOT"

echo "== Live-System kopieren (ohne isolinux und efi.img, den BIOS-Start übernimmt GRUB)"
(cd "$M_ISO" && tar cf - --exclude=./isolinux --exclude=./efi.img --exclude=./boot/grub/efi.img .) |
    (cd "$M_BOOT" && tar xf - --no-same-owner --no-same-permissions)

echo "== GRUB-Konfiguration anpassen"
CFG="$M_BOOT/boot/grub/grub.cfg"
# Das ISO sucht sich per findiso=... selbst, vom Stick soll das Live-System seine Partition per Label finden.
sed -i 's/ findiso=\${iso_path}//' "$CFG"
sed -i 's#boot=live #boot=live live-media=/dev/disk/by-label/DIAGBOOT #' "$CFG"
grep -c 'live-media=' "$CFG" | sed 's/^/   Einträge mit live-media: /'

echo "== GRUB für Legacy-BIOS installieren"
grub-install --target=i386-pc --boot-directory="$M_BOOT/boot" --modules="part_gpt fat" --recheck "$GANZ" >/dev/null 2>&1 \
    || { echo "FEHLER: grub-install (i386-pc)" >&2; exit 1; }

echo "== Datenpartition befüllen"
mount -t exfat "$L_DATA" "$M_DATA"
cp -r --no-preserve=all "$STAGING"/. "$M_DATA"/
df -h "$M_BOOT" "$M_DATA" | sed 's/^/   /'

aufraeumen
trap - EXIT
[ $MODUS = laufwerk ] && blockdev --rereadpt "$GANZ" 2>/dev/null || true
echo STICK_FERTIG
