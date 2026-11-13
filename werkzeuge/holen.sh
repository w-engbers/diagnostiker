#!/bin/sh
# Holt die Fremdwerkzeuge laut werkzeuge.lock.json, prüft sie (GPG-Signatur und feste SHA-256-Summen) und legt sie fertig
# unter cache/werkzeuge/ ab (gleiche Ordnerstruktur wie auf dem Stick). Wird von stick/staging.sh aufgerufen.
# Braucht curl, gpg, python3 und Docker (zum Entpacken des Windows-Installers mit 7-Zip, nichts wird ausgeführt).
set -eu

HIER=$(cd "$(dirname "$0")" && pwd)
WURZEL=$(cd "$HIER/.." && pwd)
LOCK="$HIER/werkzeuge.lock.json"
ZIEL="$WURZEL/cache/werkzeuge"
LADEN="$WURZEL/cache/download"
BILD=diagnostiker-basis-debian

for b in curl gpg python3 docker sha256sum; do command -v "$b" >/dev/null || { echo "$b fehlt." >&2; exit 1; }; done
mkdir -p "$LADEN"
rm -rf "$ZIEL"; mkdir -p "$ZIEL/win-x64" "$ZIEL/lizenzen" "$ZIEL/quellen"
GNUPGHOME=$(mktemp -d); export GNUPGHOME; chmod 700 "$GNUPGHOME"
trap 'rm -rf "$GNUPGHOME"' EXIT
gpg --batch --quiet --import "$HIER"/schluessel/*.asc

summe() { sha256sum "$1" | cut -d' ' -f1; }
# Eintrag aus der Sperrdatei lesen: lock.sh <ausdruck in python>, Variable w = Werkzeug
lesen() { python3 -I -c "import json,sys; w=json.load(open(sys.argv[1]))['werkzeuge'][int(sys.argv[2])]; print($3)" "$LOCK" "$1"; }
anzahl=$(python3 -I -c "import json,sys; print(len(json.load(open(sys.argv[1]))['werkzeuge']))" "$LOCK")

holen() { # url signatur sha256 datei
    [ -f "$LADEN/$4" ] || curl -fsSL -o "$LADEN/$4" "$1"
    [ -f "$LADEN/$4.asc" ] || curl -fsSL -o "$LADEN/$4.asc" "$2"
    [ "$(summe "$LADEN/$4")" = "$3" ] || { echo "PRÜFSUMME FALSCH: $4" >&2; rm -f "$LADEN/$4"; exit 1; }
    gpg --batch --status-fd 1 --verify "$LADEN/$4.asc" "$LADEN/$4" 2>/dev/null | grep -q "^\[GNUPG:\] VALIDSIG $5" \
        || { echo "SIGNATUR UNGÜLTIG: $4" >&2; exit 1; }
    echo "   geprüft: $4"
}

i=0
while [ "$i" -lt "$anzahl" ]; do
    name=$(lesen $i "" "w['name']"); ver=$(lesen $i "" "w['version']"); schl=$(lesen $i "" "w['signaturschluessel']")
    echo "== $name $ver"
    iurl=$(lesen $i "" "w['installer']['url']"); isig=$(lesen $i "" "w['installer']['signatur']"); isum=$(lesen $i "" "w['installer']['sha256']")
    qurl=$(lesen $i "" "w['quelltext']['url']"); qsig=$(lesen $i "" "w['quelltext']['signatur']"); qsum=$(lesen $i "" "w['quelltext']['sha256']")
    inst=$(basename "$iurl"); quell=$(basename "$qurl")
    holen "$iurl" "$isig" "$isum" "$inst" "$schl"
    holen "$qurl" "$qsig" "$qsum" "$quell" "$schl"

    ENTP="$LADEN/entpackt-$name-$ver"
    rm -rf "$ENTP"; mkdir -p "$ENTP"
    docker build -q -t "$BILD" "$WURZEL/basis/debian" >/dev/null
    docker run --rm -v "$LADEN":/d "$BILD" sh -c "7z x -y -o/d/entpackt-$name-$ver /d/$inst >/dev/null && chown -R $(id -u):$(id -g) /d/entpackt-$name-$ver"

    n=$(python3 -I -c "import json,sys; print(len(json.load(open(sys.argv[1]))['werkzeuge'][int(sys.argv[2])]['dateien']))" "$LOCK" $i)
    j=0
    while [ "$j" -lt "$n" ]; do
        aus=$(lesen $i "" "w['dateien'][$j]['aus']"); nach=$(lesen $i "" "w['dateien'][$j]['nach']")
        soll=$(lesen $i "" "w['dateien'][$j].get('sha256','')")
        quelle="$ENTP/$aus"
        [ -f "$quelle" ] || { echo "Datei fehlt im Installer: $aus" >&2; exit 1; }
        [ -z "$soll" ] || [ "$(summe "$quelle")" = "$soll" ] || { echo "PRÜFSUMME FALSCH: $aus" >&2; exit 1; }
        mkdir -p "$ZIEL/$(dirname "${nach#werkzeuge/}")"
        cp "$quelle" "$ZIEL/${nach#werkzeuge/}"
        j=$((j+1))
    done
    cp "$LADEN/$quell" "$ZIEL/quellen/$quell"
    projekt=$(lesen $i "" "w['projekt']"); lizenz=$(lesen $i "" "w['lizenz']")
    cat >> "$ZIEL/lizenzen/LIES-MICH.txt" <<TXT
$name $ver ($lizenz)
  Projekt:    $projekt
  Quelltext:  quellen/$quell (SHA-256 $qsum)
  Lizenztext: smartmontools-COPYING.txt
  Das Programm wird unverändert so mitgeliefert, wie es der Installer des Projekts enthält.

TXT
    i=$((i+1))
done
echo "   fertig: $ZIEL"
