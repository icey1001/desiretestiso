# Passthrough safety notes

- Never automatically select a disk for deletion based only on device order (`sda`, `nvme0n1`, etc.).
- Never pass a PCI root port merely because it shares an IOMMU group with the GPU.
- For a multifunction GPU, evaluate every function on the GPU slot (GPU, HDMI audio, USB, UCSI, etc.).
- Do not globally blacklist a host GPU driver until a separate host GPU is confirmed working.
- Do not make a VFIO boot entry the default until a manual test boot succeeds.
- Keep a non-VFIO boot entry available for rollback.
- A BIOS update is an advisory action, not an automatic requirement. Recommend it when a vendor update is actually available or a known firmware/IOMMU fault is present.
