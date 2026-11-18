## Diagnostiker

A bootable USB diagnostic stick for PCs and notebooks. This release contains everything needed to create one.

**Quick start (Linux):** download `diagnostiker-stick-manager-linux-x64.tar.gz`, unpack it and start `diagnostiker-stick-gui`.
The window downloads the package files listed below by itself, verifies their signature and guides you through creating the stick.
**The USB stick you choose will be erased completely.**

| File | What it is |
|---|---|
| `diagnostiker-stick-manager-linux-x64.tar.gz` | The stick manager (window program for Linux, no .NET installation needed) |
| `boot.img.gz`, `daten.tar.gz` | The signed package: live system and program data. The stick manager downloads these itself |
| `manifest.json`, `manifest.json.sig` | List of the package files with checksums, and its signature (checked by the stick manager) |
| `debian-pakete.txt` | List of all Debian packages (with versions) in the live system, for finding their source code |
| `SHA256SUMS` | Checksums of all files above |

Requirements: Linux with a graphical desktop, a USB stick of 4 to 512 GiB, and the standard tools `sfdisk` (util-linux), `exfatprogs`, `udisks2` and `pkexec`.

The source code of this release is the repository at the tag of this release. Licensed under GPL-3.0.

*Early test version; the interface is in German only for now.*
