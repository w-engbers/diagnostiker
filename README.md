# Diagnostiker

**English** · [Deutsch](README.de.md) · [Nederlands](README.nl.md)

A bootable USB diagnostic stick for PCs and notebooks. It grew out of the needs of repair cafés and volunteer IT help for seniors,
but it is meant for anyone who has to find out why a computer misbehaves. Plug it into a computer that will not start, or into one that is running Windows or Linux, and get a clear, plain-language
report on the disk, the boot process and the network.

> **Status: early test version.** The stick works and boots (BIOS and UEFI, Secure Boot stays on), but the creation tool is
> a command-line program and the stick's interface is **in German only** for now. A window-based creation tool and more
> languages are planned.

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

The stick is created **on Linux only** (it needs raw access to the USB drive). Running the program on Windows works from the finished stick.

You need: a USB stick of **4 to 512 GiB** (everything on it will be erased), about 1.5 GiB of free disk space, an internet connection,
and these standard Linux tools: `sfdisk` (util-linux), `exfatprogs`, `udisks2` and `pkexec`. The .NET SDK 10 is needed for now to run the creation tool.

```
git clone https://github.com/w-engbers/diagnostiker.git
cd diagnostiker
dotnet run --project src/Diagnostiker.Stick -- paket-erstellen
```

The program

1. downloads the latest release package and **verifies its signature** and checksums,
2. lists only USB drives that the system is not using and asks you to **type the device name** to confirm,
3. writes the image (it asks once for your password), reads it back and compares it,
4. copies the program, test modules and tools onto the data partition.

`paket-pruefen` only checks the latest package and changes nothing. To use a package from a local folder: `--quelle <folder>`.

## Releases and signatures

Each [release](https://github.com/w-engbers/diagnostiker/releases) contains `boot.img.gz`, `daten.tar.gz`, `manifest.json` and
`manifest.json.sig`. The manifest is signed (ECDSA P-256); the matching public key is built into the program, and the program
refuses packages whose signature or checksums do not match. Details: [docs/AUSLIEFERUNG.md](docs/AUSLIEFERUNG.md) (German).

## Source code and licenses

This repository contains the source code of everything that is distributed in the release packages. The project itself is licensed
under the **GNU General Public License, version 3** ([LICENSE](LICENSE)).

The live system is based on **Debian 13 "trixie"** and contains many packages under their own licenses (mostly GPL and similar).
Their source code is available from the Debian archive (`deb-src`, and [snapshot.debian.org](https://snapshot.debian.org/) for older versions).
The Windows build of `smartctl` is part of smartmontools (GPL-2.0); license text and source are on the stick under `werkzeuge/`.

## For developers

Architecture, build instructions, the test-module format and tests are described in German in [docs/](docs/)
(start with [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md) and [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)). Run the tests with `dotnet test`.
