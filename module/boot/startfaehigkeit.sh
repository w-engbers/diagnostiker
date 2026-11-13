#!/bin/sh
# Prüft NUR LESEND, ob die eingebauten Datenträger des Rechners startfähig aussehen (Linux-Live-System, Root nötig).
# Gesucht wird, was typischerweise den Start verhindert: fehlende oder leere EFI-Partition, fehlender Windows-Bootmanager,
# beschädigte Partitionstabelle, Windows ohne Systemdateien, Linux ohne Kernel, fstab mit fehlenden Laufwerken,
# Boot-Menü-Einträge der Firmware ins Leere, Start im falschen Modus (UEFI/BIOS) für das Plattenlayout.
# Ausgabe: Zeilen mit [OK], [INFO], [WARNUNG] oder [FEHLER]; die Regeln des Manifests werten diese Marken aus.
#
# Testhilfen: DIAGNOSTIKER_EINHAENGEN=<Ordner> verwendet <Ordner>/<Gerätename> statt zu mounten, DIAGNOSTIKER_MODUS=UEFI|BIOS.
LC_ALL=C
export LC_ALL

NF=0     # Zahl der Fehler und Warnungen je Datenträger
ok()   { echo "[OK] $*"; }
info() { echo "[INFO] $*"; }
warn() { echo "[WARNUNG] $*"; NF=$((NF+1)); }
fehl() { echo "[FEHLER] $*"; NF=$((NF+1)); }

# Wert eines Schlüssels aus einer lsblk-Zeile im Format KEY="value"
val() { printf ' %s\n' "$1" | sed -n "s/.* $2=\"\\([^\"]*\\)\".*/\\1/p" | head -1; }

tmp=$(mktemp -d)
GEHAENGT=0
aushaengen() { [ "$GEHAENGT" = 1 ] && umount "$tmp/m" 2>/dev/null; GEHAENGT=0; }
trap 'aushaengen; rm -rf "$tmp"' EXIT

# einhaengen <Gerät> <Dateisystem>: setzt W auf den Einhängepunkt, nur lesend
einhaengen() {
    W=
    if [ -n "$DIAGNOSTIKER_EINHAENGEN" ]; then
        W="$DIAGNOSTIKER_EINHAENGEN/$(basename "$1")"
        [ -d "$W" ] && return 0
        W=; return 1
    fi
    mp=$(findmnt -rno TARGET -S "$1" 2>/dev/null | head -1)
    if [ -n "$mp" ]; then W=$mp; return 0; fi
    mkdir -p "$tmp/m"
    case "$2" in
        ext2|ext3|ext4) o=ro,noload;            t=$2 ;;
        xfs)            o=ro,norecovery,nouuid; t=xfs ;;
        ntfs)           o=ro;                   t=ntfs-3g ;;
        *)              o=ro;                   t=$2 ;;
    esac
    if mount -t "$t" -o "$o" "$1" "$tmp/m" 2>/dev/null; then GEHAENGT=1; W="$tmp/m"; return 0; fi
    return 1
}

# finde <Ordner> <Name>: Eintrag ohne Beachtung der Groß-/Kleinschreibung (Windows schreibt "Windows", "System32" …)
finde() { find "$1" -maxdepth 1 -iname "$2" 2>/dev/null | head -1; }

if [ "$(id -u)" != 0 ] && [ -z "$DIAGNOSTIKER_EINHAENGEN" ]; then echo KEINE_RECHTE; exit 0; fi

modus=BIOS
[ -d /sys/firmware/efi ] && modus=UEFI
[ -n "$DIAGNOSTIKER_MODUS" ] && modus=$DIAGNOSTIKER_MODUS   # nur für Tests
info "Das Prüfsystem wurde im $modus-Modus gestartet."

# Achtung: lsblk liefert nichts, wenn seine Ausgabe direkt in eine Datei umgeleitet wird (so beobachtet am 13.11.2026), daher über eine Variable.
lsblk_aus=$(lsblk -Pnpo NAME,TYPE,PKNAME,FSTYPE,LABEL,PARTTYPE,PARTFLAGS,PARTUUID,SIZE,MODEL,TRAN,PTTYPE 2>/dev/null)
printf '%s\n' "$lsblk_aus" > "$tmp/alles"
[ -n "$lsblk_aus" ] || { echo "KEINE_DATENTRAEGER"; exit 0; }

