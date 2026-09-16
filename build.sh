#!/usr/bin/env bash
set -euo pipefail

ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
FEDORA_RELEASE=${FEDORA_RELEASE:-44}
OUT=${OUT:-$ROOT/out}
BUILD=${BUILD:-$ROOT/build}
ISO_NAME=${ISO_NAME:-DesireVFIO-Fedora-${FEDORA_RELEASE}-KDE-x86_64.iso}
KIWI_REPO=${KIWI_REPO:-https://forge.fedoraproject.org/releng/kiwi-descriptions.git}
KIWI_BRANCH=${KIWI_BRANCH:-f${FEDORA_RELEASE}}

if [[ $(id -u) -eq 0 ]]; then SUDO=; else SUDO=sudo; fi
command -v dnf >/dev/null 2>&1 || { echo "Build this ISO on Fedora ${FEDORA_RELEASE}." >&2; exit 1; }

echo "[DesireVFIO] Installing Fedora ${FEDORA_RELEASE} KIWI build dependencies..."
$SUDO dnf install -y git kiwi kiwi-systemdeps distribution-gpg-keys rsync

rm -rf "$BUILD"
mkdir -p "$BUILD" "$OUT"
KIWI="$BUILD/kiwi-descriptions"

echo "[DesireVFIO] Fetching Fedora ${FEDORA_RELEASE} KIWI descriptions..."
if ! git clone --depth 1 --branch "$KIWI_BRANCH" "$KIWI_REPO" "$KIWI"; then
  echo "[DesireVFIO] Branch $KIWI_BRANCH was not found; retrying default branch." >&2
  rm -rf "$KIWI"
  git clone --depth 1 "$KIWI_REPO" "$KIWI"
fi

# Add the DesireVFIO filesystem overlay to the live image.
mkdir -p "$KIWI/root"
rsync -a "$ROOT/overlay/" "$KIWI/root/"

# Add DesireVFIO packages only to the KDE live profile.
COMP="$KIWI/components/desire-vfio.xml"
mkdir -p "$(dirname "$COMP")"
{
  echo '<image>'
  echo '  <packages type="image" patternType="plusRecommended" profiles="KDE-Desktop-Live">'
  while IFS= read -r pkg; do
    pkg=${pkg%%#*}
    pkg=$(printf '%s' "$pkg" | xargs)
    [[ -z "$pkg" ]] && continue
    printf '    <package name="%s"/>\n' "$pkg"
  done < "$ROOT/config/packages.txt"
  echo '  </packages>'
  echo '</image>'
} > "$COMP"

# Include our component once.
if ! grep -q 'components/desire-vfio.xml' "$KIWI/Fedora.kiwi"; then
  sed -i '/<\/image>/i\    <include from="this://./components/desire-vfio.xml"/>' "$KIWI/Fedora.kiwi"
fi

# Give the image a Desire-specific internal name while keeping Fedora repositories/profile logic.
sed -i '0,/<image /s/name="[^"]*"/name="DesireVFIO"/' "$KIWI/Fedora.kiwi" || true

# Ensure our executables stay executable in the image.
chmod +x "$KIWI/root/usr/local/bin/desire-vfio-ui" \
         "$KIWI/root/usr/local/bin/desire-vfio-doctor" \
         "$KIWI/root/usr/local/libexec/desire-vfio-helper" 2>/dev/null || true

rm -rf "$OUT"/*

echo "[DesireVFIO] Building Fedora KDE Live ISO with KIWI..."
cd "$KIWI"
./kiwi-build \
  --kiwi-file=Fedora.kiwi \
  --image-type=iso \
  --image-profile=KDE-Desktop-Live \
  --image-release 0 \
  --output-dir "$OUT"

# Normalize the output filename for the Windows creator / GitHub artifact step.
FOUND=$(find "$OUT" -maxdepth 2 -type f -name '*.iso' | head -n 1 || true)
if [[ -z "$FOUND" ]]; then
  echo "[DesireVFIO] ERROR: KIWI completed but no ISO was found under $OUT" >&2
  find "$OUT" -maxdepth 3 -type f -print >&2 || true
  exit 1
fi
if [[ "$FOUND" != "$OUT/$ISO_NAME" ]]; then
  mv -f "$FOUND" "$OUT/$ISO_NAME"
fi

echo
printf '[DesireVFIO] Built: %s\n' "$OUT/$ISO_NAME"
ls -lh "$OUT/$ISO_NAME"
