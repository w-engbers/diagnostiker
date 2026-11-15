# Nachbildung eines Anwenderrechners für den Test von stick/paket-schreiben.sh: nur util-linux, fdisk (sfdisk), exfatprogs, gzip.
FROM debian:trixie-slim
RUN apt-get update && apt-get install -y --no-install-recommends fdisk util-linux exfatprogs coreutils diffutils gzip parted udev fdisk \
    && rm -rf /var/lib/apt/lists/*
