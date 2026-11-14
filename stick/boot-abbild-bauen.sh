#!/bin/sh
# Läuft IM Container (privilegiert) als root. Erzeugt das BOOT-ABBILD für die Auslieferung:
# den Anfang eines Sticks bis zum Ende von DIAGBOOT (Schutz-MBR mit GRUB-Start, GPT-Tabelle, BIOSBOOT, DIAGBOOT mit Live-System).
# DIAGDATEN (Programme, Module, Werkzeuge) gehört NICHT dazu: Das Frontend legt sie beim Erstellen auf die tatsächliche Größe des Sticks an.
# Aufruf: boot-abbild-bauen.sh <ausgabe.img> <live.iso> [boot-MiB]
# Die Ausgabe ist eine rohe Abbild-Datei (die Auslieferung packt sie mit zstd).
set -eu

AUS=${1:?}; ISO=${2:?}; BOOT_MIB=${3:-2560}
[ -f "$ISO" ] || { echo "Aufruf: $0 <ausgabe.img> <iso> [bootMiB]" >&2; exit 2; }

i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done

M_ISO=/mnt/b-iso; M_BOOT=/mnt/b-boot; L_ALL=""; L_BOOT=""
aufraeumen() {
    set +e; sync
    for m in "$M_BOOT" "$M_ISO"; do mountpoint -q "$m" && umount "$m"; done
    for l in "$L_BOOT" "$L_ALL"; do [ -n "$l" ] && losetup -d "$l"; done
}
trap aufraeumen EXIT

echo "== Abbild anlegen"
rm -f "$AUS"
truncate -s "$((BOOT_MIB + 8))M" "$AUS"
L_ALL=$(losetup -f --show "$AUS")

echo "== Partitionieren (GPT: BIOSBOOT und DIAGBOOT)"
sgdisk -Z "$L_ALL" >/dev/null
sgdisk -n 1:2048:4095       -t 1:EF02 -c 1:BIOSBOOT "$L_ALL" >/dev/null
sgdisk -n 2:0:+${BOOT_MIB}M -t 2:EF00 -c 2:DIAGBOOT "$L_ALL" >/dev/null
set -- $(sgdisk -i 2 "$L_ALL" | awk '/^First sector:/ {s=$3} /^Last sector:/ {e=$3} END {print s, e}'); B_START=$1; B_ENDE=$2
L_BOOT=$(losetup -f --show -o $((B_START * 512)) --sizelimit $(((B_ENDE - B_START + 1) * 512)) "$AUS")
mkfs.vfat -F32 -n DIAGBOOT "$L_BOOT" >/dev/null

mkdir -p "$M_ISO" "$M_BOOT"
mount -o loop,ro "$ISO" "$M_ISO"; mount "$L_BOOT" "$M_BOOT"
echo "== Live-System kopieren"
(cd "$M_ISO" && tar cf - --exclude=./isolinux --exclude=./efi.img --exclude=./boot/grub/efi.img .) |
    (cd "$M_BOOT" && tar xf - --no-same-owner --no-same-permissions)

echo "== GRUB-Konfiguration anpassen"
CFG="$M_BOOT/boot/grub/grub.cfg"
sed -i 's/ findiso=\${iso_path}//' "$CFG"
sed -i 's#boot=live #boot=live live-media=/dev/disk/by-label/DIAGBOOT #' "$CFG"
echo "   Einträge mit live-media: $(grep -c 'live-media=' "$CFG")"

echo "== GRUB für Legacy-BIOS installieren"
grub-install --target=i386-pc --boot-directory="$M_BOOT/boot" --modules="part_gpt fat" --recheck "$L_ALL" >/dev/null 2>&1 \
    || { echo "FEHLER: grub-install (i386-pc)" >&2; exit 1; }

aufraeumen; trap - EXIT
# Hinter DIAGBOOT abschneiden: die zweite (Sicherungs-)GPT-Tabelle gehört an das Ende des echten Sticks und wird dort neu geschrieben.
truncate -s "$(((B_ENDE + 1) * 512))" "$AUS"
echo "BOOTABBILD_FERTIG $(stat -c %s "$AUS")"
