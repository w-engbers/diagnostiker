#!/bin/sh
# Ende-zu-Ende-Test des Basis-Updates (Live-System auf einem vorhandenen Stick ersetzen), ohne echtes Laufwerk:
#   1. Stick-Abbild aus einem ALTEN ISO erstellen, Markierungsdatei in protokolle/ und eine überflüssige Datei in live/ anlegen
#   2. stick/basis-aktualisieren.sh mit dem NEUEN ISO auf die Abbild-Datei anwenden
#   3. prüfen: alle Dateien des neuen ISO stimmen, grub.cfg angepasst, nichts Überflüssiges in live/, i386-pc und protokolle/ unberührt
#   4. das aktualisierte Abbild in QEMU starten (BIOS und UEFI mit Secure Boot): tests/boot/boot-test.sh
# Aufruf aus dem Projektordner:  tests/stick/basis-test.sh <altes.iso> <neues.iso>
# Voraussetzung: Docker, build/stick-staging (stick/staging.sh), qemu und ovmf für den Startteil. Dauert etwa 10 bis 15 Minuten.
set -eu

HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/../.." && pwd)
ALT=$(realpath "${1:?Aufruf: $0 <altes.iso> <neues.iso>}"); NEU=$(realpath "${2:?}")
STAGING="$WURZEL/build/stick-staging"
AUS="$WURZEL/build/basistest"
BILD=diagnostiker-basis-debian
[ -f "$ALT" ] && [ -f "$NEU" ] && [ -d "$STAGING" ] || { echo "ISO oder $STAGING fehlt." >&2; exit 2; }
rm -rf "$AUS"; mkdir -p "$AUS"

cont() { # <Skript> – privilegierter Container mit den Abbildern
    docker run --rm --privileged -v "$ALT":/alt.iso:ro -v "$NEU":/neu.iso:ro -v "$STAGING":/staging:ro -v "$AUS":/t \
        -v "$WURZEL/stick":/stick:ro "$BILD" sh -c 'i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done; '"$1"
}

echo "== 1. Stick aus dem alten ISO erstellen und präparieren"
cont '
    set -e
    sh /stick/stick-erstellen.sh /t/test.img 6 /alt.iso /staging >/dev/null
    L=$(losetup -f --show /t/test.img)
    teil() { sgdisk -i "$1" "$L" | awk "/^First sector:/ {s=\$3} /^Partition size:/ {n=\$3} END {print s, n}"; }
    set -- $(teil 2); LB=$(losetup -f --show -o $((${1} * 512)) --sizelimit $((${2} * 512)) /t/test.img)
    set -- $(teil 3); LD=$(losetup -f --show -o $((${1} * 512)) --sizelimit $((${2} * 512)) /t/test.img)
    mkdir -p /m/b /m/d; mount "$LB" /m/b; mount -t exfat "$LD" /m/d
    echo "geheim" > /m/d/protokolle/marker.txt
    echo "alt" > /m/b/live/veraltet.bin
    cp /m/b/boot/grub/grub.cfg /t/grub-alt.cfg
    ls /m/b/boot/grub/i386-pc | wc -l > /t/i386-alt.txt
    # Das Live-System "veralten": Datei zerstören (wird durch das Update ersetzt)
    dd if=/dev/zero of=/m/b/live/filesystem.squashfs bs=1M count=1 conv=notrunc 2>/dev/null
    umount /m/b /m/d; losetup -d "$LB"; losetup -d "$LD"; losetup -d "$L"'

echo "== 2. Basis-Update anwenden"
cont 'sh /stick/basis-aktualisieren.sh /t/test.img /neu.iso' | tail -8

echo "== 3. Ergebnis prüfen"
cont '
    set -e
    L=$(losetup -f --show /t/test.img)
    teil() { sgdisk -i "$1" "$L" | awk "/^First sector:/ {s=\$3} /^Partition size:/ {n=\$3} END {print s, n}"; }
    set -- $(teil 2); LB=$(losetup -f --show -o $((${1} * 512)) --sizelimit $((${2} * 512)) /t/test.img)
    set -- $(teil 3); LD=$(losetup -f --show -o $((${1} * 512)) --sizelimit $((${2} * 512)) /t/test.img)
    mkdir -p /m/b /m/d /m/i; mount "$LB" /m/b; mount -t exfat "$LD" /m/d; mount -o loop,ro /neu.iso /m/i
    fehler=0
    pruefe() { if eval "$2"; then echo "   ok      $1"; else echo "   FEHLER  $1"; fehler=1; fi; }
    pruefe "alle Dateien des neuen ISO stimmen" "(cd /m/i && find . -type f ! -path \"./isolinux/*\" ! -path ./efi.img ! -path ./boot/grub/efi.img ! -path ./boot/grub/grub.cfg) | while read -r f; do cmp -s \"/m/i/\$f\" \"/m/b/\$f\" || { echo \"abweichend: \$f\"; exit 1; }; done"
    pruefe "veraltete Datei in live/ entfernt" "[ ! -e /m/b/live/veraltet.bin ]"
    pruefe "grub.cfg hat live-media und kein findiso" "grep -q live-media=/dev/disk/by-label/DIAGBOOT /m/b/boot/grub/grub.cfg && ! grep -q findiso /m/b/boot/grub/grub.cfg"
    pruefe "grub.cfg stammt aus dem neuen ISO (Kernelname)" "grep -o \"vmlinuz[^ ]*\" /m/i/boot/grub/grub.cfg | sort -u | while read -r k; do grep -q \"\$k\" /m/b/boot/grub/grub.cfg; done"
    pruefe "GRUB-Kern i386-pc unberührt" "[ \"\$(ls /m/b/boot/grub/i386-pc | wc -l)\" = \"\$(cat /t/i386-alt.txt)\" ]"
    pruefe "protokolle/ unberührt" "[ \"\$(cat /m/d/protokolle/marker.txt)\" = geheim ]"
    pruefe "keine Hilfsdateien (.neu)" "[ -z \"\$(find /m/b /m/d -name \"*.neu\")\" ]"
    umount /m/b /m/d /m/i; losetup -d "$LB"; losetup -d "$LD"; losetup -d "$L"
    exit $fehler'

echo "== 4. Start des aktualisierten Abbilds in QEMU"
if command -v qemu-system-x86_64 >/dev/null; then
    "$WURZEL/tests/boot/boot-test.sh" "$AUS/test.img" bios | tail -3
    "$WURZEL/tests/boot/boot-test.sh" "$AUS/test.img" uefi-sb | tail -3
else
    echo "   qemu fehlt, Startteil übersprungen"
fi
echo "BASISTEST_FERTIG"
