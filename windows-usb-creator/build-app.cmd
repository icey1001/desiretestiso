@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [Desire] Microsoft .NET Framework compiler was not found.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:x64 /out:"DesireUSB.exe" /win32manifest:"app.manifest" ^
 /reference:System.dll ^
 /reference:System.Core.dll ^
 /reference:System.Management.dll ^
 /reference:System.Xaml.dll ^
 /reference:WindowsBase.dll ^
 /reference:PresentationCore.dll ^
 /reference:PresentationFramework.dll ^
 "Program.cs"
if errorlevel 1 exit /b 1
exit /b 0
