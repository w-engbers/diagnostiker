#!/bin/sh
# Stellt die Datenpartition des Sticks zusammen (build/stick-staging): Programme für Windows und Linux, Module,
# Starter, leere Ordner und stick.json mit den Prüfsummen. Wird von bauen.sh und schreiben.sh aufgerufen.
# Aufruf aus dem Projektordner:  stick/staging.sh     Braucht das ISO (build/out/diagnostiker.iso) und das .NET-SDK 10.
set -eu

HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/.." && pwd)
BUILD="$WURZEL/build"
ISO="$BUILD/out/diagnostiker.iso"
STAGING="$BUILD/stick-staging"

[ -f "$ISO" ] || { echo "Kein ISO: $ISO (zuerst basis/debian/bauen.sh)" >&2; exit 1; }
if ! command -v dotnet >/dev/null; then [ -x "$HOME/.dotnet/dotnet" ] && PATH="$HOME/.dotnet:$PATH"; fi
command -v dotnet >/dev/null || { echo ".NET-SDK 10 fehlt." >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "== Datenpartition zusammenstellen: $STAGING"
rm -rf "$STAGING"
mkdir -p "$STAGING/bin/win-x64" "$STAGING/bin/linux-x64" "$STAGING/protokolle" \
         "$STAGING/werkzeuge/win-x64" "$STAGING/werkzeuge/linux-x64"
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

# Fremdwerkzeuge (fest gepinnt, geprüft): werkzeuge/holen.sh legt sie unter cache/werkzeuge ab.
"$WURZEL/werkzeuge/holen.sh"
cp -a "$WURZEL/cache/werkzeuge/." "$STAGING/werkzeuge/"

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
    "werkzeuge": { "smartmontools": { "version": "$(python3 -I -c "import json; print(next(w['version'] for w in json.load(open('$WURZEL/werkzeuge/werkzeuge.lock.json'))['werkzeuge'] if w['name']=='smartmontools'))")",
                                     "win-x64": "$(summe "$STAGING/werkzeuge/win-x64/smartctl.exe")" } }
  }
}
EOF
echo "   fertig"
