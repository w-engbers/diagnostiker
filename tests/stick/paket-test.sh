#!/bin/sh
# Ende-zu-Ende-Test des Anwender-Helfers stick/paket-schreiben.sh OHNE echtes Laufwerk:
#   - Das "Laufwerk" ist ein Loop-Gerät über einer 6-GiB-Datei (nur mit dem Argument "test" zugelassen).
#   - Läuft in einem Container, der wie ein Anwenderrechner nur util-linux/fdisk/exfatprogs hat (tests/stick/anwender-image).
#   1. Verweigerungen: ohne "test", falsche Prüfsumme, Abbild zu groß, Verweis statt Datei, eingehängt.
#   2. Schreiben: Tabelle (3 Partitionen, GPT-Sicherungskopie am Ende, eigene GUIDs, Bezeichnungen), DIAGDATEN leer und exFAT.
#   3. Zwei "Sticks" aus demselben Paket haben verschiedene GUIDs.
#   4. Datenpartition füllen (wie das Programm: daten.tar.gz entpacken) und das Ergebnis in QEMU starten (BIOS und UEFI mit Secure Boot).
# Aufruf: tests/stick/paket-test.sh <paketordner>        (Ordner mit boot.img.gz, daten.tar.gz, manifest.json; z. B. von release/bauen.sh)
set -eu
HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/../.." && pwd)
PAKET=$(realpath "${1:?Aufruf: $0 <paketordner>}")
AUS="$WURZEL/build/pakettest"
[ -f "$PAKET/boot.img.gz" ] && [ -f "$PAKET/manifest.json" ] || { echo "Paketordner unvollständig." >&2; exit 2; }
docker build -q -t diagnostiker-anwender "$HIER/anwender-image" >/dev/null
rm -rf "$AUS"; mkdir -p "$AUS"; truncate -s 6G "$AUS/stick.img"; truncate -s 6G "$AUS/stick2.img"
SHA=$(sha256sum "$PAKET/boot.img.gz" | cut -d' ' -f1)
BYTES=$(python3 -c "import json,sys; print([d for d in json.load(open(sys.argv[1]))['dateien'] if d['art']=='boot'][0]['entpacktGroesse'])" "$PAKET/manifest.json")

docker run --rm --privileged -e SHA="$SHA" -e BYTES="$BYTES" \
    -v "$PAKET":/p:ro -v "$AUS":/t -v "$WURZEL/stick/paket-schreiben.sh":/h.sh:ro diagnostiker-anwender bash -c '
i=0; while [ $i -lt 256 ]; do [ -e /dev/loop$i ] || mknod /dev/loop$i b 7 $i; i=$((i + 1)); done
fehler=0
ok()  { echo "   ok      $1"; }
nok() { echo "   FEHLER  $1"; fehler=1; }
# Ohne udev entstehen die Partitionsknoten nicht von selbst.
knoten() { while true; do for d in /sys/class/block/$1p*; do [ -e "$d/dev" ] || continue; n=$(basename "$d"); [ -e /dev/$n ] || mknod /dev/$n b $(cut -d: -f1 $d/dev) $(cut -d: -f2 $d/dev); done; sleep 0.2; done; }

erneuern() { for d in /sys/class/block/$1p*; do n=$(basename "$d"); rm -f /dev/$n; mknod /dev/$n b $(cut -d: -f1 $d/dev) $(cut -d: -f2 $d/dev); done; }
L=$(losetup -f --show -P /t/stick.img); LN=$(basename $L); knoten $LN & KP=$!
verweigert() { # <Beschreibung> <Argumente...>
    b=$1; shift
    if bash /h.sh "$@" >/tmp/v.log 2>&1; then nok "$b: wurde NICHT verweigert"
    elif grep -q "^VERWEIGERT" /tmp/v.log; then ok "$b: $(grep ^VERWEIGERT /tmp/v.log | cut -c13-80)"
    else nok "$b: anderer Fehler: $(tail -1 /tmp/v.log)"; fi
}
echo "== 1. Verweigerungen"
verweigert "Loop-Gerät ohne test"        $L /p/boot.img.gz $SHA $BYTES
verweigert "falsche Prüfsumme"           $L /p/boot.img.gz 0000000000000000000000000000000000000000000000000000000000000000 $BYTES test
verweigert "Abbild zu groß für das Gerät" $L /p/boot.img.gz $SHA 7000000000 test
verweigert "keine Gerätedatei"            /tmp/x /p/boot.img.gz $SHA $BYTES test
verweigert "Partition statt Laufwerk"     /dev/${LN}p1 /p/boot.img.gz $SHA $BYTES test
ln -sf /p/boot.img.gz /tmp/verweis.gz
verweigert "Verweis statt Abbilddatei"    $L /tmp/verweis.gz $SHA $BYTES test
[ -z "$(sfdisk -d $L 2>/dev/null | grep -c start)" ] || [ "$(sfdisk -d $L 2>&1 | grep -c "start=")" = 0 ] && ok "nach den Verweigerungen ist das Laufwerk unberührt" || nok "Laufwerk wurde verändert"

