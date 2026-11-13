#!/bin/sh
# Läuft IM Container (privilegiert) als root. Legt einen Stick an, vorerst nur als Abbild-Datei:
#   p1  1 MiB    BIOSBOOT   (GRUB-Kern für Legacy-BIOS auf GPT)
#   p2  FAT32    DIAGBOOT   Live-System (Inhalt des ISO) + signierte UEFI-Kette + GRUB für BIOS
#   p3  exFAT    DIAGDATEN  Programme, Module, Werkzeuge, Protokolle, Starter, stick.json
#
# Aufruf: stick-erstellen.sh <abbild.img> <groesse-GiB> <live.iso> <staging-ordner> [boot-MiB]
#
# exFAT lässt sich nicht nachträglich vergrößern. Darum wird DIAGDATEN beim Erstellen auf die tatsächliche
# Größe des Ziels angelegt und nicht aus einer Vorlage übernommen.
# Das Abbild wird nie auf ein echtes Laufwerk geschrieben (Ziel muss eine normale Datei sein).
set -eu

IMG=${1:?}; GIB=${2:?}; ISO=${3:?}; STAGING=${4:?}; BOOT_MIB=${5:-2560}
[ -f "$ISO" ] && [ -d "$STAGING" ] || { echo "Aufruf: $0 <img> <GiB> <iso> <staging> [bootMiB]" >&2; exit 2; }
[ ! -b "$IMG" ] || { echo "FEHLER: Ziel ist ein Laufwerk, das ist hier nicht erlaubt." >&2; exit 2; }

# Im Container fehlen die Einträge für Loop-Geräte, die der Host erst später anlegt (der Host benutzt oft schon viele).
i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done

M_ISO=/mnt/stick-iso; M_BOOT=/mnt/stick-boot; M_DATA=/mnt/stick-data
L_ALL=""; L_BOOT=""; L_DATA=""
aufraeumen() {
    set +e
    sync
    for m in "$M_DATA" "$M_BOOT" "$M_ISO"; do mountpoint -q "$m" && umount "$m"; done
    for l in "$L_DATA" "$L_BOOT" "$L_ALL"; do [ -n "$l" ] && losetup -d "$l"; done
}
trap aufraeumen EXIT

echo "== Abbild anlegen: ${GIB} GiB"
rm -f "$IMG"
truncate -s "${GIB}G" "$IMG"

echo "== Partitionieren (GPT)"
sgdisk -Z "$IMG" >/dev/null
sgdisk -n 1:2048:4095       -t 1:EF02 -c 1:BIOSBOOT  "$IMG" >/dev/null
sgdisk -n 2:0:+${BOOT_MIB}M -t 2:EF00 -c 2:DIAGBOOT  "$IMG" >/dev/null
sgdisk -n 3:0:0             -t 3:0700 -c 3:DIAGDATEN "$IMG" >/dev/null

# Start und Größe je Partition aus der Tabelle lesen. Loop-Geräte mit Versatz statt Partitionsgeräten,
# denn Partitionsgeräte erscheinen in einem Container nicht zuverlässig.
teil() { # <nr> -> "<start-sektor> <sektoren>"
    sgdisk -i "$1" "$IMG" | awk '/^First sector:/ {s=$3} /^Partition size:/ {n=$3} END {print s, n}'
}
set -- $(teil 2); B_START=$1; B_LEN=$2
set -- $(teil 3); D_START=$1; D_LEN=$2

L_ALL=$(losetup -f --show "$IMG")
L_BOOT=$(losetup -f --show -o $((B_START * 512)) --sizelimit $((B_LEN * 512)) "$IMG")
L_DATA=$(losetup -f --show -o $((D_START * 512)) --sizelimit $((D_LEN * 512)) "$IMG")

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
grub-install --target=i386-pc --boot-directory="$M_BOOT/boot" --modules="part_gpt fat" --recheck "$L_ALL" >/dev/null 2>&1 \
    || { echo "FEHLER: grub-install (i386-pc)" >&2; exit 1; }

echo "== Datenpartition befüllen"
mount -t exfat "$L_DATA" "$M_DATA"
cp -r --no-preserve=all "$STAGING"/. "$M_DATA"/
df -h "$M_BOOT" "$M_DATA" | sed 's/^/   /'

aufraeumen
trap - EXIT
echo STICK_FERTIG
