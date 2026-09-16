# DesireVFIO Fedora KDE ISO project

A Fedora 44 KDE live ISO build project for **safe, rollback-first QEMU/KVM GPU passthrough setup**.

The visual direction is based on the dark silver/purple/magenta Desire Market look. The project adds a custom pre-flight UI, Anaconda branding, hardware checks, virtualization packages, a VFIO doctor, and a rollback-safe VFIO test-boot generator.

## Scope

This project configures normal Linux virtualization and PCIe passthrough. It intentionally does **not** include game cheats, memory-reading tools, anti-cheat bypasses, patched QEMU intended to hide virtualization, VM-detection evasion, offsets, or Nika binaries. The Nika repository was used only as a reference for the benign host topology it recommends: Fedora KDE, iGPU for the Linux host, and dGPU for the Windows guest.

## What is included

- Fedora 44 KDE live environment
- Dark purple/magenta Desire-themed pre-flight UI
- Cursor glow/follow animation
- Debug toggle: concise stages by default, raw command output in debug mode
- UI text zoom controls
- Hardware auto-detection:
  - CPU model and VMX/SVM flag
  - UEFI mode
  - Intel DMAR / AMD IVRS presence
  - active IOMMU groups
  - GPUs, PCI IDs, and current drivers
  - BIOS board/version/date
  - Secure Boot state
  - disks, removable media, and NTFS/BitLocker signatures
- Safety checks before installation
- Fedora installer launcher for actual partitioning/account creation
- Anaconda Web UI branding using the Desire palette
- Preinstalled QEMU/KVM/libvirt/virt-manager/OVMF/swtpm stack
- `desire-vfio-doctor` diagnostic command
- `desire-vfio-helper make-test-entry <PCI>`:
  - refuses a guest GPU that is firmware `boot_vga=1`
  - checks the IOMMU group
  - refuses unrelated non-bridge endpoints in the group
  - includes every function on the selected GPU slot
  - adds VFIO modules to initramfs without binding them in the safe boot
  - creates a **separate** BLS boot entry named `Desire VFIO Test (rollback-safe)`
  - does not change the default boot entry

## Why Fedora KDE

The current Nika Read Only documentation explicitly recommends Fedora 44 KDE (or XFCE) for a dual-GPU setup and describes the iGPU-for-Linux / dGPU-for-Windows topology. This project follows only that normal virtualization layout.

## Build on Windows

Windows is now supported through the included wrapper:

```powershell
.\build-windows.ps1
```

Or double-click `build-windows.cmd`. The recommended backend is Docker Desktop using Linux containers/WSL 2. The script creates a Fedora builder container, verifies privileged mount/loop-device support before starting, performs the Linux-only ISO construction inside that container, and copies the finished ISO plus SHA-256 hash to `out-windows/`.

See [`WINDOWS_BUILD.md`](WINDOWS_BUILD.md) for prerequisites, options, and the Fedora-VM fallback if a particular Docker Desktop backend does not expose loop devices.

## Build requirements

Build on a Fedora 44 machine or Fedora 44 VM with:

- root/sudo access
- at least ~20 GB free space
- working network access to Fedora repositories
- preferably 8+ GB RAM

The build script installs the required Fedora tooling (`lorax`, `lorax-lmc-novirt`, `spin-kickstarts`, `pykickstart`, and `livecd-tools`).

## Build

```bash
cd desire-vfio-iso
chmod +x build.sh
./build.sh
```

The ISO is written under `out/`.

You can override the Fedora release or ISO name:

```bash
FEDORA_RELEASE=44 ISO_NAME=DesireVFIO.iso ./build.sh
```

## Boot / install flow

1. Boot the USB in UEFI mode.
2. The Desire VFIO pre-flight UI auto-opens.
3. Review virtualization, firmware, GPU, IOMMU, BIOS, and disk checks.
4. Select the disk you intend to use. This selection is advisory; the Desire UI does **not** wipe it.
5. Click **Open installer** to launch Fedora Anaconda.
6. Use Anaconda for the actual partitioning and user creation.
7. After first boot, launch **Desire VFIO Setup** from the application menu.
8. Select/verify the intended guest GPU and generate a rollback-safe VFIO test entry.
9. Reboot and manually choose **Desire VFIO Test** once.
10. If the test fails, reboot to the normal Fedora entry. The safe entry is unchanged.

## Useful commands

```bash
# Full diagnostics
sudo desire-vfio-doctor

# Generate a test entry manually (example only)
sudo /usr/local/libexec/desire-vfio-helper make-test-entry 0000:01:00.0

# Enable the virtualization daemon/socket model shipped by the Fedora release
sudo /usr/local/libexec/desire-vfio-helper enable-virtualization
```

## Safety design

The project deliberately avoids making VFIO bindings globally persistent before a successful test boot. This is important because a wrong early VFIO bind can leave the host without a display or can hang during device initialization. A separate test entry provides a simple rollback path.

Before installing or enabling passthrough, verify:

- iGPU or second host GPU is enabled and connected to a monitor
- firmware primary display is set to the host GPU
- VT-d / AMD IOMMU is enabled
- VMX / SVM is enabled
- UEFI mode is used; CSM should normally be disabled
- the guest GPU's IOMMU group contains only its functions plus acceptable PCI bridges

## Project layout

```text
assets/                         Logo asset
config/packages.txt             Packages added to Fedora KDE
build.sh                        Fedora ISO build entrypoint
tools/make_kickstart.py         Flattens/extends Fedora KDE kickstart
overlay/usr/local/bin/          Pre-flight UI and diagnostics
overlay/usr/local/libexec/      Privileged VFIO test-entry helper
overlay/usr/local/share/        Web UI assets
overlay/usr/share/cockpit/      Anaconda/Cockpit branding override
overlay/etc/xdg/autostart/      Live/first-boot launcher
```

## Current build status

The source tree is generated and syntax-checked in this bundle. A final Fedora ISO still uses Fedora/Linux image-construction tooling, but Windows users can now run it through `build-windows.ps1`, which creates a privileged Fedora builder container. If Docker Desktop cannot expose the required mount/loop-device functionality, use the documented Fedora VM fallback.

## Desire USB Creator (Windows)

The project now includes `windows-usb-creator/`, a Windows-native WPF app that can build/select the DesireVFIO ISO and raw-flash it to a USB chosen from a safe USB-only dropdown.

Start it on Windows with:

```text
windows-usb-creator\Run-DesireUSB.cmd
```

The first run builds `DesireUSB.exe` locally, then Windows prompts for administrator access. The app excludes the active Windows system disk, re-validates the selected USB immediately before the destructive operation, shows its model/size/disk number, requires two confirmations, writes the ISO byte-for-byte, and can verify the result with SHA-256.

See `windows-usb-creator/README.md` for details.


## Plain Windows USB Creator

The `windows-usb-creator` folder now runs on normal Windows without Docker/WSL. `Run-DesireUSB.cmd` shows a loading screen, bootstraps .NET Framework 4.8 only when required, and launches the USB creator. The Windows machine does not compose Fedora itself; use a bundled prebuilt ISO under `payload/` or configure a hosted prebuilt ISO in `windows-usb-creator/release.ini`.

## Get the ISO without Docker/WSL on Windows

This source bundle does not include a prebuilt ISO. For a Windows PC with no Linux tooling installed, use the included GitHub Actions workflow under `.github/workflows/build-iso.yml`. See [`GET_THE_ISO.md`](GET_THE_ISO.md). The workflow builds the ISO on a cloud Fedora environment and returns it as a downloadable artifact.
