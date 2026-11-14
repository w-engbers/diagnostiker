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
        IP_*|[0-9]*.[0-9]*.[0-9]*.[0-9]*|TCP*|BRN*|WSD*|http*|ipp*|Ne[0-9]*:*) art="Netzwerk" ;;
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
        warn "Drucker „$name“ ($art): kein passender Treiber in der Datenbank gefunden. Das heißt nicht, dass er nicht geht: Viele neuere Drucker arbeiten treiberlos (IPP, auch über USB), und viele Laserdrucker lassen sich mit einem generischen PCL- oder PostScript-Treiber betreiben. Mit dem Drucker testen und auf der Herstellerseite und auf openprinting.org nachsehen."
    fi
}

# Virtuelle Drucker (PDF, XPS, Fax, OneNote …) zählen nicht
virtuell() { # Name, Anschluss
    printf '%s|%s' "$1" "$2" | grep -qiE 'Microsoft Print to PDF|Microsoft XPS|OneNote|Fax|PDF|Snagit|Send to|Document Writer|^[^|]*\|(nul:|PORTPROMPT:|FILE:|XPSPort:|SHRFAX:|Microsoft Document Imaging)'
}

# Scanner: Netzwerk- und WLAN-Scanner arbeiten unter Linux meist treiberlos über sane-airscan (eSCL/AirScan, auch das „WSD“ vieler Windows-Geräte).
# USB-Scanner brauchen einen SANE-Treiber für das Modell. Offline lässt sich das nicht sicher beurteilen: also Hinweis plus Test mit dem Gerät.
bewerte_scanner() { # Name
    if printf '%s' "$1" | grep -qiE 'camera|kamera|webcam|WIA|virtual'; then return; fi
    info "Scanner/Bildgerät „$1“: unter Linux läuft es meist über SANE: Geräte im WLAN oder Netzwerk (auch Multifunktionsgeräte) arbeiten ohne Treiber über sane-airscan, bei USB-Scannern hängt es vom Modell ab. Mit dem Gerät testen (im Live-System „Scanner im Netz suchen“ oder per USB anschließen)."
}

