#!/bin/sh
# Gemeinsame Hilfen der Prüfmodule, die in einer Windows-Installation des Rechners lesen (Linux-Live-System, nur lesend):
# Einhängen der NTFS-Partitionen, Entsperren von BitLocker mit dem Wiederherstellungsschlüssel (Umgebungsvariable
# DIAGNOSTIKER_PARAM_SCHLUESSEL, nur lesend, der Schlüssel erscheint in keiner Ausgabe), Suchen von Pfaden ohne Beachtung der
# Groß-/Kleinschreibung. Wird mit  . "$(dirname "$0")/../lib/windows.sh"  eingebunden.
# Erwartet vom aufrufenden Skript: die Funktionen ok, info, warn, fehl und die Zähler-Variable NF.
#
# windows_durchlaufen <Funktion>: ruft <Funktion> <Einhängepunkt> <Gerät> für jede lesbare Partition auf; die Funktion
# gibt 1 zurück, wenn dort keine Windows-Installation liegt. Danach windows_abschluss aufrufen.
# Testhilfe: DIAGNOSTIKER_EINHAENGEN=<Ordner> verwendet <Ordner>/<Gerätename> statt zu mounten (auch für /dev/mapper/…).

val() { printf ' %s\n' "$1" | sed -n "s/.* $2=\"\\([^\"]*\\)\".*/\\1/p" | head -1; }

tmp=$(mktemp -d)
GEHAENGT=0; OFFEN=""
aushaengen() { [ "$GEHAENGT" = 1 ] && umount "$tmp/m" 2>/dev/null; GEHAENGT=0; }
schliessen() { [ -n "$OFFEN" ] && cryptsetup close "$OFFEN" 2>/dev/null; OFFEN=""; }
trap 'aushaengen; schliessen; rm -rf "$tmp"' EXIT

einhaengen() { # Gerät -> W (nur lesend)
    W=
    if [ -n "$DIAGNOSTIKER_EINHAENGEN" ]; then
        W="$DIAGNOSTIKER_EINHAENGEN/$(basename "$1")"
        [ -d "$W" ] && return 0
        W=; return 1
    fi
    mp=$(findmnt -rno TARGET -S "$1" 2>/dev/null | head -1)
    if [ -n "$mp" ]; then W=$mp; return 0; fi
    mkdir -p "$tmp/m"
    if mount -t ntfs-3g -o ro "$1" "$tmp/m" 2>/dev/null; then GEHAENGT=1; W="$tmp/m"; return 0; fi
    return 1
}

# pfad_ci <Ordner> <Name> [<Name> …]: Pfad ohne Beachtung der Groß-/Kleinschreibung (Windows: "Windows", "System32" …)
pfad_ci() {
    p=$1; shift
    for teil in "$@"; do
        n=$(find "$p" -maxdepth 1 -iname "$teil" 2>/dev/null | head -1)
        [ -n "$n" ] || return 1
        p=$n
    done
    printf '%s' "$p"
}
datum() { stat -c %y "$1" 2>/dev/null | cut -c1-16; }

lsblk_aus=$(lsblk -Pnpo NAME,TYPE,FSTYPE,SIZE 2>/dev/null)
[ -n "$lsblk_aus" ] || { echo "KEINE_DATENTRAEGER"; exit 0; }

# Wiederherstellungsschlüssel: nur Ziffern, 48 Stück, in Gruppen zu je 6 (Schreibweise, die cryptsetup erwartet)
SCHLUESSEL=$(printf '%s' "${DIAGNOSTIKER_PARAM_SCHLUESSEL:-}" | tr -cd '0-9')
SCHLUESSEL_VORHANDEN=0; FORMAT_OK=1
if [ -n "$SCHLUESSEL" ]; then
    SCHLUESSEL_VORHANDEN=1
    if [ "${#SCHLUESSEL}" = 48 ]; then
        SCHLUESSEL=$(printf '%s' "$SCHLUESSEL" | sed 's/\(.\{6\}\)/\1-/g; s/-$//')
    else
        FORMAT_OK=0
    fi
fi

gefunden=0; verschluesselt_zu=0; nr=0


windows_durchlaufen() {
    analysiere=$1
    echo "$lsblk_aus" > "$tmp/alle"
    while read -r z; do
        typ=$(val "$z" TYPE); fs=$(val "$z" FSTYPE); dev=$(val "$z" NAME); groesse=$(val "$z" SIZE)
        [ "$typ" = part ] || continue
        nr=$((nr+1))
        case "$fs" in
            ntfs) einhaengen "$dev" && { $analysiere "$W" "$dev"; aushaengen; } ;;
            BitLocker)
                if [ "$SCHLUESSEL_VORHANDEN" = 0 ]; then
                    verschluesselt_zu=$((verschluesselt_zu+1))
                    info "Partition $dev ($groesse) ist mit BitLocker verschlüsselt und bleibt ohne Wiederherstellungsschlüssel ungeprüft."
                elif [ "$FORMAT_OK" = 0 ]; then
                    fehl "Der eingegebene Wiederherstellungsschlüssel hat nicht 48 Ziffern (6 Gruppen zu je 6 Ziffern, acht Gruppen): bitte neu eingeben."
                else
                    name="diag-bl$nr"
                    if fehlermeldung=$(printf '%s' "$SCHLUESSEL" | cryptsetup bitlkOpen --readonly --key-file=- "$dev" "$name" 2>&1); then
                        OFFEN=$name
                        if einhaengen "/dev/mapper/$name"; then
                            $analysiere "$W" "$dev (entsperrt)" || info "Auf $dev (entsperrt) liegt keine Windows-Installation."
                            aushaengen
                        else
                            warn "Die entsperrte Partition $dev lässt sich nicht einhängen (Dateisystem beschädigt?)."
                        fi
                        schliessen
                    else
                        fehl "Partition $dev lässt sich nicht entsperren: Der Schlüssel passt nicht zu diesem Laufwerk oder es ist beschädigt ($(printf '%s' "$fehlermeldung" | head -1 | cut -c1-120))."
                    fi
                fi ;;
        esac
    done < "$tmp/alle"
}

windows_abschluss() {
    # Hinweis, wenn nirgends eine Windows-Installation geprüft werden konnte
    if [ "$gefunden" = 0 ]; then
        echo
        if [ "$verschluesselt_zu" -gt 0 ]; then
            warn "Es wurde keine Windows-Installation geprüft: Die BitLocker-Partition bleibt ohne Wiederherstellungsschlüssel verschlossen. Das Modul mit dem Schlüssel erneut ausführen (er steht im Microsoft-Konto des Besitzers: account.microsoft.com/devices/recoverykey)."
        else
            info "Keine Windows-Installation auf den Datenträgern gefunden."
        fi
    fi
}
