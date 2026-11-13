#!/bin/sh
# Bootprüfung des Live-Systems in QEMU/KVM.
#   tests/boot/boot-test.sh <abbild.iso|stick.img> [bios|uefi-sb]
#
# Legt eine Testkopie an, in der die Kernel-Konsole zusätzlich auf die serielle Schnittstelle geht
# (nur die Kopie, das Original bleibt unverändert), startet sie ohne Fenster und wertet die serielle Ausgabe aus.
#   bios     Legacy-BIOS
#   uefi-sb  UEFI mit Secure Boot, OVMF mit den Microsoft-Schlüsseln (shim -> GRUB -> Kernel ohne Schlüsseleintrag)
# Bei einem Stick-Abbild (.img) wird zusätzlich geprüft: Programm läuft von DIAGDATEN, das Protokoll landet dort,
# und das Herunterfahren (ACPI) dauert nicht zu lange.
# Ergebnis: Exit 0 = erfolgreich. Bildschirmfoto und Protokolle in build/boottest/<name>-<modus>/.
# Braucht: qemu-system-x86_64, ovmf (für uefi-sb), Docker (nur zum Ändern der Kopie und zum Lesen des Ergebnisses), python3.
set -eu

QUELLE=${1:?Aufruf: boot-test.sh <abbild.iso|stick.img> [bios|uefi-sb]}
MODUS=${2:-bios}
WARTEN=${WARTEN:-300}
BILD=diagnostiker-basis-debian
HIER=$(cd "$(dirname "$0")" && pwd)
QUELLE=$(cd "$(dirname "$QUELLE")" && pwd)/$(basename "$QUELLE")
case "$QUELLE" in *.img) ART=img ;; *) ART=iso ;; esac
AUS="$HIER/../../build/boottest/$ART-$MODUS"
rm -rf "$AUS"; mkdir -p "$AUS"; AUS=$(cd "$AUS" && pwd)

# In einem Container ausführen, der die Loop-Geräte nutzen kann (Einträge fehlen dort sonst).
im_container() { # <Befehl> (Umgebung: Ordner /t = $AUS, /q = Quelle nur lesend)
    docker run --rm --privileged -v "$QUELLE":/q:ro -v "$AUS":/t -e U="$(id -u)" -e G="$(id -g)" "$BILD" sh -c '
        i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done
        '"$1"
}

echo "== Testkopie mit serieller Konsole ($ART, $MODUS)"
if [ $ART = iso ]; then
    im_container '
        set -e; cd /t
        xorriso -osirrox on -indev /q -extract /boot/grub/grub.cfg grub.cfg -extract /isolinux/live.cfg live.cfg >/dev/null 2>&1
        chmod u+w grub.cfg live.cfg
        sed -i "s/ quiet / console=tty0 console=ttyS0,115200 /" grub.cfg live.cfg
        grep -q ttyS0 grub.cfg && grep -q ttyS0 live.cfg
        # 32 = tolerierte Warnung von libburn (Leseadresse am Ende des Hybrid-ISO), harmlos
        xorriso -indev /q -outdev test.iso -boot_image any keep -boot_image isolinux patch \
            -map grub.cfg /boot/grub/grub.cfg -map live.cfg /isolinux/live.cfg -commit >/dev/null 2>&1 || [ $? -eq 32 ]
        chown "$U:$G" test.iso grub.cfg live.cfg'
    set -- -cdrom "$AUS/test.iso" -boot d
else
    cp --sparse=always "$QUELLE" "$AUS/test.img"
    docker run --rm --privileged -v "$AUS":/t "$BILD" sh -c '
        set -e
        i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done
        S=$(sgdisk -i 2 /t/test.img | awk "/^First sector:/ {print \$3}")
        L=$(losetup -f --show -o $((S * 512)) /t/test.img)
        mkdir -p /m && mount "$L" /m
        sed -i "s/ quiet / console=tty0 console=ttyS0,115200 /" /m/boot/grub/grub.cfg
        grep -q ttyS0 /m/boot/grub/grub.cfg
        umount /m; losetup -d "$L"'
    set -- -drive file="$AUS/test.img",format=raw,if=none,id=stick -device qemu-xhci -device usb-storage,drive=stick,bootindex=1
fi

set -- -m 2048 -smp 2 -no-reboot -display none -nodefaults -vga std "$@" \
    -serial "file:$AUS/seriell.log" -monitor "unix:$AUS/monitor.sock,server,nowait"
if [ -w /dev/kvm ]; then set -- -enable-kvm -cpu host "$@"; else echo "Hinweis: kein KVM, langsam"; fi
if [ "$MODUS" = uefi-sb ]; then
    cp /usr/share/OVMF/OVMF_VARS_4M.ms.fd "$AUS/vars.fd"
    set -- -machine q35 \
        -drive if=pflash,format=raw,readonly=on,file=/usr/share/OVMF/OVMF_CODE_4M.secboot.fd \
        -drive if=pflash,format=raw,file="$AUS/vars.fd" "$@"
