#!/bin/sh
# Schreibt den Stick auf ein LAUFWERK. Wird vom Stick-Manager aufgerufen, nachdem der Mensch bestätigt hat.
# Aufruf:  BESTAETIGT_LAUFWERK=/dev/sdX stick/schreiben.sh /dev/sdX
# Ohne die Umgebungsvariable mit genau diesem Pfad verweigert schon dieses Skript, und der Container prüft es noch einmal.
# Alles auf dem Laufwerk wird gelöscht.
set -eu

HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/.." && pwd)
BUILD="$WURZEL/build"
ISO="$BUILD/out/diagnostiker.iso"
STAGING="$BUILD/stick-staging"
BILD=diagnostiker-basis-debian

LAUFWERK=${1:?Aufruf: BESTAETIGT_LAUFWERK=/dev/sdX stick/schreiben.sh /dev/sdX}
[ "${BESTAETIGT_LAUFWERK:-}" = "$LAUFWERK" ] || { echo "VERWEIGERT: keine Bestätigung für $LAUFWERK" >&2; exit 3; }
case "$LAUFWERK" in /dev/sd[a-z]|/dev/sd[a-z][a-z]|/dev/nvme[0-9]n[0-9]|/dev/mmcblk[0-9]) ;; *) echo "VERWEIGERT: unerwarteter Gerätename" >&2; exit 3 ;; esac
[ -b "$LAUFWERK" ] || { echo "VERWEIGERT: $LAUFWERK ist kein Blockgerät" >&2; exit 3; }
command -v docker >/dev/null || { echo "Docker fehlt." >&2; exit 1; }

"$HIER/staging.sh"
echo "== Bauumgebung (Docker-Image)"
docker build -q -t "$BILD" "$WURZEL/basis/debian" >/dev/null

echo "== Stick schreiben: $LAUFWERK"
docker run --rm --privileged \
    -v "$ISO":/eingabe/diagnostiker.iso:ro \
    -v "$STAGING":/staging:ro \
    -v "$HIER/stick-erstellen.sh":/stick-erstellen.sh:ro \
    -e BESTAETIGT_LAUFWERK="$BESTAETIGT_LAUFWERK" \
    "$BILD" sh /stick-erstellen.sh "$LAUFWERK" 0 /eingabe/diagnostiker.iso /staging
