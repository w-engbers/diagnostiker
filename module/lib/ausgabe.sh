#!/bin/sh
# Gemeinsame Ausgabefunktionen der Prüfskripte: Zeilen mit [OK], [INFO], [WARNUNG] oder [FEHLER], die die Regeln der Manifeste auswerten.
LC_ALL=C
export LC_ALL
ok()   { echo "[OK] $*"; }
info() { echo "[INFO] $*"; }
warn() { echo "[WARNUNG] $*"; }
fehl() { echo "[FEHLER] $*"; }
