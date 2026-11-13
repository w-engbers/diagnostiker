#!/bin/sh
# Prüft den Laufwerksmodus von stick/stick-erstellen.sh OHNE echtes Laufwerk: Das "Laufwerk" ist ein Loop-Gerät
# über einer Datei (nur mit DIAGNOSTIKER_TESTLAUFWERK=ja zugelassen). Voraussetzung: build/out/diagnostiker.iso und
# build/stick-staging (zuvor stick/bauen.sh). Ergebnis: build/laufwerktest/stick.img, danach mit boot-test.sh startbar.
set -eu
HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/../.." && pwd)
AUS="$WURZEL/build/laufwerktest"
[ -f "$WURZEL/build/out/diagnostiker.iso" ] && [ -d "$WURZEL/build/stick-staging" ] || { echo "Zuerst stick/bauen.sh ausführen." >&2; exit 1; }
rm -rf "$AUS"; mkdir -p "$AUS"

docker run --rm --privileged \
    -v "$WURZEL/build/out/diagnostiker.iso":/iso:ro -v "$WURZEL/build/stick-staging":/staging:ro \
    -v "$AUS":/aus -v "$WURZEL/stick/stick-erstellen.sh":/s.sh:ro -e U="$(id -u)" -e G="$(id -g)" \
    diagnostiker-basis-debian sh -c '
i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done
FEHLER=0
ok()  { echo "   ok      $1"; }
nok() { echo "   FEHLT   $1"; FEHLER=1; }

truncate -s 6G /aus/stick.img
L=$(losetup -f --show /aus/stick.img)
echo "Testlaufwerk: $L"

echo "== Verweigerungen (es darf nichts geschrieben werden)"
verweigert() { # <Beschreibung> <Umgebung...>
    b=$1; shift
    if env "$@" sh /s.sh "$L" 6 /iso /staging >/tmp/v.log 2>&1; then nok "$b: wurde NICHT verweigert"
    elif grep -q "^VERWEIGERT" /tmp/v.log; then ok "$b: $(grep ^VERWEIGERT /tmp/v.log | cut -c13-90)"
    else nok "$b: anderer Fehler: $(tail -1 /tmp/v.log)"; fi
}
verweigert "ohne Bestätigung"                    DIAGNOSTIKER_TESTLAUFWERK=ja
verweigert "falsche Bestätigung"                 DIAGNOSTIKER_TESTLAUFWERK=ja BESTAETIGT_LAUFWERK=/dev/sdz
verweigert "Loop-Gerät ohne Testfreigabe"        BESTAETIGT_LAUFWERK=$L
# danach muss die Platte noch leer sein (keine Partitionstabelle)
sgdisk -p "$L" 2>&1 | grep -q "Creating new GPT\|invalid\|not present\|Partition table scan" && ok "Testlaufwerk unverändert" || nok "Testlaufwerk wurde verändert"

echo "== Erstellen auf dem Testlaufwerk"
DIAGNOSTIKER_TESTLAUFWERK=ja BESTAETIGT_LAUFWERK=$L sh /s.sh "$L" 6 /iso /staging > /tmp/e.log 2>&1 && ok "Stick erstellt" || { nok "Erstellen fehlgeschlagen"; tail -5 /tmp/e.log; }
tail -4 /tmp/e.log | sed "s/^/      /"

echo "== Ergebnis prüfen"
sgdisk -p "$L" | grep -E "^ +[123] " | sed "s/^/      /"
teil() { sgdisk -i "$1" "$L" | awk "/^First sector:/ {s=\$3} /^Partition size:/ {n=\$3} END {print s, n}"; }
set -- $(teil 2); LB=$(losetup -f --show -r -o $(($1 * 512)) --sizelimit $(($2 * 512)) "$L")
set -- $(teil 3); LD=$(losetup -f --show -r -o $(($1 * 512)) --sizelimit $(($2 * 512)) "$L")
mkdir -p /mb /md; mount -o ro "$LB" /mb; mount -o ro "$LD" /md
[ -e /mb/EFI/boot/bootx64.efi ] || [ -e /mb/EFI/BOOT/BOOTX64.EFI ] && ok "UEFI-Startdatei vorhanden" || nok "UEFI-Startdatei fehlt"
[ -s /mb/live/filesystem.squashfs ] && ok "Live-System vorhanden" || nok "Live-System fehlt"
[ -s /md/bin/linux-x64/Diagnostiker ] && [ -s /md/bin/win-x64/Diagnostiker.exe ] && ok "Programme auf DIAGDATEN" || nok "Programme fehlen"
[ -s /md/stick.json ] && ok "stick.json vorhanden" || nok "stick.json fehlt"
[ "$(blkid -o value -s LABEL "$LB")" = DIAGBOOT ] && [ "$(blkid -o value -s LABEL "$LD")" = DIAGDATEN ] && ok "Bezeichnungen DIAGBOOT und DIAGDATEN" || nok "Bezeichnungen"
umount /mb /md; losetup -d "$LB" "$LD"; losetup -d "$L"
chown "$U:$G" /aus/stick.img
[ $FEHLER = 0 ] && echo "ERGEBNIS: erfolgreich" || echo "ERGEBNIS: FEHLGESCHLAGEN"
exit $FEHLER'