echo "== 2. Schreiben"
bash /h.sh $L /p/boot.img.gz $SHA $BYTES test 2>&1 | grep -v "copied" | tail -12
erneuern $LN
sfdisk --verify $L >/dev/null 2>&1 && ok "Partitionstabelle gültig (inkl. GPT-Sicherungskopie)" || nok "Partitionstabelle ungültig"
NAMEN=$(sfdisk -d $L | sed -n "s/.*name=\"\([^\"]*\)\".*/\1/p" | tr "\n" " ")
[ "$NAMEN" = "BIOSBOOT DIAGBOOT DIAGDATEN " ] && ok "Partitionen BIOSBOOT, DIAGBOOT, DIAGDATEN" || nok "Partitionsnamen: $NAMEN"
[ "$(blkid -s LABEL -s TYPE -o value ${L}p3 | tr "\n" " ")" = "DIAGDATEN exfat " ] && ok "DIAGDATEN ist exFAT" || nok "DIAGDATEN: $(blkid ${L}p3)"
[ "$(blkid -s LABEL -s TYPE -o value ${L}p2 | tr "\n" " ")" = "DIAGBOOT vfat " ] && ok "DIAGBOOT ist FAT32" || nok "DIAGBOOT: $(blkid ${L}p2)"
LETZT=$(sfdisk -d $L | sed -n "s/^last-lba: *//p"); P3E=$(sfdisk -d $L | sed -n "s|^${L}p3 : start= *\([0-9]*\), size= *\([0-9]*\).*|\1 \2|p" | awk "{print \$1 + \$2 - 1}")
[ "$P3E" -le "$LETZT" ] && [ "$((LETZT - P3E))" -lt 2048 ] && ok "DIAGDATEN reicht bis auf die 1-MiB-Ausrichtung ans Laufwerksende (Ende $P3E, nutzbar bis $LETZT)" || nok "DIAGDATEN endet bei $P3E, nutzbar bis $LETZT"
sfdisk -d $L | grep -Eo "^/dev/[^ ]+ : start= *[0-9]+, size= *[0-9]+" | sed "s/^/   /" | tail -3
sfdisk -d $L | grep -E "^(label-id|/dev.*uuid)" | sed -E "s/.*(uuid=|label-id: )//" | sort > /t/guid1.txt

echo "== 3. Zweiter Stick aus demselben Paket: andere GUIDs"
L2=$(losetup -f --show -P /t/stick2.img); LN2=$(basename $L2); knoten $LN2 & KP2=$!
bash /h.sh $L2 /p/boot.img.gz $SHA $BYTES test >/dev/null 2>&1
sfdisk -d $L2 | grep -E "^(label-id|/dev.*uuid)" | sed -E "s/.*(uuid=|label-id: )//" | sort > /t/guid2.txt
[ -n "$(cat /t/guid1.txt)" ] && [ -z "$(comm -12 /t/guid1.txt /t/guid2.txt)" ] && ok "keine gemeinsamen GUIDs" || nok "gleiche GUIDs auf beiden Sticks"
kill $KP2 2>/dev/null; losetup -d $L2

echo "== 4. Datenpartition füllen"
mkdir -p /m && mount -t exfat ${L}p3 /m && tar -xzf /p/daten.tar.gz -C /m --no-same-owner --no-same-permissions && ls /m | tr "\n" " " && echo && umount /m && ok "daten.tar.gz entpackt" || nok "Entpacken fehlgeschlagen"
kill $KP 2>/dev/null; losetup -d $L
exit $fehler'

echo "== 5. Start in QEMU"
if command -v qemu-system-x86_64 >/dev/null; then
    "$WURZEL/tests/boot/boot-test.sh" "$AUS/stick.img" bios | tail -3
    "$WURZEL/tests/boot/boot-test.sh" "$AUS/stick.img" uefi-sb | tail -3
else echo "   qemu fehlt, Startteil übersprungen"; fi
echo "PAKETTEST_FERTIG"
