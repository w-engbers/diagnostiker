#!/bin/bash
# Privilegierter Helfer: schreibt das Boot-Abbild eines Pakets auf ein LAUFWERK und legt DIAGDATEN (exFAT) an.
# Wird vom Stick-Manager über pkexec (oder sudo) als root gestartet; das Skript ist im Programm eingebettet und wird als Text
# übergeben (nicht aus einer vom Benutzer beschreibbaren Datei gelesen). Alle Sicherheitsregeln werden HIER noch einmal geprüft,
# unabhängig vom aufrufenden Programm.
#
# Aufruf: paket-schreiben.sh <laufwerk> <boot.img.gz> <sha256-des-gz> <abbildgroesse-bytes> [test]
#   test  erlaubt zusätzlich ein Loop-Gerät (nur für Tests)
#
# Ablauf: prüfen → Abbild schreiben (gzip -dc | dd) → zurücklesen und vergleichen → GPT-Sicherungskopie ans Ende,
#         Partition 3 (DIAGDATEN) anlegen, neue GUIDs → mkfs.exfat → "SCHREIBEN_FERTIG <partition>"
set -euo pipefail

ZIEL=${1:?Aufruf: $0 <laufwerk> <boot.img.gz> <sha256> <bytes> [test]}
GZ=${2:?}; SHA=${3:?}; BYTES=${4:?}; TEST=${5:-}
export LC_ALL=C

verweigern() { echo "VERWEIGERT: $1" >&2; exit 3; }
[ "$(id -u)" = 0 ] || verweigern "nicht als root gestartet"

# --- Ziel prüfen ---
case "$ZIEL" in /dev/*) ;; *) verweigern "kein Gerätepfad" ;; esac
NAME=${ZIEL#/dev/}
echo "$NAME" | grep -Eq '^[a-z][a-z0-9]*$' || verweigern "ungültiger Gerätename"
[ -b "$ZIEL" ] || verweigern "$ZIEL ist kein Blockgerät"
[ -e "/sys/class/block/$NAME" ] || verweigern "unbekanntes Gerät"
[ ! -e "/sys/class/block/$NAME/partition" ] || verweigern "$ZIEL ist eine Partition, kein ganzes Laufwerk"
TYP=$(lsblk -dn -o TYPE "$ZIEL" | tr -d ' ')
TRAN=$(lsblk -dn -o TRAN "$ZIEL" | tr -d ' ')
[ "$TYP" = disk ] || { [ "$TYP" = loop ] && [ "$TEST" = test ]; } || verweigern "Typ $TYP nicht erlaubt"
[ "$TYP" = loop ] || [ "$TRAN" = usb ] || verweigern "kein USB-Laufwerk (Anschluss: ${TRAN:-unbekannt})"
[ "$(cat "/sys/class/block/$NAME/ro")" = 0 ] || verweigern "schreibgeschützt"
GROESSE=$(blockdev --getsize64 "$ZIEL")
[ "$GROESSE" -ge $((4 * 1024 * 1024 * 1024)) ] || verweigern "kleiner als 4 GiB"
[ "$GROESSE" -le $((512 * 1024 * 1024 * 1024)) ] || verweigern "größer als 512 GiB"
[ -z "$(lsblk -nro MOUNTPOINTS "$ZIEL" | tr -d '[:space:]')" ] || verweigern "$ZIEL oder eine Partition ist eingehängt"
grep -q "^$ZIEL" /proc/swaps && verweigern "$ZIEL wird als Auslagerung benutzt"

# --- Abbild prüfen ---
[ -f "$GZ" ] && [ ! -L "$GZ" ] || verweigern "Abbilddatei fehlt oder ist ein Verweis"
echo "$SHA" | grep -Eq '^[0-9a-fA-F]{64}$' || verweigern "ungültige Prüfsumme"
echo "$BYTES" | grep -Eq '^[0-9]{6,12}$' || verweigern "ungültige Abbildgröße"
[ "$BYTES" -lt $((GROESSE - 64 * 1024 * 1024)) ] || verweigern "das Abbild passt nicht auf das Laufwerk"
echo "== Abbilddatei prüfen"
[ "$(sha256sum "$GZ" | cut -d' ' -f1)" = "$(echo "$SHA" | tr 'A-F' 'a-f')" ] || verweigern "Prüfsumme der Abbilddatei stimmt nicht"

# --- Schreiben ---
echo "== Laufwerk $ZIEL ($((GROESSE / 1024 / 1024 / 1024)) GiB) wird vollständig neu angelegt"
wipefs -a "$ZIEL" >/dev/null
echo "== Abbild schreiben ($((BYTES / 1024 / 1024)) MiB)"
{ gzip -dc "$GZ" | dd of="$ZIEL" bs=4M iflag=fullblock conv=fsync status=progress; } 2>&1 | stdbuf -oL tr '\r' '\n'
echo "== $(date +%T) Schreiben beendet, Zwischenspeicher leeren"
sync
blockdev --flushbufs "$ZIEL"
echo "== $(date +%T) Zurücklesen und vergleichen"
# Das Laufwerk wird mit dd gelesen (Fortschrittszeilen), cmp vergleicht mit dem entpackten Abbild.
dd if="$ZIEL" bs=4M iflag=fullblock,count_bytes count="$BYTES" status=progress 2> >(stdbuf -oL tr '\r' '\n' >&2) | cmp - <(gzip -dc "$GZ") \
    || { echo "FEHLER: Das Laufwerk enthält nicht das geschriebene Abbild (defekter Stick?)." >&2; exit 4; }
echo "== $(date +%T) Vergleich in Ordnung"

# --- Partitionstabelle fertigstellen ---
echo "== $(date +%T) Partitionstabelle fertigstellen"
sfdisk --relocate gpt-bak-std "$ZIEL" >/dev/null
echo 'type=EBD0A0A2-B9E5-4433-87C0-68B6B72699C7, name=DIAGDATEN' | sfdisk --append --no-reread "$ZIEL" >/dev/null
# Jeder Stick bekommt eigene GUIDs (das Abbild ist für alle gleich).
sfdisk --disk-id "$ZIEL" "$(cat /proc/sys/kernel/random/uuid)" >/dev/null
for n in 1 2 3; do sfdisk --part-uuid "$ZIEL" "$n" "$(cat /proc/sys/kernel/random/uuid)" >/dev/null; done
sfdisk --verify "$ZIEL" >/dev/null
partprobe "$ZIEL" 2>/dev/null || blockdev --rereadpt "$ZIEL" 2>/dev/null || true
udevadm settle 2>/dev/null || true

P3=""
for _ in $(seq 1 30); do
    # Die Nummer steht in /sys (lsblk zeigt sie ohne udev nicht an).
    for d in /sys/class/block/"$NAME"*; do
        [ -f "$d/partition" ] && [ "$(cat "$d/partition")" = 3 ] && P3=/dev/$(basename "$d")
    done
    [ -n "$P3" ] && [ -b "$P3" ] && break
    P3=""; sleep 1
done
[ -n "$P3" ] || { echo "FEHLER: Die Partition 3 erscheint nicht." >&2; exit 5; }

echo "== Datenpartition formatieren (exFAT, DIAGDATEN)"
mkfs.exfat -L DIAGDATEN "$P3" >/dev/null 2>&1 || mkfs.exfat -n DIAGDATEN "$P3" >/dev/null
sync
echo "SCHREIBEN_FERTIG $P3"
