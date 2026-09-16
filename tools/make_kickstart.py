#!/usr/bin/env python3
import argparse, base64, io, os, tarfile
from pathlib import Path

ap=argparse.ArgumentParser()
ap.add_argument('--base',required=True)
ap.add_argument('--overlay',required=True)
ap.add_argument('--packages',required=True)
ap.add_argument('--out',required=True)
args=ap.parse_args()
base=Path(args.base).read_text()
packages=[]
for line in Path(args.packages).read_text().splitlines():
    line=line.strip()
    if line and not line.startswith('#'): packages.append(line)

# Add packages to the first %packages section.
lines=base.splitlines()
inside=False; inserted=False; out=[]
for line in lines:
    if line.strip().startswith('%packages') and not inserted:
        inside=True
        out.append(line)
        continue
    if inside and line.strip()=='%end' and not inserted:
        out.append('\n# Desire VFIO additions')
        out.extend(packages)
        out.append(line)
        inside=False; inserted=True
        continue
    out.append(line)
if not inserted:
    out += ['','%packages'] + packages + ['%end']

# Package the overlay so the kickstart can install it without an external repo.
buf=io.BytesIO()
with tarfile.open(fileobj=buf,mode='w:gz') as tf:
    root=Path(args.overlay)
    for p in sorted(root.rglob('*')):
        tf.add(p, arcname=str(p.relative_to(root)), recursive=False)
b64=base64.b64encode(buf.getvalue()).decode()
chunks='\n'.join(b64[i:i+76] for i in range(0,len(b64),76))

post=f'''\n\n%post --erroronfail\nset -eux\nmkdir -p /var/lib/desire-vfio\nbase64 -d > /tmp/desire-vfio-overlay.tar.gz <<'DESIRE_OVERLAY_EOF'\n{chunks}\nDESIRE_OVERLAY_EOF\ntar -xzf /tmp/desire-vfio-overlay.tar.gz -C /\nrm -f /tmp/desire-vfio-overlay.tar.gz\nchmod 0755 /usr/local/bin/desire-vfio-ui /usr/local/bin/desire-vfio-doctor /usr/local/libexec/desire-vfio-helper\n# Keep safe host defaults. VFIO is configured only through a separate test boot entry later.\nmkdir -p /etc/modprobe.d\ncat > /etc/modprobe.d/desire-vfio-safe.conf <<'SAFE'\n# Desire VFIO safe host profile. Do not bind a guest GPU here.\n# The first-boot assistant creates a separate rollback-safe BLS test entry.\nSAFE\n# Make sure virtualization services are available after install; sockets are enabled on demand by the helper.\nmkdir -p /var/lib/desire-vfio\necho 'Fedora KDE live image with Desire VFIO preflight installed' > /var/lib/desire-vfio/image-info\n# Desktop icon cache is optional during image build.\ncommand -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f /usr/share/icons/hicolor || true\n%end\n'''
Path(args.out).write_text('\n'.join(out)+post)
print(args.out)
