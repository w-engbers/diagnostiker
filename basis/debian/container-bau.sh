#!/bin/sh
# Läuft IM Container als root. Erwartet:
#   /quelle  (nur lesend)  die live-build-Konfiguration (auto/, config/), das Programm ist darin schon eingefügt
#   /bau     (Volume)      Arbeitsverzeichnis; der Paket-Cache bleibt dort zwischen den Läufen erhalten
#   /ausgabe (Wirtsordner) Ergebnis: diagnostiker.iso und .sha256
# Umgebung: WIRT_UID und WIRT_GID für den Besitzer der Ergebnisdateien.
#
# Der chroot wird bei jedem Lauf neu aufgesetzt (lb clean): live-build entfernt nach einem Lauf die
# Kernel-Dateien aus dem chroot, ein Teil-Rebuild schlägt deshalb fehl. Der Cache bleibt erhalten.
set -eu

cd /bau
mkdir -p live
cd live
[ -d auto ] && lb clean >/dev/null 2>&1 || true
rm -rf auto config
cp -a /quelle/auto /quelle/config .

chmod +x auto/config config/hooks/normal/* config/includes.chroot/usr/local/bin/diagnostiker-start
[ -f config/includes.chroot/opt/diagnostiker/bin/Diagnostiker.Tui ] \
    && chmod +x config/includes.chroot/opt/diagnostiker/bin/Diagnostiker.Tui

lb config
lb build

ISO=live-image-amd64.hybrid.iso
[ -f "$ISO" ] || { echo "FEHLER: kein ISO erzeugt" >&2; exit 1; }

mkdir -p /ausgabe
cp "$ISO" /ausgabe/diagnostiker.iso
( cd /ausgabe && sha256sum diagnostiker.iso > diagnostiker.iso.sha256 )
chown "${WIRT_UID:-0}:${WIRT_GID:-0}" /ausgabe/diagnostiker.iso /ausgabe/diagnostiker.iso.sha256
echo "FERTIG: $(du -h /ausgabe/diagnostiker.iso | cut -f1)  $(cut -c1-16 /ausgabe/diagnostiker.iso.sha256)"
