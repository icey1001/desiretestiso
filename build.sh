#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
FEDORA_RELEASE=${FEDORA_RELEASE:-44}
OUT=${OUT:-$ROOT/out}
BUILD=${BUILD:-$ROOT/build}
ISO_NAME=${ISO_NAME:-DesireVFIO-Fedora-${FEDORA_RELEASE}-KDE-x86_64.iso}

if [[ $(id -u) -eq 0 ]]; then SUDO=; else SUDO=sudo; fi
command -v dnf >/dev/null 2>&1 || { echo "Build this ISO on Fedora ${FEDORA_RELEASE} (or a Fedora VM)." >&2; exit 1; }

$SUDO dnf install -y lorax lorax-lmc-novirt spin-kickstarts pykickstart livecd-tools
mkdir -p "$BUILD" "$OUT"
BASE=""
for p in /usr/share/spin-kickstarts/fedora-live-kde.ks /usr/share/spin-kickstarts/fedora-live-kde-base.ks; do
  [[ -f "$p" ]] && { BASE=$p; break; }
done
[[ -n "$BASE" ]] || { echo "Could not find Fedora KDE spin kickstart." >&2; exit 1; }

ksflatten -c "$BASE" -o "$BUILD/fedora-kde-flat.ks"
python3 "$ROOT/tools/make_kickstart.py" \
  --base "$BUILD/fedora-kde-flat.ks" \
  --overlay "$ROOT/overlay" \
  --packages "$ROOT/config/packages.txt" \
  --out "$BUILD/desire-vfio.ks"

rm -rf "$OUT"/*
$SUDO livemedia-creator \
  --make-iso \
  --ks "$BUILD/desire-vfio.ks" \
  --project DesireVFIO \
  --releasever "$FEDORA_RELEASE" \
  --iso-name "$ISO_NAME" \
  --iso-only \
  --no-virt \
  --resultdir "$OUT" \
  --tmp /var/tmp/desire-vfio-lmc

echo
echo "Built ISO should be under: $OUT"
find "$OUT" -maxdepth 3 -type f -name '*.iso' -print
