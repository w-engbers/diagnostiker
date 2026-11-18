# Diagnostiker

**English** · [Deutsch](README.de.md) · [Nederlands](README.nl.md)

A bootable USB diagnostic stick for PCs and notebooks. It grew out of the needs of repair cafés and volunteer IT help for seniors,
but it is meant for anyone who has to find out why a computer misbehaves. Plug it into a computer that will not start, or into one that is running Windows or Linux, and get a clear, plain-language
report on the disk, the boot process and the network.

> **Status: early test version.** The stick works and boots (BIOS and UEFI, Secure Boot stays on) and has a window-based creation tool.
> The interface of both the stick and the creation tool is **in German only** for now; more languages are planned.

## What the stick checks

- **Boot:** can the machine start at all, firmware mode, Windows start problems
- **Disk:** SMART health of all drives, overview, encryption (BitLocker/LUKS)
- **Network:** adapters, wired and Wi-Fi connection, Bluetooth, internet reachability
- **System:** hardware inventory, recent system log entries
- **Switching over:** Windows 11 readiness, printers, hardware summary

The stick **does not change the machine it checks** unless you explicitly confirm a question. Every session writes a report
(`bericht.md`, plus raw data) to the stick.

How it starts:

- **Machine does not boot:** boot the stick from the machine's boot menu. A small Debian-based live system starts the program by itself.
- **Machine is running:** open the stick and start `Start-Windows.cmd` (Windows) or `start-linux.sh` (Linux).

## Creating a stick

The stick is created **on Linux only** (it needs raw access to the USB drive). Running the diagnostic program on Windows works from the finished stick.

1. Download `diagnostiker-stick-manager-linux-x64.tar.gz` from the [latest release](https://github.com/w-engbers/diagnostiker/releases/latest), unpack it and start `diagnostiker-stick-gui`.
2. The window guides you through four steps:
   1. It downloads the latest package and **verifies its signature** and checksums.
   2. You choose a USB stick. Only USB drives of 4 to 512 GiB that the system is not using are offered.
   3. You confirm by **typing the device name**. Everything on that stick will be erased.
   4. The image is written (you are asked for your password once), read back and compared, and the program and test modules are copied onto the data partition.

You need: a Linux desktop, a USB stick of **4 to 512 GiB**, about 1.5 GiB of free disk space, an internet connection, and the standard tools
`sfdisk` (util-linux), `exfatprogs`, `udisks2` and `pkexec`. Most desktop distributions have them; install missing ones with your package manager.
The stick manager is not signed itself: compare the file with `SHA256SUMS` from the same release if you want to be sure.
The packages it downloads are signed, and the program checks them before anything is written.

*Command line instead of the window* (needs the .NET SDK 10):

```
git clone https://github.com/w-engbers/diagnostiker.git
cd diagnostiker
dotnet run --project src/Diagnostiker.Stick -- paket-erstellen
```

`paket-pruefen` only checks the latest package and changes nothing. To use a package from a local folder (for example without internet): `--quelle <folder>`; the window program accepts the same option.

## Releases and signatures

Each [release](https://github.com/w-engbers/diagnostiker/releases) contains the stick manager, the package (`boot.img.gz`, `daten.tar.gz`, `manifest.json`,
`manifest.json.sig`), the list of Debian packages (`debian-pakete.txt`) and `SHA256SUMS`. The manifest is signed (ECDSA P-256); the matching public key is built
into the program, and the program refuses packages whose signature or checksums do not match. Details: [docs/AUSLIEFERUNG.md](docs/AUSLIEFERUNG.md) (German).

## Source code and licenses

This repository contains the source code of everything that is distributed in the release packages. The project itself is licensed
under the **GNU General Public License, version 3** ([LICENSE](LICENSE)).

The live system is based on **Debian 13 "trixie"** and contains many packages under their own licenses (mostly GPL and similar).
Each release lists them in `debian-pakete.txt` (also on the stick, under `werkzeuge/lizenzen/`); their source code is available from the Debian archive
(`apt-get source <package>=<version>`, and [snapshot.debian.org](https://snapshot.debian.org/) for older versions).
The Windows build of `smartctl` is part of smartmontools (GPL-2.0); license text and source are on the stick under `werkzeuge/`.

## For developers

Architecture, build instructions, the test-module format and tests are described in German in [docs/](docs/)
(start with [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md) and [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)). Run the tests with `dotnet test`.
