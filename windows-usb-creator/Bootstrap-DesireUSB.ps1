param()
$ErrorActionPreference = 'Stop'
$Base = Split-Path -Parent $MyInvocation.MyCommand.Path
$LogDir = Join-Path $env:ProgramData 'DesireVFIO'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$LogFile = Join-Path $LogDir 'bootstrap.log'
function Log([string]$m) { "$(Get-Date -Format s) $m" | Add-Content -Path $LogFile -Encoding UTF8 }

function Is-Admin {
  $id = [Security.Principal.WindowsIdentity]::GetCurrent()
  $p = New-Object Security.Principal.WindowsPrincipal($id)
  return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (-not (Is-Admin)) {
  Log 'Requesting administrator access.'
  $arg = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $MyInvocation.MyCommand.Path + '"'
  Start-Process powershell.exe -Verb RunAs -ArgumentList $arg | Out-Null
  exit
}

Add-Type -AssemblyName PresentationFramework
$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="520" Height="235" WindowStartupLocation="CenterScreen" WindowStyle="None" ResizeMode="NoResize" Background="#080812" AllowsTransparency="False" Topmost="True">
 <Border BorderBrush="#7036B8" BorderThickness="1" CornerRadius="18" Background="#0D0D18" Padding="28">
  <Grid>
   <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
   <TextBlock Text="DESIRE" Foreground="#FF3BCF" FontFamily="Segoe UI" FontWeight="Bold" FontSize="12"/>
   <TextBlock Grid.Row="1" Text="Preparing USB Creator" Foreground="#F6F4FF" FontFamily="Segoe UI" FontWeight="SemiBold" FontSize="27" Margin="0,5,0,7"/>
   <TextBlock Grid.Row="2" Name="Status" Text="Checking Windows requirements..." Foreground="#B7B2C9" FontFamily="Segoe UI" FontSize="12" Margin="0,0,0,19"/>
   <ProgressBar Grid.Row="3" IsIndeterminate="True" Height="8" Foreground="#B54CFF" Background="#181628"/>
  </Grid>
 </Border>
</Window>
'@
$reader = New-Object System.Xml.XmlNodeReader ([xml]$xaml)
$Splash = [Windows.Markup.XamlReader]::Load($reader)
$Status = $Splash.FindName('Status')
$Splash.Show()
$Splash.Dispatcher.Invoke([action]{}, 'Background')
function Set-Status([string]$s) {
  Log $s
  $Status.Text = $s
  $Splash.Dispatcher.Invoke([action]{}, 'Background')
}

try {
  Set-Status 'Checking Microsoft .NET Framework...'
  $release = 0
  try { $release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction Stop).Release } catch {}
  if ($release -lt 528040) {
    Set-Status 'Downloading Microsoft .NET Framework 4.8...'
    $installer = Join-Path $env:TEMP 'NDP48-x86-x64-AllOS-ENU.exe'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    (New-Object Net.WebClient).DownloadFile('https://go.microsoft.com/fwlink/?linkid=2088631', $installer)
    Set-Status 'Installing Microsoft .NET Framework 4.8 silently...'
    $proc = Start-Process $installer -ArgumentList '/q /norestart' -Wait -PassThru
    Log "NET48 installer exit code: $($proc.ExitCode)"
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
    if ($proc.ExitCode -eq 3010) {
      $Splash.Close()
      [System.Windows.MessageBox]::Show('Windows needs one restart to finish installing .NET Framework 4.8. Restart, then open Desire USB Creator again.','Desire USB Creator','OK','Information') | Out-Null
      exit
    }
    if ($proc.ExitCode -ne 0) { throw ".NET Framework installer exited with code $($proc.ExitCode)." }
  }

  Set-Status 'Checking Desire USB Creator...'
  $exe = Join-Path $Base 'DesireUSB.exe'
  if (-not (Test-Path $exe)) {
    Set-Status 'Preparing the Desire app for this PC...'
    $build = Join-Path $Base 'build-app.cmd'
    $proc = Start-Process 'cmd.exe' -ArgumentList "/d /s /c `"`"$build`"`"" -WorkingDirectory $Base -WindowStyle Hidden -Wait -PassThru
    Log "App compiler exit code: $($proc.ExitCode)"
    if ($proc.ExitCode -ne 0 -or -not (Test-Path $exe)) { throw 'DesireUSB.exe could not be prepared. See bootstrap.log for details.' }
  }

  Set-Status 'Scanning local image and release configuration...'
  Start-Sleep -Milliseconds 350
  $Splash.Close()
  Start-Process $exe -WorkingDirectory $Base | Out-Null
}
catch {
  Log ("ERROR: " + $_.Exception.ToString())
  try { $Splash.Close() } catch {}
  [System.Windows.MessageBox]::Show($_.Exception.Message + "`n`nLog: " + $LogFile,'Desire USB Creator','OK','Error') | Out-Null
  exit 1
}
