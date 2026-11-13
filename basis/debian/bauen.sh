#!/bin/sh
# Baut das Live-System-Abbild (ISO) in einem Debian-Container.
# Aufruf aus dem Projektordner:  basis/debian/bauen.sh
# Ergebnis: build/out/diagnostiker.iso (+ .sha256). Braucht: Docker, .NET-SDK 10.
set -eu

HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/../.." && pwd)
BUILD="$WURZEL/build"
QUELLE="$BUILD/quelle"
AUSGABE="$BUILD/out"
BILD=diagnostiker-basis-debian
VOLUME=diagnostiker-bau

command -v docker >/dev/null || { echo "Docker fehlt." >&2; exit 1; }
if ! command -v dotnet >/dev/null; then
    [ -x "$HOME/.dotnet/dotnet" ] && PATH="$HOME/.dotnet:$PATH"
fi
command -v dotnet >/dev/null || { echo ".NET-SDK 10 fehlt (dotnet nicht im PATH)." >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "== Quelle zusammenstellen: $QUELLE"
rm -rf "$QUELLE"
mkdir -p "$QUELLE" "$AUSGABE"
cp -a "$HIER/live/auto" "$HIER/live/config" "$QUELLE/"
PROGRAMM="$QUELLE/config/includes.chroot/opt/diagnostiker"
mkdir -p "$PROGRAMM/bin"
dotnet publish "$WURZEL/src/Diagnostiker.Tui" -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
    -o "$PROGRAMM/bin" >/dev/null
cp -a "$WURZEL/module" "$PROGRAMM/module"

echo "== Bauumgebung (Docker-Image)"
docker build -q -t "$BILD" "$HIER"

echo "== Bau (dauert je nach Verbindung 10 bis 20 Minuten)"
docker run --rm --privileged \
    -v "$QUELLE":/quelle:ro \
    -v "$VOLUME":/bau \
    -v "$AUSGABE":/ausgabe \
    -v "$HIER/container-bau.sh":/container-bau.sh:ro \
    -e WIRT_UID="$(id -u)" -e WIRT_GID="$(id -g)" \
    "$BILD" sh /container-bau.sh
