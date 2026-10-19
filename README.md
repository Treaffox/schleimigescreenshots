<p align="center"><img src="assets/logo.png" alt="Schleimige Screenshots" width="128"></p>

# Schleimige Screenshots

Tray-App für Windows (.NET 10, WinForms). Globaler Hotkey (Standard **Strg + Umschalt + Q**) → Bildschirmbereich
aufziehen (wie bei einem Screenshot-Programm) → QR-Codes/Barcodes werden erkannt (ZXing.Net) → Text in die Zwischenablage
und Links im Standardbrowser.

- Links (`http(s)://…`, `www.…`) werden direkt geöffnet; anderer Inhalt (z. B. EAN) per Websuche (abschaltbar, Suchadresse einstellbar).
  Es werden **nur http/https** geöffnet, maximal 5 Tabs pro Scan.
- Findet die Suche keinen Code, liest die App den Text im Bild (RapidOCR/PP-OCRv5, lokal und offline, Modelle in der exe; Windows-OCR als Fallback) und öffnet darin enthaltene **Webadressen**
  (`http(s)://…`, `www.…`). Normaler Text wird ignoriert. Abschaltbar in den Einstellungen.
- Mehrere Codes gleichzeitig; Dark-Mode-Codes (invertiert); kleine und feine Codes; Mehrmonitor.
- Keine Adminrechte: Einstellungen in `%AppData%\SchleimigeScreenshots\settings.json`, Log in `%LocalAppData%\SchleimigeScreenshots\log.txt`, Autostart über HKCU.
- Automatische Updates über GitHub-Releases.

## Bereichsauswahl
Beim Scannen friert die App den Bildschirm ein und dunkelt ihn ab; mit der Maus wird der Bereich aufgezogen, der den Code enthält.

| Aktion | Wirkung |
| --- | --- |
| Ziehen mit der linken Maustaste | Bereich wählen und scannen |
| `Enter` | ganzen Monitor scannen |
| `Esc`, Rechtsklick oder Hotkey erneut | abbrechen |
| einfacher Klick | wartet weiter auf einen Bereich |

Kleine Bereiche werden gründlicher durchsucht (auch vergrößert), das findet selbst sehr feine Codes. Die Auswahl gilt je Monitor.
In den Einstellungen lässt sich die Auswahl abschalten (dann wird der ganze Bildschirm gescannt); im Tray-Menü gibt es zusätzlich
„Ganzen Bildschirm scannen“.

## Installieren (nutzerweit, ohne Administratorrechte)
`SchleimigeScreenshots-Setup-<Version>.exe` aus den [Releases](https://github.com/Treaffox/schleimigescreenshots/releases) starten.
Der Assistent (Deutsch/Englisch je nach Windows-Sprache) bietet:

- frei wählbaren **Zielordner** (Standard: `%LocalAppData%\Programs\Schleimige Screenshots`)
- **Startmenü-Eintrag** (erscheint unter „Alle Apps“ und in der Suche; per Rechtsklick lässt er sich an „Start“ anheften –
  Windows erlaubt Installern nicht, das selbst zu tun)
- optional **Desktop-Verknüpfung** und **Start mit Windows**
- Eintrag unter *Einstellungen → Apps* samt Deinstallation (fragt, ob Einstellungen/Logs mitgelöscht werden sollen)

Unsigniert: Windows SmartScreen kann warnen („Weitere Informationen“ → „Trotzdem ausführen“).
Ohne Installation geht es auch: `SchleimigeScreenshots.exe` direkt starten (portabel, kein .NET nötig).
Silent-Installation: `Setup.exe /VERYSILENT /TASKS="desktopicon,autostart"` (Ordner mit `/DIR="…"`).

## Updates
Die App prüft nach dem Start und danach etwa alle 6 Stunden (höchstens alle 12 Stunden) die GitHub-Releases. Findet sie eine neuere Version,
lädt sie die passende Datei, prüft deren SHA-256-Summe und installiert sie:

- **mit Setup installiert:** das Setup läuft still im Hintergrund, beendet die App, aktualisiert und startet die neue Version
  (Einstellungen, Verknüpfungen und Autostart bleiben erhalten). Scheitert die Installation, startet die vorhandene Version wieder.
- **portable EXE:** die Datei wird ausgetauscht und neu gestartet (Voraussetzung: der Ordner ist beschreibbar).

Einstellungen (Tray → Einstellungen): automatisch suchen, automatisch installieren, Vorabversionen (Pre-releases) berücksichtigen.
Wer nicht automatisch installieren will, bekommt einen Hinweis; im Tray-Menü gibt es „Nach Updates suchen …“ und „Update installieren …“.

**Hinweis zur Sicherheit:** Die Dateien sind nicht signiert; die Prüfsumme stammt von GitHub (schützt vor beschädigten Downloads, nicht vor
einem gekaperten GitHub-Konto). Es werden ausschließlich Dateien von `github.com/Treaffox/schleimigescreenshots/releases/download/` akzeptiert.

**Release-Konvention (damit Updates funktionieren):** Tag `vX.Y.Z`; Dateien `SchleimigeScreenshots-Setup-X.Y.Z.exe` und
`SchleimigeScreenshots.exe`; die Version steht in `Directory.Build.props`.

## Bauen
Windows (PowerShell, .NET 10 SDK; für den Installer zusätzlich [Inno Setup 6](https://jrsoftware.org/isdl.php)):

    ./build.ps1          # Tests, Publish nach dist\ und – falls Inno Setup gefunden wird – dist\SchleimigeScreenshots-Setup-<Version>.exe

Linux/macOS (Docker; Inno Setup läuft im Container unter Wine):

    dotnet publish src/App -c Release -o dist
    installer/build.sh   # baut beim ersten Mal das Image installer/docker, dann dist/SchleimigeScreenshots-Setup-<Version>.exe

Programmsymbol und Setup-Bilder entstehen aus dem Logo `assets/code-scanner-solid.svg` (eingefärbt) mit `python3 tools/make_icon.py`.

## Fehlersuche / Debug-Modus
Tray-Symbol → Rechtsklick → **Debug-Modus**. Danach erscheint nach jedem Scan ein Fenster mit einem kopierbaren Bericht
(Windows/.NET/DPI/Monitore, Aufnahme samt Pixelstatistik, gewählter Bereich, Erkennung pro Bildregion, ausgeführte Aktionen, Fehler samt Stacktrace).

- **Alles kopieren** legt den Bericht in die Zwischenablage; **Aufnahme speichern …** sichert das gescannte Bild als PNG.
- **Selbsttest** prüft die Codeerkennung an einem künstlichen Testbild (trennt „Erkennung kaputt“ von „Aufnahme kaputt“).
- Unerwartete Fehler zeigen den Bericht auch ohne Debug-Modus. Zusätzlich landet alles in `%LocalAppData%\SchleimigeScreenshots\log.txt`.
