# Bauumgebung für das Live-System: Debian 13 "trixie" mit live-build.
# Der Bau läuft in diesem Container (privilegiert gestartet), unabhängig von der Distribution des Wirtsrechners.
FROM debian:trixie

ENV DEBIAN_FRONTEND=noninteractive LANG=C.UTF-8

RUN apt-get update \
 && apt-get install -y \
        live-build debootstrap squashfs-tools xorriso mtools dosfstools \
        syslinux syslinux-common isolinux grub-pc-bin grub-efi-amd64-bin \
        ca-certificates gnupg cpio zstd \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /bau
