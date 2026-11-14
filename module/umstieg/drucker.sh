#!/bin/sh
# Drucker vor einem Umstieg von Windows auf Linux: Welche Drucker sind unter Windows eingerichtet, und gibt es dafür unter Linux einen Treiber?
# (Linux-Live-System, nur lesend, Root nötig.)
#   1. Die in Windows eingerichteten Drucker werden OFFLINE aus der Windows-Registrierung gelesen (Hive SYSTEM, Schlüssel Print\Printers);
#      bei BitLocker öffnet das Modul die Partition mit dem Wiederherstellungsschlüssel (siehe lib/windows.sh).
#   2. Jeder Drucker wird gegen die Treiberdatenbank des Live-Systems (CUPS, lpinfo -m) abgeglichen.
#   3. Zusätzlich werden jetzt angeschlossene oder im Netz sichtbare Drucker aufgelistet (lpinfo -v).
# Ob ein Drucker wirklich funktioniert, zeigt erst ein Test mit dem Drucker selbst (anschließen, Testseite drucken).
# Ausgabe: Zeilen mit [OK], [INFO], [WARNUNG] oder [FEHLER]; die Regeln des Manifests werten diese Marken aus.
LC_ALL=C
export LC_ALL

NF=0
ok()   { echo "[OK] $*"; }
info() { echo "[INFO] $*"; }
warn() { echo "[WARNUNG] $*"; NF=$((NF+1)); }
fehl() { echo "[FEHLER] $*"; NF=$((NF+1)); }

if [ "$(id -u)" != 0 ] && [ -z "$DIAGNOSTIKER_EINHAENGEN" ]; then echo KEINE_RECHTE; exit 0; fi
. "$(dirname "$0")/../lib/windows.sh"

# ---- Treiberdatenbank (CUPS) ---------------------------------------------------------------------------------------------------
HAT_DB=0
if command -v lpinfo >/dev/null 2>&1; then
    lpinfo -m > "$tmp/modelle" 2>/dev/null
    if [ ! -s "$tmp/modelle" ]; then
        # Der Druckdienst läuft im Live-System nicht immer schon: einmal starten und noch einmal fragen.
        systemctl start cups >/dev/null 2>&1; sleep 3
        lpinfo -m > "$tmp/modelle" 2>/dev/null
    fi
    [ -s "$tmp/modelle" ] && HAT_DB=1
fi
[ "$HAT_DB" = 1 ] || warn "Die Treiberdatenbank für Drucker (CUPS) ist im Live-System nicht verfügbar: Die Drucker lassen sich nur auflisten, nicht bewerten."

# Marke und Modellnummer aus einem Druckernamen: "Brother HL-L2350DW series" -> marke=brother, modell=hl-l2350dw
modell_token() {
    printf '%s\n' "$1" | awk '{ for (i = 1; i <= NF; i++) if ($i ~ /[0-9]/ && length($i) >= 3 && $i !~ /^[0-9]+[.][0-9]+$/) { print $i; exit } }'
}
marke_token() { printf '%s\n' "$1" | awk '{ print tolower($1) }' | tr -cd 'a-z0-9'; }
# Erste Zeile der Treiberdatenbank, in der die Modellnummer als eigenes Wort vorkommt (und, wenn bekannt, die Marke).
# "M15" passt auf "M15w", aber nicht auf "M1522n": Das Wort der Datenbank darf höchstens zwei Buchstaben länger oder kürzer sein.
treiber_suche() { # Name
    [ "$HAT_DB" = 1 ] || return 1
    modell=$(modell_token "$1" | tr 'A-Z' 'a-z' | tr -cd 'a-z0-9')
    [ -n "$modell" ] || return 1
    marke=$(marke_token "$1")
    suche() { # Marke ("" = egal)
        awk -v m="$modell" -v b="$1" '
            function norm(s) { s = tolower(s); gsub(/[^a-z0-9]/, "", s); return s }
            function passt(t, m,   d) {
                if (t == m) return 1
                if (index(t, m) == 1) { d = substr(t, length(m) + 1); return (length(d) <= 2 && d ~ /^[a-z]+$/) }
                if (length(t) >= 4 && index(m, t) == 1) { d = substr(m, length(t) + 1); return (length(d) <= 2 && d ~ /^[a-z]+$/) }
                return 0
            }
            { n = split($0, w, /[ ,\/()_.:]+/); gef = 0; brand = (b == "")
              for (i = 1; i <= n; i++) { t = norm(w[i]); if (t == "") continue; if (passt(t, m)) gef = 1; if (b != "" && index(t, b) == 1) brand = 1 }
              if (gef && brand) { print $0; exit } }' "$tmp/modelle"
    }
    t=$(suche "$marke")
    [ -n "$t" ] || t=$(suche "")
    [ -n "$t" ] || return 1
    # Kurzform: Beschreibung ohne die Treiberkennung am Anfang
    printf '%s' "$t" | sed 's/^[^ ]* //' | cut -c1-110
}

