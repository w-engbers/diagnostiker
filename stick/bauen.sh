#!/bin/sh
# Baut ein Stick-Abbild (Datei) aus dem Live-ISO und dem Prüfprogramm.
# Aufruf aus dem Projektordner:  stick/bauen.sh [groesse-GiB]     (Standard 6)
# Voraussetzung: build/out/diagnostiker.iso (basis/debian/bauen.sh), Docker, .NET-SDK 10.
# Ergebnis: build/stick/diagnostiker-stick.img. Es wird NICHTS auf ein echtes Laufwerk geschrieben.
set -eu

HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/.." && pwd)
BUILD="$WURZEL/build"
ISO="$BUILD/out/diagnostiker.iso"
STAGING="$BUILD/stick-staging"
AUSGABE="$BUILD/stick"
GIB=${1:-6}
BILD=diagnostiker-basis-debian

command -v docker >/dev/null || { echo "Docker fehlt." >&2; exit 1; }
"$HIER/staging.sh"
mkdir -p "$AUSGABE"

echo "== Bauumgebung (Docker-Image)"
docker build -q -t "$BILD" "$WURZEL/basis/debian" >/dev/null

echo "== Abbild bauen"
docker run --rm --privileged \
    -v "$ISO":/eingabe/diagnostiker.iso:ro \
    -v "$STAGING":/staging:ro \
    -v "$AUSGABE":/ausgabe \
    -v "$HIER/stick-erstellen.sh":/stick-erstellen.sh:ro \
    -e WIRT_UID="$(id -u)" -e WIRT_GID="$(id -g)" \
    "$BILD" sh -c 'sh /stick-erstellen.sh /ausgabe/diagnostiker-stick.img '"$GIB"' /eingabe/diagnostiker.iso /staging && chown "$WIRT_UID:$WIRT_GID" /ausgabe/diagnostiker-stick.img'
ls -lh "$AUSGABE/diagnostiker-stick.img"
