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
[ -f "$ISO" ] || { echo "Kein ISO: $ISO (zuerst basis/debian/bauen.sh)" >&2; exit 1; }
if ! command -v dotnet >/dev/null; then [ -x "$HOME/.dotnet/dotnet" ] && PATH="$HOME/.dotnet:$PATH"; fi
command -v dotnet >/dev/null || { echo ".NET-SDK 10 fehlt." >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "== Datenpartition zusammenstellen: $STAGING"
rm -rf "$STAGING"
mkdir -p "$STAGING/bin/win-x64" "$STAGING/bin/linux-x64" "$STAGING/protokolle" \
         "$STAGING/werkzeuge/win-x64" "$STAGING/werkzeuge/linux-x64" "$AUSGABE"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
for rid in win-x64 linux-x64; do
    dotnet publish "$WURZEL/src/Diagnostiker.Tui" -c Release -r "$rid" --self-contained \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
        -o "$TMP/$rid" >/dev/null
done
cp "$TMP/win-x64/Diagnostiker.Tui.exe" "$STAGING/bin/win-x64/Diagnostiker.exe"
cp "$TMP/linux-x64/Diagnostiker.Tui"   "$STAGING/bin/linux-x64/Diagnostiker"
cp -a "$WURZEL/module" "$STAGING/module"
cp "$HIER"/vorlage/* "$STAGING/"

# stick.json: Stand der Teile mit Prüfsummen (Grundlage für das spätere Aktualisieren).
summe() { sha256sum "$1" | cut -d' ' -f1; }
VERSION=$(git -C "$WURZEL" describe --always --dirty 2>/dev/null || echo unbekannt)
MODULSUMME=$(cd "$STAGING/module" && find . -type f | LC_ALL=C sort | xargs sha256sum | sha256sum | cut -d' ' -f1)
cat > "$STAGING/stick.json" <<EOF
{
  "schema": 1,
  "erstellt": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "teile": {
    "basis":    { "name": "debian-trixie", "sha256": "$(summe "$ISO")" },
    "programm": { "version": "$VERSION",
                  "win-x64":   "$(summe "$STAGING/bin/win-x64/Diagnostiker.exe")",
                  "linux-x64": "$(summe "$STAGING/bin/linux-x64/Diagnostiker")" },
    "module":   { "sha256": "$MODULSUMME" },
    "werkzeuge": {}
  }
}
EOF

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
