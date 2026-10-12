# Schleimige Screenshots

Tray-App für Windows (.NET 10, WinForms). Globaler Hotkey (Standard **Strg + Umschalt + Q**) → Screenshot aller
Monitore → QR-Codes/Barcodes werden erkannt (ZXing.Net) → Text in die Zwischenablage und Links im Standardbrowser.

- Links (`http(s)://…`, `www.…`) werden direkt geöffnet; anderer Inhalt (z. B. EAN) per Websuche (abschaltbar, Suchadresse einstellbar).
  Es werden **nur http/https** geöffnet, maximal 5 Tabs pro Scan.
- Mehrere Codes gleichzeitig; Dark-Mode-Codes (invertiert); kleine Codes auf 4K/Mehrmonitor.
- Keine Adminrechte: Einstellungen in `%AppData%\SchleimigeScreenshots\settings.json`, Log in `%LocalAppData%\SchleimigeScreenshots\log.txt`, Autostart über HKCU.

## Installieren (nutzerweit, ohne Administratorrechte)
`SchleimigeScreenshots-Setup-<Version>.exe` aus den [Releases](https://github.com/Treaffox/schleimigescreenshots/releases) starten.
Der Assistent (Deutsch/Englisch je nach Windows-Sprache) bietet:

- frei wählbaren **Zielordner** (Standard: `%LocalAppData%\Programs\Schleimige Screenshots`)
- **Startmenü-Eintrag** (erscheint unter „Alle Apps“ und in der Suche; per Rechtsklick lässt er sich an „Start“ anheften –
  Windows erlaubt Installern nicht, das selbst zu tun)
- optional **Desktop-Verknüpfung** und **Start mit Windows**
- Eintrag unter *Einstellungen → Apps* samt Deinstallation (fragt, ob Einstellungen/Logs mitgelöscht werden sollen)
- Updates: das Setup erkennt eine laufende Instanz, beendet sie sauber und aktualisiert die vorhandene Installation

Unsigniert: Windows SmartScreen kann warnen („Weitere Informationen“ → „Trotzdem ausführen“).
Ohne Installation geht es auch: `SchleimigeScreenshots.exe` direkt starten (portabel, kein .NET nötig).
Silent-Installation: `Setup.exe /VERYSILENT /TASKS="desktopicon,autostart"` (Ordner mit `/DIR="…"`).

## Bauen
Windows (PowerShell, .NET 10 SDK; für den Installer zusätzlich [Inno Setup 6](https://jrsoftware.org/isdl.php)):

    ./build.ps1          # Tests, Publish nach dist\ und – falls Inno Setup gefunden wird – dist\SchleimigeScreenshots-Setup-<Version>.exe

Linux/macOS (Docker; Inno Setup läuft im Container unter Wine):

    dotnet publish src/App -c Release -o dist
    installer/build.sh   # baut beim ersten Mal das Image installer/docker, dann dist/SchleimigeScreenshots-Setup-<Version>.exe

Die Version steht in `Directory.Build.props`. Programmsymbol und Setup-Bilder erzeugt `python3 tools/make_icon.py`.

## Fehlersuche / Debug-Modus
Tray-Symbol → Rechtsklick → **Debug-Modus**. Danach erscheint nach jedem Scan ein Fenster mit einem kopierbaren Bericht
(Windows/.NET/DPI/Monitore, Aufnahme samt Pixelstatistik, Erkennung pro Bildregion, ausgeführte Aktionen, Fehler samt Stacktrace).

- **Alles kopieren** legt den Bericht in die Zwischenablage; **Aufnahme speichern …** sichert den Screenshot als PNG.
- **Selbsttest** prüft die Codeerkennung an einem künstlichen Testbild (trennt „Erkennung kaputt“ von „Aufnahme kaputt“).
- Unerwartete Fehler zeigen den Bericht auch ohne Debug-Modus. Zusätzlich landet alles in `%LocalAppData%\SchleimigeScreenshots\log.txt`.
