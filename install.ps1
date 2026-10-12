<#
  Nutzerweite Installation ohne Administratorrechte.
  Installiert nach %LOCALAPPDATA%\Programs\SchleimigeScreenshots, legt eine Startmenü-Verknüpfung an,
  registriert die Deinstallation unter HKCU und startet die App.
  Aufruf:  powershell -ExecutionPolicy Bypass -File install.ps1 [-Autostart] [-Uninstall]
#>
param([switch]$Autostart, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$name    = 'SchleimigeScreenshots'
$target  = Join-Path $env:LOCALAPPDATA "Programs\$name"
$exe     = Join-Path $target "$name.exe"
$link    = Join-Path ([Environment]::GetFolderPath('Programs')) 'Schleimige Screenshots.lnk'
$uninst  = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$name"
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($Uninstall) {
    Remove-Item $link -ErrorAction SilentlyContinue
    Remove-Item $uninst -Recurse -ErrorAction SilentlyContinue
    Remove-ItemProperty $runKey -Name $name -ErrorAction SilentlyContinue
    # Das laufende Skript liegt im Zielordner -> Ordner verzögert löschen.
    Start-Process cmd.exe -WindowStyle Hidden -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$target`""
    Write-Host 'Deinstalliert (Einstellungen in %APPDATA%\SchleimigeScreenshots bleiben erhalten).'
    return
}

$source = Join-Path $PSScriptRoot "$name.exe"
if (-not (Test-Path $source)) { throw "$name.exe nicht neben install.ps1 gefunden." }

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $source $exe -Force
Copy-Item $PSCommandPath (Join-Path $target 'install.ps1') -Force

$sh = New-Object -ComObject WScript.Shell
$s = $sh.CreateShortcut($link); $s.TargetPath = $exe; $s.WorkingDirectory = $target; $s.Save()

New-Item $uninst -Force | Out-Null
Set-ItemProperty $uninst DisplayName 'Schleimige Screenshots'
Set-ItemProperty $uninst DisplayIcon $exe
Set-ItemProperty $uninst InstallLocation $target
Set-ItemProperty $uninst Publisher 'Schleimige Screenshots'
Set-ItemProperty $uninst UninstallString "powershell.exe -ExecutionPolicy Bypass -File `"$target\install.ps1`" -Uninstall"
Set-ItemProperty $uninst NoModify 1 -Type DWord
Set-ItemProperty $uninst NoRepair 1 -Type DWord

if ($Autostart) { Set-ItemProperty $runKey -Name $name -Value "`"$exe`"" }

Start-Process $exe
Write-Host "Installiert nach $target. Standard-Hotkey: Strg + Umschalt + Q"