# Der Stick selbst wird nicht geprüft.
sticks=$(grep -E 'LABEL="(DIAGBOOT|DIAGDATEN)"' "$tmp/alles" | while read -r z; do val "$z" PKNAME; done | sort -u)

eb=
if [ "$modus" = UEFI ] && command -v efibootmgr >/dev/null 2>&1; then eb=$(efibootmgr -v 2>/dev/null); fi
eintraege=$(printf '%s\n' "$eb" | grep -c '^Boot[0-9A-Fa-f]\{4\}')
alle_partuuid=$(grep 'TYPE="part"' "$tmp/alles" | while read -r z; do val "$z" PARTUUID; done | tr 'A-F' 'a-f')
esp_mit_windows=0
anzahl_platten=0

# ---- Prüfungen je Partition ----------------------------------------------------------------------------------------

pruefe_esp() { # Gerät, Dateisystem, PARTUUID
    p=$1; fs=$2; pu=$(printf '%s' "$3" | tr 'A-F' 'a-f')
    hat_esp=1
    if [ "$fs" != vfat ]; then fehl "EFI-Systempartition $p ist nicht FAT-formatiert (gefunden: ${fs:-ohne Dateisystem}) – die Firmware kann sie nicht lesen."; return; fi
    if command -v fsck.fat >/dev/null 2>&1 && ! fsck.fat -n "$p" >/dev/null 2>&1; then
        warn "Das Dateisystem der EFI-Systempartition $p meldet Fehler oder ist nicht sauber beendet (fsck.fat)."
    fi
    if ! einhaengen "$p" vfat; then fehl "EFI-Systempartition $p lässt sich nicht lesen."; return; fi
    if [ ! -d "$W/EFI" ]; then fehl "Die EFI-Systempartition $p ist leer (kein Ordner EFI) – es ist kein Bootloader installiert."; aushaengen; return; fi
    gefunden=0
    if [ -f "$W/EFI/Microsoft/Boot/bootmgfw.efi" ]; then
        gefunden=1; esp_hat_windows=1
        ok "Windows-Bootmanager vorhanden (EFI/Microsoft/Boot/bootmgfw.efi)."
        if [ -f "$W/EFI/Microsoft/Boot/BCD" ]; then
            [ -s "$W/EFI/Microsoft/Boot/BCD" ] || fehl "Die Boot-Konfiguration (BCD) auf $p ist leer."
        else
            fehl "Die Boot-Konfiguration von Windows (EFI/Microsoft/Boot/BCD) fehlt auf $p – Windows startet nicht (Reparatur: bcdboot)."
        fi
    fi
    for ordner in "$W"/EFI/*/; do
        n=$(basename "$ordner")
        case "$n" in Microsoft|BOOT|'*') continue ;; esac
        if ls "$ordner"*.efi "$ordner"*.EFI >/dev/null 2>&1; then gefunden=1; info "Bootloader-Ordner EFI/$n (z. B. Linux) vorhanden."; fi
    done
    if [ -f "$W/EFI/BOOT/BOOTX64.EFI" ] || [ -f "$W/EFI/Boot/bootx64.efi" ]; then
        gefunden=1; info "Fallback-Bootdatei EFI/BOOT/BOOTX64.EFI vorhanden (startet auch ohne Boot-Menü-Eintrag)."
        esp_hat_fallback=1
    fi
    [ "$gefunden" = 1 ] || fehl "Auf der EFI-Systempartition $p liegt kein Bootloader (weder Windows noch Linux noch Fallback)."
    [ "$esp_hat_windows" = 1 ] && esp_mit_windows=1
    # Passt jeder Boot-Menü-Eintrag dieser Partition zu einer vorhandenen Datei?
    if [ -n "$eb" ] && [ -n "$pu" ]; then
        printf '%s\n' "$eb" | grep -i "$pu" | while read -r e; do
            name=$(printf '%s' "$e" | sed 's/^Boot[0-9A-Fa-f]*\*\{0,1\} *//; s/[[:space:]]*HD(.*//')
            pfad=$(printf '%s' "$e" | sed -n 's/.*[Ff]ile(\([^)]*\)).*/\1/p' | tr '\\' '/')
            [ -n "$pfad" ] || pfad=$(printf '%s' "$e" | sed -n 's/.*)\/\(\\[^ )]*\).*/\1/p' | tr '\\' '/')   # ältere Schreibweise ohne File()
            [ -n "$pfad" ] || continue
            [ -f "$W$pfad" ] || echo "[FEHLER] Der Boot-Menü-Eintrag „$name“ verweist auf $pfad, diese Datei fehlt auf $p – der Eintrag kann nicht starten."
        done > "$tmp/eintrag"
        if [ -s "$tmp/eintrag" ]; then cat "$tmp/eintrag"; NF=$((NF+$(grep -c . "$tmp/eintrag"))); fi
    fi
    aushaengen
}

pruefe_ntfs() { # Gerät
    p=$1
    if command -v ntfsinfo >/dev/null 2>&1 && ntfsinfo -m "$p" 2>/dev/null | grep -qi 'dirty'; then
        warn "Das NTFS-Dateisystem auf $p ist als „nicht sauber beendet“ markiert (Absturz, Stromausfall oder Schnellstart) – Windows prüft es beim nächsten Start (chkdsk)."
    fi
    if ! einhaengen "$p" ntfs; then
        warn "Die NTFS-Partition $p lässt sich nicht einhängen (stark beschädigt, im Ruhezustand oder gesperrt)."
        return
    fi
    wdir=$(finde "$W" windows)
    if [ -z "$wdir" ]; then aushaengen; return; fi
    s32=$(finde "$wdir" system32)
    if [ -z "$s32" ]; then fehl "Auf $p gibt es einen Ordner Windows, aber ohne System32 – die Windows-Installation ist unvollständig."; aushaengen; return; fi
    hat_win=1
    info "Windows-Installation auf $p gefunden."
    kern=$(finde "$s32" ntoskrnl.exe)
    if [ -z "$kern" ]; then fehl "Auf $p fehlt der Windows-Kernel (System32/ntoskrnl.exe) – Windows kann nicht starten."
    elif [ ! -s "$kern" ]; then fehl "Der Windows-Kernel auf $p ist leer oder beschädigt."; fi
    if [ "$pttype" = gpt ]; then lader=winload.efi; else lader=winload.exe; fi
    wl=$(finde "$s32" "$lader")
    if [ -z "$wl" ]; then fehl "Auf $p fehlt der Windows-Ladevorgang (System32/$lader) – Windows startet nicht."
    elif [ ! -s "$wl" ]; then fehl "System32/$lader auf $p ist leer oder beschädigt."; fi
    cfg=$(finde "$s32" config)
    if [ -z "$cfg" ] || [ -z "$(finde "$cfg" SYSTEM)" ]; then fehl "Auf $p fehlt die Registrierungsdatei config/SYSTEM – Windows startet nicht (Wiederherstellung nötig)."
    elif [ ! -s "$(finde "$cfg" SYSTEM)" ]; then fehl "Die Registrierungsdatei config/SYSTEM auf $p ist leer."; fi
    [ -n "$(finde "$W" hiberfil.sys)" ] && info "Auf $p liegt hiberfil.sys (Ruhezustand oder Schnellstart): Dateien nicht von außen verändern, Windows könnte sonst beschädigt werden."
    [ -n "$(finde "$W" bootmgr)" ] && hat_bootmgr=1
    aushaengen
}

pruefe_linux() { # Gerät, Dateisystem
    p=$1; fs=$2
    if ! einhaengen "$p" "$fs"; then warn "Die Partition $p ($fs) lässt sich nicht einhängen."; return; fi
    root=0; [ -d "$W/etc" ] && { [ -r "$W/etc/os-release" ] || [ -r "$W/usr/lib/os-release" ]; } && root=1
    bootdir=$W; [ -d "$W/boot" ] && bootdir="$W/boot"
    kernel=$(ls "$bootdir"/vmlinuz* "$bootdir"/vmlinux* "$bootdir"/Image* 2>/dev/null | head -1)
    if [ "$root" = 0 ]; then
        if [ -n "$kernel" ]; then hat_linuxboot=1; info "Boot-Partition mit Linux-Kernel auf $p gefunden."; fi
        aushaengen; return
    fi
    hat_linux=1
    name=$(cat "$W/etc/os-release" "$W/usr/lib/os-release" 2>/dev/null | sed -n 's/^PRETTY_NAME=//p' | head -1 | tr -d '"')
    info "Linux-Installation auf $p gefunden (${name:-unbekannt})."
    sep_boot=0
    if [ -r "$W/etc/fstab" ]; then
        grep -v '^[[:space:]]*#' "$W/etc/fstab" | awk '$2=="/boot"{f=1} END{exit !f}' && sep_boot=1
    fi
    if [ -z "$kernel" ]; then
        if [ "$sep_boot" = 1 ]; then info "Der Kernel liegt auf einer eigenen /boot-Partition."
        else fehl "Auf $p (Linux) ist kein Kernel vorhanden (/boot/vmlinuz*) – das System kann nicht starten."; fi
    else
        [ -n "$(ls "$bootdir"/initrd* "$bootdir"/initramfs* 2>/dev/null)" ] || warn "Auf $p gibt es einen Kernel, aber kein initrd/initramfs – die meisten Systeme starten so nicht."
    fi
    # fstab: verweist sie auf Laufwerke, die es nicht mehr gibt?
    if [ -r "$W/etc/fstab" ]; then
        grep -v '^[[:space:]]*#' "$W/etc/fstab" | awk 'NF>=4 {print $1 "\t" $2 "\t" $4}' > "$tmp/fstab"
        while IFS="$(printf '\t')" read -r spec mp opts; do
            case "$opts" in *nofail*|*noauto*) continue ;; esac
            case "$spec" in
                UUID=*)      v=${spec#UUID=};      b=$(blkid -U "$v" 2>/dev/null) ;;
                LABEL=*)     v=${spec#LABEL=};     b=$(blkid -L "$v" 2>/dev/null) ;;
                PARTUUID=*)  v=${spec#PARTUUID=};  b=$(blkid -t "PARTUUID=$v" -o device 2>/dev/null) ;;
                *) continue ;;
            esac
            [ -n "$b" ] && continue
            case "$mp" in
                /|/boot|/boot/efi|/usr|/var) fehl "fstab auf $p: $spec für $mp existiert nicht – der Start bleibt hängen oder landet in der Notfall-Shell (Platte getauscht oder Partition neu angelegt?)." ;;
                *) warn "fstab auf $p: $spec für $mp existiert nicht – der Start verzögert sich oder bleibt hängen (mit „nofail“ ginge er weiter)." ;;
            esac
        done < "$tmp/fstab"
    fi
    if [ "$fs" = ext2 ] || [ "$fs" = ext3 ] || [ "$fs" = ext4 ]; then
        if command -v dumpe2fs >/dev/null 2>&1; then
            zustand=$(dumpe2fs -h "$p" 2>/dev/null | sed -n 's/^Filesystem state: *//p')
            [ -n "$zustand" ] && [ "$zustand" != clean ] && warn "Das Dateisystem auf $p ist nicht sauber (Zustand: $zustand) – beim Start läuft eine Prüfung oder es wird nur lesend eingehängt."
        fi
    fi
    if [ -z "$(ls "$bootdir"/grub/grub.cfg "$bootdir"/grub2/grub.cfg "$bootdir"/loader/loader.conf "$bootdir"/extlinux/extlinux.conf 2>/dev/null)" ] && [ "$sep_boot" = 0 ]; then
        warn "Auf $p fehlt eine Bootloader-Konfiguration (grub.cfg, loader.conf, extlinux.conf)."
    fi
    aushaengen
}

