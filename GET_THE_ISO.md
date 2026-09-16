# Get the DesireVFIO ISO without installing Linux locally

The project source does not contain a prebuilt Fedora ISO. The easiest no-Docker/no-WSL Windows path is the included GitHub Actions builder.

1. Create a new GitHub repository.
2. Upload the contents of this folder, including the hidden `.github` folder.
3. Open the repository's **Actions** tab.
4. Select **Build DesireVFIO ISO**.
5. Click **Run workflow**.
6. Wait for the build to finish.
7. Open the completed run and download the **DesireVFIO-Fedora-44-KDE-x86_64** artifact.
8. Extract the artifact. It contains the `.iso` and a `.sha256` checksum.
9. Put the `.iso` in this project's `payload` folder, or select it in Desire USB Creator.

The cloud runner uses a privileged Fedora 44 container because Fedora's ISO composition tools are Linux-only. Nothing needs to be installed on the Windows PC for the ISO build itself.
