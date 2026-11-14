#!/bin/sh
# Baut ein signiertes Paket (build/release/<version>/) aus dem aktuellen Stand und lädt es auf Wunsch zu GitHub hoch.
# Aufruf aus dem Projektordner:
#   release/bauen.sh [--version 2026.11.14] [--veroeffentlichen <besitzer/repo>]
# Voraussetzungen: das Live-System-ISO (build/out/diagnostiker.iso, basis/debian/bauen.sh), Docker, .NET-SDK 10,
# der private Signaturschlüssel in ~/.config/diagnostiker-release/signatur.key (oder DIAGNOSTIKER_SCHLUESSEL), für das Hochladen `gh`.
# Es wird nichts abgefragt: Das Signieren läuft ohne Rückfrage.
set -eu

HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/.." && pwd)
BUILD="$WURZEL/build"
VERSION=$(date +%Y.%m.%d); REPO=""
while [ $# -gt 0 ]; do
    case "$1" in
        --version) VERSION=${2:?}; shift 2 ;;
        --veroeffentlichen) REPO=${2:?}; shift 2 ;;
        *) echo "Unbekannte Angabe: $1" >&2; exit 2 ;;
    esac
done
SCHLUESSEL=${DIAGNOSTIKER_SCHLUESSEL:-$HOME/.config/diagnostiker-release/signatur.key}
[ -f "$SCHLUESSEL" ] || { echo "Signaturschlüssel fehlt: $SCHLUESSEL (erzeugen: dotnet run --project src/Diagnostiker.Stick -- schluessel-erzeugen <datei>)" >&2; exit 1; }
[ -f "$BUILD/out/diagnostiker.iso" ] || { echo "Kein ISO: $BUILD/out/diagnostiker.iso (zuerst basis/debian/bauen.sh)" >&2; exit 1; }
command -v docker >/dev/null || { echo "Docker fehlt." >&2; exit 1; }
if ! command -v dotnet >/dev/null; then [ -x "$HOME/.dotnet/dotnet" ] && PATH="$HOME/.dotnet:$PATH"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
BILD=diagnostiker-basis-debian
AUS="$BUILD/release/$VERSION"
rm -rf "$AUS"; mkdir -p "$AUS" "$BUILD/release"

echo "== 1. Datenpartition zusammenstellen"
"$WURZEL/stick/staging.sh" >/dev/null
echo "== 2. Boot-Abbild bauen"
docker build -q -t "$BILD" "$WURZEL/basis/debian" >/dev/null
docker run --rm --privileged \
    -v "$BUILD/out/diagnostiker.iso":/in.iso:ro -v "$BUILD/release":/aus -v "$HIER/../stick/boot-abbild-bauen.sh":/b.sh:ro \
    "$BILD" sh /b.sh "/aus/boot-$VERSION.img" /in.iso 2560 | tail -1
echo "== 3. Paket packen und signieren"
dotnet run --project "$WURZEL/src/Diagnostiker.Stick" -c Release -- paket-bauen \
    "$BUILD/release/boot-$VERSION.img" "$BUILD/stick-staging" "$AUS" "$VERSION" "$SCHLUESSEL" --bootmib 2560
rm -f "$BUILD/release/boot-$VERSION.img"

if [ -n "$REPO" ]; then
    command -v gh >/dev/null || { echo "gh fehlt." >&2; exit 1; }
    echo "== 4. Hochladen nach $REPO (Version $VERSION)"
    gh release create "v$VERSION" "$AUS"/boot.img.gz "$AUS"/daten.tar.gz "$AUS"/manifest.json "$AUS"/manifest.json.sig \
        --repo "$REPO" --title "Diagnostiker $VERSION" --notes "Diagnostiker-Paket $VERSION. Zum Erstellen eines Sticks das Frontend benutzen."
fi
echo "PAKET_FERTIG $AUS"