# ---- Je Datenträger ------------------------------------------------------------------------------------------------

grep 'TYPE="disk"' "$tmp/alles" > "$tmp/platten"
while read -r pz; do
    d=$(val "$pz" NAME)
    skip=0; for s in $sticks; do [ "$s" = "$d" ] && skip=1; done
    [ "$skip" = 1 ] && continue
    anzahl_platten=$((anzahl_platten+1))
    NF=0
    pttype=$(val "$pz" PTTYPE)
    tran=$(val "$pz" TRAN)
    echo
    info "Datenträger $d: $(val "$pz" MODEL), $(val "$pz" SIZE), Anschluss ${tran:-unbekannt}, Partitionstabelle ${pttype:-keine}."

    hat_esp=0; hat_win=0; hat_linux=0; hat_linuxboot=0; hat_bootmgr=0; hat_biosgrub=0; aktiv=0; esp_hat_windows=0; esp_hat_fallback=0

    # Partitionstabelle selbst
    case "$pttype" in
        gpt) if command -v sgdisk >/dev/null 2>&1; then
                 v=$(sgdisk -v "$d" 2>&1)
                 if ! printf '%s' "$v" | grep -q 'No problems found'; then
                     warn "Die GPT-Partitionstabelle auf $d meldet Probleme: $(printf '%s' "$v" | grep -i 'problem\|error\|warning\|mismatch' | head -1 | cut -c1-150)"
                 fi
             fi ;;
        dos) if command -v sfdisk >/dev/null 2>&1; then
                 v=$(sfdisk -V "$d" 2>&1)
                 printf '%s' "$v" | grep -qi 'No errors found' || warn "Die MBR-Partitionstabelle auf $d meldet Probleme: $(printf '%s' "$v" | head -1 | cut -c1-150)"
             fi ;;
        "") info "Auf $d gibt es keine Partitionstabelle (leer oder das ganze Gerät ist ein Dateisystem)." ;;
    esac

    grep "PKNAME=\"$d\"" "$tmp/alles" | grep 'TYPE="part"' > "$tmp/teile"
    [ -s "$tmp/teile" ] || info "Auf $d gibt es keine Partitionen."
    while read -r z; do
        p=$(val "$z" NAME); fs=$(val "$z" FSTYPE); pt=$(val "$z" PARTTYPE | tr 'A-F' 'a-f'); fl=$(val "$z" PARTFLAGS); pu=$(val "$z" PARTUUID)
        case "$fl" in *0x80*|*0X80*) aktiv=1 ;; esac
        case "$pt" in
            c12a7328-f81f-11d2-ba4b-00a0c93ec93b|0xef) pruefe_esp "$p" "$fs" "$pu"; continue ;;
            21686148-6449-6e6f-744e-656564454649)      hat_biosgrub=1; continue ;;
        esac
        case "$fs" in
            BitLocker)   info "Partition $p ist mit BitLocker verschlüsselt: der Inhalt lässt sich von außen nicht prüfen. Nach Änderungen an Firmware oder Boot-Dateien fragt Windows den Wiederherstellungsschlüssel ab." ;;
            crypto_LUKS) info "Partition $p ist mit LUKS verschlüsselt: der Inhalt lässt sich ohne Kennwort nicht prüfen." ;;
            LVM2_member) info "Partition $p gehört zu LVM: die logischen Laufwerke werden hier nicht geprüft." ;;
            ntfs)        pruefe_ntfs "$p" ;;
            ext2|ext3|ext4|xfs|btrfs|f2fs) pruefe_linux "$p" "$fs" ;;
        esac
    done < "$tmp/teile"

    # Passt das Plattenlayout zum Startmodus?
    if [ "$pttype" = gpt ]; then
        if [ "$modus" = UEFI ]; then
            if [ "$hat_esp" = 0 ] && { [ "$hat_win" = 1 ] || [ "$hat_linux" = 1 ] || [ "$hat_linuxboot" = 1 ]; }; then
                fehl "Auf $d gibt es keine EFI-Systempartition, der Rechner startet im UEFI-Modus – ohne sie startet nichts von dieser Platte."
            fi
        else
            if [ "$hat_win" = 1 ]; then warn "Auf $d liegt Windows auf einer GPT-Platte, der Rechner wurde im BIOS-Modus gestartet: Windows startet von GPT nur im UEFI-Modus (in der Firmware auf UEFI umstellen)."; fi
            if [ "$hat_win" = 0 ] && { [ "$hat_linux" = 1 ] || [ "$hat_linuxboot" = 1 ]; } && [ "$hat_biosgrub" = 0 ]; then
                warn "Auf $d liegt Linux auf einer GPT-Platte ohne BIOS-Boot-Partition; im BIOS-Modus kann GRUB so nicht starten (nur im UEFI-Modus)."
            fi
        fi
    elif [ "$pttype" = dos ]; then
        if [ "$modus" = UEFI ] && { [ "$hat_win" = 1 ] || [ "$hat_linux" = 1 ]; } && [ "$hat_esp" = 0 ]; then
            warn "Auf $d liegt ein System auf einer MBR-Platte (ohne EFI-Partition), der Rechner wurde im UEFI-Modus gestartet: von dieser Platte startet der Rechner nur im Legacy-/CSM-Modus der Firmware."
        fi
        if [ "$hat_win" = 1 ] && [ "$hat_bootmgr" = 1 ] && [ "$aktiv" = 0 ]; then
            fehl "Auf $d ist keine Partition als „aktiv“ markiert, Windows startet im BIOS-Modus nur von einer aktiven Partition."
        fi
        if [ -r "$d" ] && [ "$modus" = BIOS ]; then
            code=$(dd if="$d" bs=446 count=1 2>/dev/null | tr -d '\000' | wc -c)
            [ "$code" -gt 0 ] || warn "Der MBR von $d enthält keinen Startcode – im BIOS-Modus kann von dieser Platte nichts starten."
        fi
    fi

    [ "$esp_hat_windows" = 1 ] && esp_mit_windows=1
    if [ "$NF" = 0 ]; then
        if [ "$hat_win" = 1 ] || [ "$hat_linux" = 1 ] || [ "$hat_linuxboot" = 1 ]; then ok "Auf $d sieht alles startfähig aus (soweit von außen prüfbar)."
        else info "Auf $d wurde weder Windows noch Linux gefunden (Datenplatte oder nicht lesbar)."; fi
    fi
