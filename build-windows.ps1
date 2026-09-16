Write-Host "[Desire] The plain-Windows edition uses a bundled or hosted prebuilt ISO; Docker is not required."
Start-Process (Join-Path $PSScriptRoot 'windows-usb-creator\Run-DesireUSB.cmd')