bewerte_drucker() { # Anzeigename, Treibername, Anschluss
    name=$1; treiber=$2; port=$3
    case "$port" in
        USB*|usb*)                         art="USB" ;;
        IP_*|[0-9]*.[0-9]*.[0-9]*.[0-9]*|TCP*|BRN*|WSD*|http*|ipp*) art="Netzwerk" ;;
        *)                                 art="Anschluss $port" ;;
    esac
    # Windows-eigene Spezialtreiber (GDI/SPL/Host-based) gibt es unter Linux nicht
    if printf '%s' "$treiber $name" | grep -qiE 'GDI|host[- ]?based|\bSPL\b|CAPT\b'; then
        warn "Drucker „$name“ ($art): ein reiner Windows-Drucker (GDI/Host-based, Treiber „$treiber“). Unter Linux meist nicht nutzbar: den Drucker durch einen Netzwerk- oder PostScript-fähigen ersetzen oder vorher beim Hersteller und auf openprinting.org nachsehen."
        return
    fi
    if t=$(treiber_suche "$treiber"); [ -n "$t" ]; then
        ok "Drucker „$name“ ($art): Treiber im Live-System vorhanden: $t."
    elif t=$(treiber_suche "$name"); [ -n "$t" ]; then
        ok "Drucker „$name“ ($art): Treiber im Live-System vorhanden: $t."
    elif [ "$art" = Netzwerk ]; then
        info "Drucker „$name“ ($art): kein eigener Treiber in der Datenbank gefunden, aber Netzwerkdrucker arbeiten meist ohne Treiber (IPP/AirPrint). Mit dem Drucker testen."
    else
        warn "Drucker „$name“ ($art): kein Treiber im Live-System gefunden. Auf der Seite des Herstellers und auf openprinting.org nachsehen; neuere USB-Drucker laufen oft treiberlos (IPP über USB), und viele Laserdrucker lassen sich mit einem generischen PCL- oder PostScript-Treiber betreiben. Mit dem Drucker testen."
    fi
}

# Virtuelle Drucker (PDF, XPS, Fax, OneNote …) zählen nicht
virtuell() { # Name, Anschluss
    printf '%s|%s' "$1" "$2" | grep -qiE 'Microsoft Print to PDF|Microsoft XPS|OneNote|Fax|PDF|Snagit|Send to|Document Writer|^[^|]*\|(nul:|PORTPROMPT:|FILE:|XPSPort:|SHRFAX:|Microsoft Document Imaging)'
}

