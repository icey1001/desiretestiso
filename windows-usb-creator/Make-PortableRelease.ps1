param(
  [Parameter(Mandatory=$true)][string]$Iso,
  [string]$Output = "DesireVFIO-Windows-Portable"
)
$ErrorActionPreference='Stop'
$Base = Resolve-Path (Join-Path $PSScriptRoot '..')
$IsoPath = Resolve-Path $Iso
$Out = Join-Path (Split-Path $Base -Parent) $Output
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Copy-Item (Join-Path $Base 'windows-usb-creator') (Join-Path $Out 'windows-usb-creator') -Recurse
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'payload') | Out-Null
$target = Join-Path $Out ('payload\' + [IO.Path]::GetFileName($IsoPath))
Copy-Item $IsoPath $target
$sha=(Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
@("image_name=$([IO.Path]::GetFileName($target))","download_url=","sha256=$sha") | Set-Content (Join-Path $Out 'windows-usb-creator\release.ini') -Encoding ASCII
Write-Host "Portable plain-Windows release created at: $Out"
Write-Host "ISO SHA-256: $sha"
