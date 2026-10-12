# Schleimige Screenshots

Tray-App für Windows (.NET 10, WinForms). Globaler Hotkey (Standard **Strg + Umschalt + Q**) → Screenshot aller
Monitore → QR-Codes/Barcodes werden erkannt (ZXing.Net) → Text in die Zwischenablage und Links im Standardbrowser.

- Links (`http(s)://…`, `www.…`) werden direkt geöffnet; anderer Inhalt (z. B. EAN) per Websuche (abschaltbar, Suchadresse einstellbar).
  Es werden **nur http/https** geöffnet, maximal 5 Tabs pro Scan.
- Mehrere Codes gleichzeitig; Dark-Mode-Codes (invertiert); kleine Codes auf 4K/Mehrmonitor.
- Keine Adminrechte: Einstellungen in `%AppData%\SchleimigeScreenshots\settings.json`, Log in `%LocalAppData%\SchleimigeScreenshots\log.txt`, Autostart über HKCU.

## Bauen (Windows PowerShell, .NET 10 SDK)
    ./build.ps1          # Tests + Publish nach dist\

## Installieren (nutzerweit, ohne Admin)
    powershell -ExecutionPolicy Bypass -File dist\install.ps1 -Autostart
Deinstallieren: Windows-Einstellungen → Apps, oder `install.ps1 -Uninstall`.
