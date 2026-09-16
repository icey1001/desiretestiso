# Plain Windows distribution

The end-user Windows workflow no longer requires Docker Desktop or WSL.

1. Build the DesireVFIO ISO once on a supported Fedora build machine/CI.
2. Either put that ISO in `payload/`, or host it and fill in `windows-usb-creator/release.ini` with the HTTPS URL and SHA-256.
3. On the Windows target, double-click `windows-usb-creator\Run-DesireUSB.cmd`.

The launcher immediately shows a Desire loading window. If .NET Framework 4.8 is missing, it downloads the official Microsoft installer and installs it silently, then prepares and opens the USB creator. No Docker, WSL, VM platform, or Visual C++ redistributable is required by the flasher itself.

A customized Fedora ISO still has to be prebuilt somewhere because Fedora's ISO composition toolchain is Linux-native. The plain-Windows app intentionally downloads/bundles that finished image rather than installing a hidden VM or Linux subsystem on the user's computer.