done < "$tmp/platten"

# ---- UEFI-Boot-Menü der Firmware -----------------------------------------------------------------------------------

if [ "$modus" = UEFI ] && [ -n "$eb" ]; then
    echo
    NF=0
    printf '%s\n' "$eb" | grep '^Boot[0-9A-Fa-f]\{4\}' | while read -r e; do
        g=$(printf '%s' "$e" | sed -n 's/.*HD([0-9]*,GPT,\([0-9A-Fa-f-]*\),.*/\1/p' | tr 'A-F' 'a-f')
        [ -n "$g" ] || continue
        if ! printf '%s\n' "$alle_partuuid" | grep -qx "$g"; then
            name=$(printf '%s' "$e" | sed 's/^Boot[0-9A-Fa-f]*\*\{0,1\} *//; s/[[:space:]]*HD(.*//')
            echo "[WARNUNG] Der Boot-Menü-Eintrag „$name“ der Firmware zeigt auf eine Partition, die es nicht mehr gibt (Platte getauscht oder Partition neu angelegt?)."
        fi
    done > "$tmp/tote"
    [ -s "$tmp/tote" ] && cat "$tmp/tote"
    if [ "$eintraege" = 0 ] && [ "$esp_mit_windows" = 1 ]; then
        echo "[WARNUNG] Die Firmware hat keine Boot-Menü-Einträge (Speicher zurückgesetzt?), die Windows-Bootdateien sind aber vorhanden. Mit bcdboot (Windows-Reparaturumgebung) oder efibootmgr (Linux) lässt sich der Eintrag neu anlegen."
    fi
fi

[ "$anzahl_platten" -gt 0 ] || echo "KEINE_DATENTRAEGER"
exit 0