fi

monitor() { # <Befehl> – an den QEMU-Monitor senden
    python3 -I - "$AUS/monitor.sock" "$1" <<'PY'
import socket, sys, time
s = socket.socket(socket.AF_UNIX); s.connect(sys.argv[1]); time.sleep(0.5); s.recv(4096)
s.sendall((sys.argv[2] + "\n").encode()); time.sleep(2); s.recv(4096)
PY
}

echo "== Start (bis zu $WARTEN s)"
: > "$AUS/seriell.log"
qemu-system-x86_64 "$@" 2>"$AUS/qemu.log" &
QPID=$!
trap 'kill $QPID 2>/dev/null || true' EXIT

ERFOLG=0; i=0
while [ $i -lt "$WARTEN" ]; do
    sleep 3; i=$((i + 3))
    kill -0 $QPID 2>/dev/null || { echo "QEMU vorzeitig beendet"; break; }
    if grep -q "Reached target.*Multi-User System" "$AUS/seriell.log" 2>/dev/null; then ERFOLG=1; break; fi
done
echo "   nach etwa $i s: $( [ $ERFOLG = 1 ] && echo 'Multi-User-Ziel erreicht' || echo 'Ziel NICHT erreicht')"

# Bildschirmfoto der ersten Konsole (dort läuft der Diagnostiker). Etwas warten, bis das Programm gezeichnet hat.
sleep 20
monitor "screendump $AUS/bildschirm.png -f png" || echo "   (Bildschirmfoto nicht möglich)"

HERUNTER=""
if [ $ART = img ] && kill -0 $QPID 2>/dev/null; then
    echo "== Herunterfahren (ACPI)"
    T0=$(date +%s)
    monitor system_powerdown || true
    while kill -0 $QPID 2>/dev/null && [ $(( $(date +%s) - T0 )) -lt 180 ]; do sleep 1; done
    HERUNTER=$(( $(date +%s) - T0 ))
    kill -0 $QPID 2>/dev/null && echo "   läuft nach 180 s noch" || echo "   beendet nach etwa $HERUNTER s"
fi
kill $QPID 2>/dev/null || true; wait $QPID 2>/dev/null || true; trap - EXIT

echo "== Prüfung"
FEHLER=0
ok()  { echo "   ok      $1"; }
nok() { echo "   FEHLT   $1"; FEHLER=1; }
pruefe() { if grep -qE "$2" "$AUS/seriell.log"; then ok "$1"; else nok "$1"; fi; }
pruefe "Kernel gestartet"      "Linux version"
pruefe "Live-System gestartet" "live-config|live-boot|Reached target.*Basic System"
[ $ERFOLG = 1 ] && ok "Multi-User-Ziel erreicht" || nok "Multi-User-Ziel"
if [ "$MODUS" = uefi-sb ]; then
    pruefe "Secure Boot im Kernel aktiv" "[Ss]ecure boot enabled|secureboot: Secure boot enabled"
    pruefe "UEFI-Start (EFI-Konsole)"    "EFI v[0-9.]+ (by|from)|efi: EFI"
fi
if [ $ART = img ]; then
    [ -n "$HERUNTER" ] && [ "$HERUNTER" -lt 180 ] && ok "Herunterfahren in $HERUNTER s" || nok "Herunterfahren (länger als 180 s)"
    # Ergebnis lesen: Protokollordner auf DIAGDATEN (Programm lief von dort und konnte schreiben)
    docker run --rm --privileged -v "$AUS":/t:ro "$BILD" sh -c '
        i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done
        S=$(sgdisk -i 3 /t/test.img | awk "/^First sector:/ {print \$3}")
        L=$(losetup -r -f --show -o $((S * 512)) /t/test.img)
        mkdir -p /m && mount -o ro "$L" /m
        ls /m/protokolle > /dev/null && for d in /m/protokolle/*; do [ -e "$d" ] && echo "$(basename "$d")"; done
        umount /m; losetup -d "$L"' > "$AUS/protokolle.txt" 2>/dev/null || true
    if [ -s "$AUS/protokolle.txt" ]; then ok "Protokoll auf DIAGDATEN: $(head -1 "$AUS/protokolle.txt")"; else nok "Protokoll auf DIAGDATEN"; fi
fi
echo "   Seriell:    $AUS/seriell.log"
echo "   Bildschirm: $AUS/bildschirm.png"
[ $FEHLER = 0 ] && echo "ERGEBNIS: erfolgreich ($ART, $MODUS)" || echo "ERGEBNIS: FEHLGESCHLAGEN ($ART, $MODUS)"
exit $FEHLER