analysiere() { # Einhängepunkt, Gerät
    w=$1; dev=$2
    wdir=$(pfad_ci "$w" windows) || return 1
    s32=$(pfad_ci "$wdir" system32) || return 1
    hive=$(pfad_ci "$s32" config system) || return 1
    gefunden=$((gefunden+1))
    echo
    info "Windows-Installation auf $dev: eingerichtete Drucker (aus der Windows-Registrierung)."
    # Aktuellen Satz von Steuerungseinstellungen lesen (meist ControlSet001)
    aktuell=$(hivexget "$hive" '\Select' Current 2>/dev/null | tr -cd '0-9')
    [ -n "$aktuell" ] || aktuell=1
    satz=$(printf 'ControlSet%03d' "$aktuell")
    schluessel="\\$satz\\Control\\Print\\Printers"
    printf 'cd %s\nls\n' "$schluessel" | hivexsh "$hive" 2>/dev/null | sed 's/\r$//' > "$tmp/druckernamen"
    gesamt=$(grep -c . "$tmp/druckernamen")
    anzahl=0; virtuelle=0
    while IFS= read -r name; do
        [ -n "$name" ] || continue
        treiber=$(hivexget "$hive" "$schluessel\\$name" "Printer Driver" 2>/dev/null)
        port=$(hivexget "$hive" "$schluessel\\$name" "Port" 2>/dev/null)
        if virtuell "$name" "$port"; then virtuelle=$((virtuelle+1)); continue; fi
        anzahl=$((anzahl+1))
        bewerte_drucker "$name" "${treiber:-$name}" "$port"
    done < "$tmp/druckernamen"
    info "In der Windows-Registrierung ($satz) gelesen: $gesamt Druckerwarteschlange(n), davon $virtuelle virtuelle (PDF, XPS, Fax, OneNote …)."
    [ "$anzahl" -gt 0 ] || info "Auf $dev sind keine echten Drucker eingerichtet (nur virtuelle wie „Microsoft Print to PDF“ oder gar keine)."
    return 0
}

if command -v hivexsh >/dev/null 2>&1 && command -v hivexget >/dev/null 2>&1; then
    windows_durchlaufen analysiere
    windows_abschluss
else
    info "Die Drucker aus Windows lassen sich nicht lesen: hivexsh (libhivex-bin) fehlt im Live-System."
fi

# ---- Jetzt angeschlossene oder im Netz sichtbare Drucker --------------------------------------------------------------------------
if command -v lpinfo >/dev/null 2>&1; then
    lpinfo -v 2>/dev/null | grep -E '^(direct usb://|network (dnssd|ipp|ipps|socket|lpd)://)' > "$tmp/geraete"
    if [ -s "$tmp/geraete" ]; then
        echo
        info "Drucker, die jetzt am Rechner angeschlossen oder im Netz sichtbar sind:"
        while IFS= read -r z; do
            uri=${z#* }
            name=$(printf '%s' "$uri" | sed 's#^[a-z]*://##; s#[?].*##; s#/$##; s#%20# #g; s#\._ipp.*##; s#\._ipps.*##; s#\._pdl-datastream.*##; s#\._printer.*##; s#/# #g')
            case "$uri" in
                usb://*)   art="USB" ;;
                dnssd://*) art="Netzwerk (Bonjour)" ;;
                *)         art="Netzwerk" ;;
            esac
            if t=$(treiber_suche "$name"); [ -n "$t" ]; then ok "Gefunden: $name ($art): Treiber im Live-System vorhanden: $t."
            else
                case "$uri" in
                    dnssd://*_ipp*|ipp://*|ipps://*) info "Gefunden: $name ($art): spricht IPP und arbeitet meist ohne Treiber (AirPrint-ähnlich). Mit einer Testseite prüfen." ;;
                    *) warn "Gefunden: $name ($art): kein Treiber in der Datenbank. Mit dem Drucker testen und auf openprinting.org nachsehen." ;;
                esac
            fi
        done < "$tmp/geraete"
    fi
fi

echo
info "Hinweis: Diese Prüfung vergleicht nur Namen mit der Treiberdatenbank. Ob ein Drucker (und Scanner) wirklich funktioniert, zeigt erst ein Test: Drucker anschließen oder einschalten, im Live-System einrichten und eine Testseite drucken."
exit 0
