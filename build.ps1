# Erzeugt dist\SchleimigeScreenshots.exe (self-contained, einzelne Datei) + install.ps1
dotnet test tests -c Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet publish src/App -c Release -o dist
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Copy-Item install.ps1 dist -Force
Write-Host 'Fertig: dist\ (SchleimigeScreenshots.exe + install.ps1)'
