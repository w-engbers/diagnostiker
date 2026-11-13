#!/bin/sh
# Bootprüfung des Live-Abbilds in QEMU/KVM.
#   tests/boot/boot-test.sh <abbild.iso> [bios|uefi-sb]
#
# Legt eine Testkopie des ISO an, in der die Kernel-Konsole zusätzlich auf die serielle Schnittstelle geht
# (nur Testkopie, das Original bleibt unverändert), startet sie ohne Fenster und wertet die serielle Ausgabe aus.
#   bios     Legacy-BIOS (isolinux)
#   uefi-sb  UEFI mit Secure Boot, OVMF mit den Microsoft-Schlüsseln (shim -> GRUB -> Kernel ohne Schlüsseleintrag)
# Ergebnis: Exit 0 = erfolgreich. Bildschirmfoto und Protokoll liegen in build/boottest/<modus>/.
# Braucht: qemu-system-x86_64, ovmf (für uefi-sb), Docker (nur zum Ändern des ISO), python3.
set -eu

ISO=${1:?Aufruf: boot-test.sh <abbild.iso> [bios|uefi-sb]}
MODUS=${2:-bios}
WARTEN=${WARTEN:-300}
BILD=diagnostiker-basis-debian
HIER=$(cd "$(dirname "$0")" && pwd)
AUS="$HIER/../../build/boottest/$MODUS"
ISO=$(cd "$(dirname "$ISO")" && pwd)/$(basename "$ISO")

rm -rf "$AUS"; mkdir -p "$AUS"
AUS=$(cd "$AUS" && pwd)

echo "== Testkopie mit serieller Konsole ($MODUS)"
docker run --rm -v "$ISO":/i/abbild.iso:ro -v "$AUS":/t -e U="$(id -u)" -e G="$(id -g)" "$BILD" sh -c '
    set -e; cd /t
    xorriso -osirrox on -indev /i/abbild.iso -extract /boot/grub/grub.cfg grub.cfg -extract /isolinux/live.cfg live.cfg >/dev/null 2>&1
    chmod u+w grub.cfg live.cfg
    sed -i "s/ quiet / console=tty0 console=ttyS0,115200 /" grub.cfg live.cfg
    grep -q ttyS0 grub.cfg && grep -q ttyS0 live.cfg
    xorriso -indev /i/abbild.iso -outdev test.iso -boot_image any keep -boot_image isolinux patch \
        -map grub.cfg /boot/grub/grub.cfg -map live.cfg /isolinux/live.cfg -commit >/dev/null 2>&1 \
        || [ $? -eq 32 ]   # 32 = tolerierte Warnung von libburn (Leseadresse am Ende des Hybrid-ISO), harmlos
    chown "$U:$G" test.iso grub.cfg live.cfg
'

set -- -m 2048 -smp 2 -cdrom "$AUS/test.iso" -boot d -display none -no-reboot \
    -serial "file:$AUS/seriell.log" -monitor "unix:$AUS/monitor.sock,server,nowait"
if [ -w /dev/kvm ]; then set -- -enable-kvm "$@"; else echo "Hinweis: kein KVM, langsam"; fi
if [ "$MODUS" = uefi-sb ]; then
    cp /usr/share/OVMF/OVMF_VARS_4M.ms.fd "$AUS/vars.fd"
    set -- -machine q35 \
        -drive if=pflash,format=raw,readonly=on,file=/usr/share/OVMF/OVMF_CODE_4M.secboot.fd \
        -drive if=pflash,format=raw,file="$AUS/vars.fd" "$@"
fi

echo "== Start (bis zu $WARTEN s)"
: > "$AUS/seriell.log"
qemu-system-x86_64 "$@" &
QPID=$!
trap 'kill $QPID 2>/dev/null || true' EXIT

ERFOLG=0
i=0
while [ $i -lt "$WARTEN" ]; do
    sleep 3; i=$((i + 3))
    kill -0 $QPID 2>/dev/null || { echo "QEMU vorzeitig beendet"; break; }
    if grep -q "Reached target.*Multi-User System" "$AUS/seriell.log" 2>/dev/null; then ERFOLG=1; break; fi
done
echo "   nach etwa $i s: $( [ $ERFOLG = 1 ] && echo 'Multi-User-Ziel erreicht' || echo 'Ziel NICHT erreicht')"

# Bildschirmfoto der ersten Konsole (dort läuft der Diagnostiker). Etwas warten, bis das Programm gezeichnet hat.
sleep 20
python3 -I - "$AUS/monitor.sock" "$AUS/bildschirm.png" <<'PY' || echo "   (Bildschirmfoto nicht möglich)"
import socket, sys, time
s = socket.socket(socket.AF_UNIX); s.connect(sys.argv[1]); time.sleep(0.5); s.recv(4096)
s.sendall(("screendump %s -f png\n" % sys.argv[2]).encode()); time.sleep(2); s.recv(4096)
PY
kill $QPID 2>/dev/null || true; wait $QPID 2>/dev/null || true; trap - EXIT

echo "== Prüfung"
FEHLER=0
pruefe() { # <Beschreibung> <Muster> [Datei]
    if grep -qE "$2" "$AUS/seriell.log"; then echo "   ok      $1"; else echo "   FEHLT   $1"; FEHLER=1; fi
}
pruefe "Kernel gestartet"                 "Linux version"
pruefe "Live-System gestartet"            "live-config|live-boot|Reached target.*Basic System"
[ $ERFOLG = 1 ] || { echo "   FEHLT   Multi-User-Ziel"; FEHLER=1; }
if [ "$MODUS" = uefi-sb ]; then
    pruefe "Secure Boot im Kernel aktiv"  "[Ss]ecure boot enabled|secureboot: Secure boot enabled"
    pruefe "UEFI-Start (EFI-Konsole)"     "EFI v[0-9.]+ (by|from)|efi: EFI"
fi
echo "   Protokoll:  $AUS/seriell.log"
echo "   Bildschirm: $AUS/bildschirm.png"
[ $FEHLER = 0 ] && echo "ERGEBNIS: erfolgreich ($MODUS)" || echo "ERGEBNIS: FEHLGESCHLAGEN ($MODUS)"
exit $FEHLER
