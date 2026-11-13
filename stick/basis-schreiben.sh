#!/bin/sh
# Ersetzt das Live-System (DIAGBOOT) eines vorhandenen Sticks. Wird vom Stick-Manager aufgerufen, nachdem der Mensch bestätigt hat.
# Aufruf:  BESTAETIGT_LAUFWERK=/dev/sdX stick/basis-schreiben.sh /dev/sdX
# Ohne die Umgebungsvariable mit genau diesem Pfad verweigert schon dieses Skript, und der Container prüft es noch einmal.
# DIAGDATEN (Programme, Module, Protokolle) wird nicht berührt.
set -eu

HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/.." && pwd)
ISO=${DIAGNOSTIKER_ISO:-$WURZEL/build/out/diagnostiker.iso}
BILD=diagnostiker-basis-debian

LAUFWERK=${1:?Aufruf: BESTAETIGT_LAUFWERK=/dev/sdX stick/basis-schreiben.sh /dev/sdX}
[ "${BESTAETIGT_LAUFWERK:-}" = "$LAUFWERK" ] || { echo "VERWEIGERT: keine Bestätigung für $LAUFWERK" >&2; exit 3; }
case "$LAUFWERK" in /dev/sd[a-z]|/dev/sd[a-z][a-z]|/dev/nvme[0-9]n[0-9]|/dev/mmcblk[0-9]|/dev/loop[0-9]*) ;; *) echo "VERWEIGERT: unerwarteter Gerätename" >&2; exit 3 ;; esac
[ -b "$LAUFWERK" ] || { echo "VERWEIGERT: $LAUFWERK ist kein Blockgerät" >&2; exit 3; }
[ -f "$ISO" ] || { echo "Kein ISO: $ISO" >&2; exit 2; }
command -v docker >/dev/null || { echo "Docker fehlt." >&2; exit 1; }

echo "== Bauumgebung (Docker-Image)"
docker build -q -t "$BILD" "$WURZEL/basis/debian" >/dev/null

echo "== Live-System auf $LAUFWERK aktualisieren"
docker run --rm --privileged \
    -v "$ISO":/eingabe/diagnostiker.iso:ro \
    -v "$HIER/basis-aktualisieren.sh":/basis-aktualisieren.sh:ro \
    -e BESTAETIGT_LAUFWERK="$BESTAETIGT_LAUFWERK" \
    -e DIAGNOSTIKER_TESTLAUFWERK="${DIAGNOSTIKER_TESTLAUFWERK:-}" \
    "$BILD" sh /basis-aktualisieren.sh "$LAUFWERK" /eingabe/diagnostiker.iso
