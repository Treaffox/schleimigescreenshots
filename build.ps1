# Erzeugt dist\SchleimigeScreenshots.exe (self-contained, einzelne Datei), install.ps1 und - wenn Inno Setup 6
# installiert ist - den Setup-Installer dist\SchleimigeScreenshots-Setup-<Version>.exe
dotnet test tests -c Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet publish src/App -c Release -o dist
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Copy-Item install.ps1 dist -Force

[xml]$props = Get-Content Directory.Build.props
$version = $props.Project.PropertyGroup.Version

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
    & $iscc "/DAppVersion=$version" installer\SchleimigeScreenshots.iss
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Write-Host "Fertig: dist\SchleimigeScreenshots-Setup-$version.exe (+ SchleimigeScreenshots.exe, install.ps1)"
} else {
    Write-Warning 'Inno Setup 6 nicht gefunden - Installer übersprungen (Download: https://jrsoftware.org/isdl.php).'
    Write-Host 'Fertig: dist\ (SchleimigeScreenshots.exe + install.ps1)'
}
