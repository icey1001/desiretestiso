# Desire USB Creator — Plain Windows Edition

This edition does **not** require Docker Desktop, WSL, Hyper-V, VirtualBox, or a Linux VM on the end-user PC.

## Run it

Double-click `Run-DesireUSB.cmd` (or `Run-DesireUSB.vbs`). A Desire loading window appears immediately. The bootstrapper requests administrator access, checks the required Microsoft .NET Framework, and silently installs .NET Framework 4.8 from Microsoft only if the PC does not already have it. Then it prepares and launches `DesireUSB.exe`.

No Visual C++ redistributable is needed by this app. It intentionally does not install software that it does not use.

## Where the ISO comes from

A fully customized Fedora/Linux ISO cannot be composed natively by stock Windows without a Linux build environment. To keep the end-user experience dependency-free, Desire USB Creator uses a **prebuilt** DesireVFIO ISO. There are two supported zero-Docker release models:

1. **Bundled/offline:** put the prebuilt `.iso` in the project `payload/` folder. The app finds it automatically.
2. **Hosted:** set `download_url` and (recommended) `sha256` in `release.ini`. The app downloads the image into `%ProgramData%\DesireVFIO\cache` and verifies the hash before it can be flashed.

If neither is configured, the user can still press Browse and select an ISO manually.

## Portable release packaging

Once you have a prebuilt DesireVFIO ISO, create a self-contained Windows folder with:

```powershell
.\Make-PortableRelease.ps1 -Iso C:\path\DesireVFIO-Fedora-44-KDE-x86_64.iso
```

That copies the ISO into the release payload and writes its SHA-256 into the release config. The target PC then needs only Windows.

## USB safety

Only detected USB disks are offered. The active Windows system disk is excluded, the device identity is rechecked immediately before the destructive write, and the user must confirm twice. The selected USB is then written byte-for-byte and can optionally be verified against the source image.