analysiere() { # Einhängepunkt, Gerät
    w=$1; dev=$2
    wdir=$(pfad_ci "$w" windows) || return 1
    s32=$(pfad_ci "$wdir" system32) || return 1
    hive=$(pfad_ci "$s32" config system) || return 1
    gefunden=$((gefunden+1))
    echo
    info "Windows-Installation auf $dev: eingerichtete Drucker (aus der Windows-Registrierung)."
    # Nur hivexsh benutzen, und zwar im unsicheren Modus (-u): Ein Windows mit "Schnellstart" lässt die Hive-Dateien unsauber zurück,
    # dann verweigert hivexsh ohne -u das Öffnen und hivexget kennt diese Option nicht.
    # Aktuellen Satz von Steuerungseinstellungen lesen (meist ControlSet001)
    hex=$(printf 'cd \\Select\nlsval\n' | hivexsh -u "$hive" 2>/dev/null | sed -n 's/^"Current"=dword:\([0-9a-fA-F]*\)$/\1/p')
    aktuell=1; [ -n "$hex" ] && aktuell=$(printf '%d' "0x$hex")
    satz=$(printf 'ControlSet%03d' "$aktuell")
    schluessel="\\$satz\\Control\\Print\\Printers"
    printf 'cd %s\nls\n' "$schluessel" | hivexsh -u "$hive" 2>"$tmp/hivefehler" | sed 's/\r$//' > "$tmp/druckernamen"
    gesamt=$(grep -c . "$tmp/druckernamen")
    if [ "$gesamt" = 0 ] && [ -s "$tmp/hivefehler" ]; then
        info "Der Schlüssel Control\\Print\\Printers ist in der Registrierung ($satz) nicht lesbar, es folgt ein Rückfall: $(head -1 "$tmp/hivefehler" | cut -c1-150)"
    fi
    anzahl=0; virtuelle=0
    while IFS= read -r name; do
        [ -n "$name" ] || continue
        werte=$(printf 'cd %s\ncd %s\nlsval\n' "$schluessel" "$name" | hivexsh -u "$hive" 2>/dev/null)
        treiber=$(printf '%s\n' "$werte" | sed -n 's/^"Printer Driver"="\(.*\)"$/\1/p')
        port=$(printf '%s\n' "$werte" | sed -n 's/^"Port"="\(.*\)"$/\1/p')
        if virtuell "$name" "$port"; then virtuelle=$((virtuelle+1)); continue; fi
        anzahl=$((anzahl+1))
        bewerte_drucker "$name" "${treiber:-$name}" "$port"
    done < "$tmp/druckernamen"
    if [ "$gesamt" = 0 ]; then
        # Rückfall: Neuere Windows-Versionen führen die Warteschlangen nicht mehr (nur) unter Control\Print\Printers. Jedes Benutzerprofil
        # hat eine Liste "Devices" (Name = winspool,Anschluss), die alle eingerichteten Drucker des Benutzers enthält.
        printf 'cd \\%s\\Control\\Print\nls\n' "$satz" | hivexsh -u "$hive" 2>/dev/null | sed 's/\r$//' | tr '\n' ' ' > "$tmp/printunter"
        info "Untergeordnete Schlüssel von Control\\Print: $(cat "$tmp/printunter")"
        ud=$(pfad_ci "$w" users) || ud=""
        if [ -n "$ud" ]; then
            for prof in "$ud"/*/; do
                nt=$(pfad_ci "$prof" ntuser.dat) || continue
                printf 'cd \\Software\\Microsoft\\Windows NT\\CurrentVersion\\Devices\nlsval\n' | hivexsh -u "$nt" 2>/dev/null | sed 's/\r$//' \
                    | sed -n 's/^"\(.*\)"="winspool,\(.*\)"$/\1|\2/p' >> "$tmp/devices"
            done
            sort -u "$tmp/devices" 2>/dev/null > "$tmp/devices2"
            while IFS='|' read -r name port; do
                [ -n "$name" ] || continue
                gesamt=$((gesamt+1))
                if virtuell "$name" "$port"; then virtuelle=$((virtuelle+1)); continue; fi
                anzahl=$((anzahl+1))
                bewerte_drucker "$name" "$name" "$port"
            done < "$tmp/devices2"
            [ "$gesamt" -gt 0 ] && info "Die Drucker stammen aus den Benutzerprofilen (Liste „Devices“ der Benutzer)."
        fi
    fi
    info "In der Windows-Registrierung ($satz) gelesen: $gesamt Druckerwarteschlange(n), davon $virtuelle virtuelle (PDF, XPS, Fax, OneNote …)."

    # Scanner: Bildverarbeitungsgeräte (Geräteklasse {6BDD1FC6-…}); jeder Eintrag 0000, 0001 … hat eine Beschreibung
    klasse="\\$satz\\Control\\Class\\{6BDD1FC6-810F-11D0-BEC7-08002BE2092F}"
    printf 'cd %s\nls\n' "$klasse" | hivexsh -u "$hive" 2>/dev/null | sed 's/\r$//' | grep -E '^[0-9]{4}$' > "$tmp/scannernummern"
    : > "$tmp/scannernamen"
    while IFS= read -r nr; do
        [ -n "$nr" ] || continue
        printf 'cd %s\ncd %s\nlsval\n' "$klasse" "$nr" | hivexsh -u "$hive" 2>/dev/null | sed -n 's/^"DriverDesc"="\(.*\)"$/\1/p' >> "$tmp/scannernamen"
    done < "$tmp/scannernummern"
    sort -u "$tmp/scannernamen" | grep -v '^$' > "$tmp/scanner"
    if [ -s "$tmp/scanner" ]; then
        echo
        info "In Windows eingerichtete Scanner und Bildgeräte (Registrierung):"
        while IFS= read -r sname; do
            bewerte_scanner "$sname"
        done < "$tmp/scanner"
    fi
    [ "$anzahl" -gt 0 ] || info "Auf $dev sind keine echten Drucker eingerichtet (nur virtuelle wie „Microsoft Print to PDF“ oder gar keine)."
    return 0
}

if command -v hivexsh >/dev/null 2>&1; then
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
                    usb://*) warn "Gefunden: $name ($art): kein Treiber in der Datenbank. Mit dem Drucker testen und auf openprinting.org nachsehen." ;;
                    *) info "Gefunden: $name ($art): kein eigener Treiber in der Datenbank, Netzwerkdrucker arbeiten aber meist treiberlos (IPP). Mit einer Testseite prüfen." ;;
                esac
            fi
        done < "$tmp/geraete"
    fi
fi

# ---- Jetzt erreichbare Scanner (SANE: USB und Netzwerk über sane-airscan) --------------------------------------------------------
if command -v scanimage >/dev/null 2>&1; then
    scanimage -L 2>/dev/null | grep '^device' > "$tmp/scannerlive"
    echo
    if [ -s "$tmp/scannerlive" ]; then
        info "Scanner, die jetzt erreichbar sind (USB oder Netzwerk):"
        while IFS= read -r z; do
            beschr=$(printf '%s' "$z" | sed "s/^device \`[^']*' is a //")
            ok "Scanner gefunden: $beschr (SANE erkennt ihn, er lässt sich unter Linux nutzen)."
        done < "$tmp/scannerlive"
    else
        info "Zurzeit ist kein Scanner erreichbar. Für den Test den Scanner einschalten (Netzwerk/WLAN) oder per USB anschließen und das Modul erneut ausführen."
    fi
fi

echo
info "Hinweis: Die Druckerprüfung vergleicht nur Namen mit der Treiberdatenbank. Ob ein Drucker oder Scanner wirklich funktioniert, zeigt erst ein Test mit dem Gerät: einschalten, im Live-System einrichten und eine Testseite drucken beziehungsweise einen Scan machen."
exit 0
