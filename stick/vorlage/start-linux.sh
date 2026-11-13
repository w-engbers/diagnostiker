#!/bin/sh
# Startet den Diagnostiker auf einem laufenden Linux (mit sudo). Das Programm wird nach /tmp kopiert,
# weil Stick-Dateisysteme (exFAT/NTFS) oft ohne Ausführungsrecht eingebunden sind.
D=$(dirname "$(readlink -f "$0")")
B=$(mktemp -d /tmp/diagnostiker.XXXXXX) || exit 1
cp "$D/bin/linux-x64/Diagnostiker" "$B/diagnostiker" && chmod +x "$B/diagnostiker" || exit 1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_BUNDLE_EXTRACT_BASE_DIR="$B/x"
if [ "$(id -u)" -eq 0 ]; then
    "$B/diagnostiker" --module "$D/module" --protokolle "$D/protokolle"
else
    sudo -E "$B/diagnostiker" --module "$D/module" --protokolle "$D/protokolle"
fi
rm -rf "$B"
