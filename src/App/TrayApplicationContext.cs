using System.Diagnostics;
using SchleimigeScreenshots.Core;
using SchleimigeScreenshots.Core.Updates;

namespace SchleimigeScreenshots;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string TriggerHotkey = "Hotkey";
    private const string TriggerMenu = "Menü „Jetzt scannen“";
    private const string TriggerMenuFull = "Menü „Ganzen Bildschirm scannen“";

    private readonly SettingsStore _store = new();
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeys = new();
    private readonly Icon _icon = TrayIcon.Create();
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _scanItem;
    private readonly ToolStripMenuItem _scanFullItem;
    private readonly ToolStripMenuItem _debugItem;
    private readonly ToolStripMenuItem _lastReportItem;
    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private DebugWindow? _debugWindow;
    private RegionSelection? _selection;
    private readonly UpdateService _updates;
    private readonly ToolStripMenuItem _checkUpdatesItem;
    private readonly ToolStripMenuItem _installUpdateItem;
    private bool _updating;
    private string? _lastReport;
    private string _lastHeadline = "";
    private bool _busy;

    public TrayApplicationContext(string[] args)
    {
        _settings = _store.Load();

        _scanItem = new ToolStripMenuItem("Jetzt scannen", null, (_, _) => BeginScan(TriggerMenu, _settings.SelectRegion));
        _scanFullItem = new ToolStripMenuItem("Ganzen Bildschirm scannen", null, (_, _) => BeginScan(TriggerMenuFull, false));
        _debugItem = new ToolStripMenuItem("Debug-Modus (Bericht nach jedem Scan)", null, (_, _) => ToggleDebug())
        {
            Checked = _settings.DebugMode,
        };
        _lastReportItem = new ToolStripMenuItem("Letzten Bericht anzeigen", null, (_, _) => ShowLastReport())
        {
            Enabled = false,
        };
        _autostartItem = new ToolStripMenuItem("Mit Windows starten", null, (_, _) => ToggleAutostart())
        {
            Checked = AutoStart.IsEnabled(),
        };
        _installUpdateItem = new ToolStripMenuItem("Update installieren …", null, async (_, _) => await InstallPendingUpdateAsync())
        {
            Visible = false,
        };
        _installUpdateItem.Font = new Font(_installUpdateItem.Font, FontStyle.Bold);
        _checkUpdatesItem = new ToolStripMenuItem("Nach Updates suchen …", null, async (_, _) => await CheckForUpdatesManuallyAsync());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            new ToolStripMenuItem($"Schleimige Screenshots {AppVersion.Current}") { Enabled = false },
            new ToolStripSeparator(),
            _scanItem,
            _scanFullItem,
            new ToolStripMenuItem("Einstellungen …", null, (_, _) => ShowSettings()),
            new ToolStripSeparator(),
            _debugItem,
            _lastReportItem,
            new ToolStripSeparator(),
            _installUpdateItem,
            _checkUpdatesItem,
            _autostartItem,
            new ToolStripMenuItem("Beenden", null, (_, _) => ExitThread()),
        ]);

        _tray = new NotifyIcon { Icon = _icon, ContextMenuStrip = menu, Visible = true };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        _hotkeys.Pressed += () => BeginScan(TriggerHotkey, _settings.SelectRegion);
        _hotkeys.CloseRequested += () =>
        {
            Log.Info("Beenden angefordert (WM_CLOSE)");
            ExitThread();
        };
        if (_hotkeys.Register(_settings.Hotkey, out var error))
        {
            UpdateTooltip();
        }
        else
        {
            Log.Error("Hotkey-Registrierung fehlgeschlagen: " + error);
            UpdateTooltip();
            // Nach dem Start Einstellungen öffnen, damit der Nutzer eine freie Kombination wählen kann.
            SynchronizationContext.Current?.Post(_ =>
            {
                MessageBox.Show(error + "\n\nBitte wähle eine andere Tastenkombination.",
                    "Schleimige Screenshots", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowSettings();
            }, null);
        }
        Log.Info($"Gestartet ({DebugInfo.Version}), Hotkey {_settings.Hotkey}, Debug-Modus {(_settings.DebugMode ? "an" : "aus")}");

        UpdateService.Cleanup();
        _updates = new UpdateService(() => _settings, () => _store.Save(_settings));
        _updates.UpdateFound += OnUpdateFound;
        _updates.Start();

        // Startparameter --settings öffnet gleich den Einstellungsdialog (praktisch für Tests und Verknüpfungen)
        if (args.Contains("--settings")) SynchronizationContext.Current?.Post(_ => ShowSettings(), null);

        if (args.Contains("--updated"))
        {
            Log.Info($"Nach Update gestartet: Version {AppVersion.Current}");
            SynchronizationContext.Current?.Post(_ =>
                Notify("Aktualisiert", $"Schleimige Screenshots läuft jetzt in Version {AppVersion.Current}.", ToolTipIcon.Info), null);
        }
    }

    private void UpdateTooltip() =>
        _tray.Text = $"Schleimige Screenshots {AppVersion.Current} – {_settings.Hotkey}";

    // ---- Updates ----------------------------------------------------------------------------------------------

    private void ShowPendingUpdate(UpdateInfo update)
    {
        _installUpdateItem.Text = $"Update auf Version {update.Version} installieren …";
        _installUpdateItem.Visible = true;
    }

    private async void OnUpdateFound(UpdateInfo update)
    {
        try
        {
            ShowPendingUpdate(update);
            if (!_settings.AutoInstallUpdates)
            {
                Notify("Update verfügbar", $"Version {update.Version} ist verfügbar. Tray-Menü → „Update installieren“.", ToolTipIcon.Info);
                return;
            }

            // Nicht mitten in einem Scan oder bei geöffneten Einstellungen neu starten.
            for (var i = 0; i < 18 && (_busy || _settingsForm is { IsDisposed: false }); i++)
                await Task.Delay(10_000);
            if (_busy || _settingsForm is { IsDisposed: false })
            {
                Log.Info("Update verfügbar, aber die App ist gerade beschäftigt – Installation bleibt über das Menü möglich");
                return;
            }
            await InstallUpdateAsync(update, interactive: false);
        }
        catch (Exception ex)
        {
            Log.Error("Automatisches Update", ex);
        }
    }

    private async Task InstallUpdateAsync(UpdateInfo update, bool interactive)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            Log.Info($"Installiere Update {update.Tag} ({UpdateService.DetectInstallKind()})");
            Notify($"Update auf {update.Version}", "Wird heruntergeladen und installiert – die App startet gleich neu.", ToolTipIcon.Info);
            var error = await _updates.InstallAsync(update);
            if (error is null)
            {
                Log.Info("Update gestartet – beende diese Instanz");
                ExitThread();
                return;
            }

            Log.Error("Update nicht installiert: " + error);
            if (interactive)
            {
                var open = MessageBox.Show($"{error}\n\nSoll die Download-Seite im Browser geöffnet werden?", "Update fehlgeschlagen",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (open == DialogResult.Yes) OpenReleasePage(update);
            }
            else
            {
                Notify("Update fehlgeschlagen", error, ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Update-Installation", ex);
            if (interactive) Program.ShowFatal("Update fehlgeschlagen", ex);
        }
        finally
        {
            _updating = false;
        }
    }

    private async Task InstallPendingUpdateAsync()
    {
        if (_updates.Pending is not { } update) return;
        var answer = MessageBox.Show($"Version {update.Version} jetzt installieren?\nDie App startet dabei neu.",
            "Update installieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) await InstallUpdateAsync(update, interactive: true);
    }

    private async Task CheckForUpdatesManuallyAsync()
    {
        _checkUpdatesItem.Enabled = false;
        try
        {
            var result = await _updates.CheckAsync();
            if (result.Error is not null)
            {
                MessageBox.Show("Die Suche nach Updates ist fehlgeschlagen:\n\n" + result.Error, "Updates",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (result.Update is not { } update)
            {
                MessageBox.Show($"Du verwendest die aktuelle Version ({AppVersion.Current}).", "Updates",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                ShowPendingUpdate(update);
                var notes = update.Notes.Length > 700 ? update.Notes[..700] + "…" : update.Notes;
                var answer = MessageBox.Show(
                    $"Version {update.Version} ist verfügbar (installiert: {AppVersion.Current}).\n\n{notes}\n\nJetzt installieren? Die App startet dabei neu.",
                    "Update verfügbar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes) await InstallUpdateAsync(update, interactive: true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Manuelle Update-Prüfung", ex);
            Program.ShowFatal("Update-Prüfung fehlgeschlagen", ex);
        }
        finally
        {
            _checkUpdatesItem.Enabled = true;
        }
    }

    private static void OpenReleasePage(UpdateInfo update)
    {
        // Nur die Release-Seite dieses Projekts öffnen
        if (!update.PageUrl.StartsWith($"https://github.com/{UpdateService.Repository}/", StringComparison.Ordinal)) return;
        try { Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Release-Seite öffnen", ex); }
    }

    // ---- Scan -------------------------------------------------------------------------------------------------

    private async void BeginScan(string trigger, bool selectRegion)
    {
        if (_busy)
        {
            // Hotkey während der Bereichsauswahl bricht diese ab (wie bei Screenshot-Programmen üblich).
            if (_selection is not null)
            {
                Log.Info($"Bereichsauswahl durch erneuten Aufruf ({trigger}) abgebrochen");
                _selection.Cancel();
            }
            else
            {
                Log.Info($"Scan ({trigger}) ignoriert: ein Scan läuft bereits");
            }
            return;
        }

        _busy = true;
        _scanItem.Enabled = _scanFullItem.Enabled = false;
        try
        {
            await ScanAsync(trigger, selectRegion);
        }
        catch (Exception ex)
        {
            // async void darf nie eine Ausnahme nach außen lassen – sonst wäre sie unsichtbar oder würde die App beenden.
            Log.Error("Scan: unerwarteter Fehler", ex);
            Program.ShowFatal("Unerwarteter Fehler beim Scannen", ex);
        }
        finally
        {
            _busy = false;
            _scanItem.Enabled = _scanFullItem.Enabled = true;
        }
    }

    private async Task ScanAsync(string trigger, bool selectRegion)
    {
        var total = Stopwatch.StartNew();
        var report = new ScanReport();
        report.Title($"Schleimige Screenshots – Debug-Bericht ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
        report.Line($"Auslöser: {trigger} | Modus: {(selectRegion ? "Bereichsauswahl" : "ganzer Bildschirm")}");
        try
        {
            foreach (var line in DebugInfo.Describe(_settings)) report.Line(line);
        }
        catch (Exception ex)
        {
            // Diagnose darf den eigentlichen Scan nie verhindern.
            report.Warn("Umgebungsangaben nicht verfügbar: " + ex.Message);
        }

        string headline;
        string body;
        var icon = ToolTipIcon.Info;
        CaptureResult? shot = null; // das gescannte Bild: ganzer Bildschirm oder der gewählte Bereich
        var cancelled = false;
        var selectionTime = TimeSpan.Zero; // Wartezeit des Nutzers gehört nicht zur Scandauer
        var debugWasVisible = false;

        try
        {
            // Debug-Fenster bzw. das gerade geschlossene Kontextmenü dürfen nicht im Screenshot landen.
            var debugVisible = debugWasVisible = _debugWindow is { Visible: true };
            if (debugVisible) _debugWindow!.Hide();
            if (debugVisible || trigger is TriggerMenu or TriggerMenuFull) await Task.Delay(300);

            report.Section("Aufnahme");
            shot = ScreenCapture.CaptureVirtualScreen();
            DescribeCapture(report, shot);

            if (selectRegion)
            {
                var selectWatch = Stopwatch.StartNew();
                var region = await SelectRegionAsync(shot);
                if (region is { } picked)
                {
                    var r = Rectangle.Intersect(picked, new Rectangle(0, 0, shot.Width, shot.Height)); // Sicherheitsnetz gegen Randfälle
                    if (r.Width <= 0 || r.Height <= 0) throw new InvalidOperationException($"Der gewählte Bereich {picked} liegt außerhalb der Aufnahme.");
                    report.Section("Bereichsauswahl");
                    selectionTime = selectWatch.Elapsed;
                    report.Line($"Bereich: {r.Width} x {r.Height} px @ ({r.X},{r.Y}) | Auswahl dauerte {selectionTime.TotalSeconds:0.0} s");
                    shot = shot.Crop(r);
                    DescribeStats(report, shot, warnIfBlank: false);
                }
                else
                {
                    cancelled = true;
                }
            }

            if (cancelled)
            {
                headline = body = "";
            }
            else
            {
                report.Section("Erkennung");
                var outcome = await DetectAsync(shot);
                DescribeDetection(report, outcome);

                var results = outcome.Results;
                var fromText = false;
                if (results.Count == 0 && _settings.RecognizeTextUrls)
                {
                    report.Section("Texterkennung (nur Webadressen)");
                    results = await DetectTextUrlsAsync(shot, report);
                    fromText = results.Count > 0;
                }

                report.Section("Ergebnis");
                if (results.Count == 0)
                {
                    headline = "Kein Code gefunden";
                    body = selectRegion
                        ? "Im gewählten Bereich wurde kein QR-Code, Barcode oder Link erkannt."
                        : "Auf dem Bildschirm wurde kein QR-Code, Barcode oder Link erkannt.";
                    report.Line("Kein Code gefunden.");
                }
                else
                {
                    (headline, body, icon) = Act(results, report);
                    if (fromText) headline = results.Count == 1 ? "Link im Text erkannt" : $"{results.Count} Links im Text erkannt";
                }
            }
        }
        catch (OperationCanceledException)
        {
            headline = "Zeitüberschreitung";
            body = "Die Suche nach Codes hat zu lange gedauert (über 30 s).";
            icon = ToolTipIcon.Warning;
            report.Error("Zeitüberschreitung: Die Erkennung dauerte länger als 30 s und wurde abgebrochen.");
        }
        catch (Exception ex)
        {
            headline = "Fehler: " + ex.GetType().Name;
            body = ex.Message;
            icon = ToolTipIcon.Error;
            report.Error("Scan", ex);
        }

        if (cancelled)
        {
            Log.Info("Scan abgebrochen (Bereichsauswahl)");
            if (debugWasVisible && _debugWindow is { IsDisposed: false }) _debugWindow.Show(); // das zuvor offene Debug-Fenster zurückholen
            return;
        }

        report.Section("Zusammenfassung");
        report.Line($"{headline} – Gesamtdauer {(total.Elapsed - selectionTime).TotalMilliseconds:0} ms" +
                    (selectionTime > TimeSpan.Zero ? " (ohne Auswahlzeit)" : ""));

        var text = report.ToString();
        _lastReport = text;
        _lastHeadline = headline;
        _lastReportItem.Enabled = true;

        if (report.HasError) Log.Error("Scan-Bericht:" + Environment.NewLine + text);
        else if (_settings.DebugMode) Log.Info("Scan-Bericht:" + Environment.NewLine + text);
        else Log.Info(headline);

        // Fehler werden nie nur als (leicht unterdrückte) Sprechblase gemeldet, sondern immer als lesbarer Bericht.
        if (_settings.DebugMode || report.HasError)
            ShowDebugWindow(headline, text, shot);
        else
            Notify(headline, body, icon);
    }

    private static async Task<ScanOutcome> DetectAsync(CaptureResult shot)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await Task.Run(() => CodeScanner.ScanDetailed(shot.Pixels, shot.Width, shot.Height, cts.Token));
    }

    /// <summary>Liest den Text im Bild (OCR) und behält ausschließlich Webadressen; sonstiger Text wird verworfen.</summary>
    private static async Task<IReadOnlyList<ScanResult>> DetectTextUrlsAsync(CaptureResult shot, ScanReport report)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var text = await TextRecognizer.RecognizeAsync(shot.Pixels, shot.Width, shot.Height, cts.Token);
            if (text is null)
            {
                report.Warn("Texterkennung nicht verfügbar: Windows hat keine OCR-Sprache installiert (Einstellungen → Zeit & Sprache).");
                return [];
            }
            var urls = TextUrlExtractor.Extract(text);
            if (TextRecognizer.LastRapidError is { } rerr) report.Warn("RapidOCR fehlgeschlagen, Fallback auf Windows-OCR: " + rerr);
            report.Line($"Texterkennung ({TextRecognizer.LastEngine}): {text.Length} Zeichen, {urls.Count} Webadresse(n), {watch.ElapsedMilliseconds} ms");
            return urls.Select(u => new ScanResult(u, "TEXT-URL", 0, 0)).ToList();
        }
        catch (OperationCanceledException)
        {
            report.Warn("Texterkennung: Zeitüberschreitung (30 s).");
            return [];
        }
        catch (Exception ex)
        {
            // OCR ist nur ein Zusatz; ein Fehler dort darf den normalen Ablauf nicht kippen.
            report.Warn("Texterkennung fehlgeschlagen: " + ex.Message);
            return [];
        }
    }

    private async Task<Rectangle?> SelectRegionAsync(CaptureResult shot)
    {
        var selection = new RegionSelection(shot);
        _selection = selection;
        try
        {
            selection.Show();
            return await selection.Result;
        }
        finally
        {
            _selection = null;
        }
    }

    private static void DescribeCapture(ScanReport report, CaptureResult shot)
    {
        report.Line($"Methode: {shot.Method} | Dauer: {shot.Milliseconds:0} ms");
        report.Line($"Größe: {shot.Width} x {shot.Height} px ({shot.Pixels.Length / 1048576.0:0.0} MB) @ ({shot.Bounds.X},{shot.Bounds.Y})");
        foreach (var note in shot.Notes) report.Warn(note);
        DescribeStats(report, shot, warnIfBlank: true);
    }

    private static void DescribeStats(ScanReport report, CaptureResult shot, bool warnIfBlank)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            var st = ImageStats.Compute(shot.Pixels, shot.Width, shot.Height);
            report.Line($"Pixelstatistik (Stichprobe, {watch.ElapsedMilliseconds} ms): Helligkeit min/Ø/max = {st.MinLuma} / {st.MeanLuma:0.0} / {st.MaxLuma}" +
                        $" | fast schwarz {st.NearBlackShare:P0} | fast weiß {st.NearWhiteShare:P0}" +
                        $" | Alpha=0 bei {st.ZeroAlphaShare:P0} | Farben {st.DistinctColors}{(st.DistinctColorsCapped ? "+" : "")}");
            if (warnIfBlank && st.LooksBlank)
                report.Warn("Die Aufnahme ist praktisch einfarbig – der Bildschirm wurde vermutlich nicht richtig erfasst " +
                            "(gesperrte Sitzung, Remotedesktop, geschützter Inhalt oder Treiberproblem).");
        }
        catch (Exception ex)
        {
            report.Warn("Pixelstatistik nicht verfügbar: " + ex.Message);
        }
    }

    private static void DescribeDetection(ScanReport report, ScanOutcome outcome)
    {
        foreach (var pass in outcome.Passes)
        {
            var hits = pass.Regions.Sum(r => r.Hits);
            report.Line($"Durchlauf „{pass.Name}“: {pass.Regions.Count} Regionen, {pass.Milliseconds:0} ms, {hits} Treffer (vor Entduplizierung)");
            for (int i = 0; i < pass.Regions.Count; i++)
            {
                var r = pass.Regions[i];
                report.Line($"  #{i:00}  {r.X},{r.Y}  {r.Width}x{r.Height}  →  {r.Hits} Treffer, {r.Milliseconds:0} ms{(r.Error is null ? "" : "  [FEHLER]")}");
            }
        }

        // Regionsfehler allein sind harmlos, solange andere Regionen Codes gefunden haben; sonst sind sie die Fehlerursache.
        foreach (var r in outcome.Passes.SelectMany(p => p.Regions).Where(r => r.Error is not null))
        {
            var message = $"Fehler in Region {r.X},{r.Y} {r.Width}x{r.Height}:{Environment.NewLine}    " +
                          r.Error!.Replace("\n", Environment.NewLine + "    ");
            if (outcome.Results.Count == 0) report.Error(message);
            else report.Warn(message);
        }

        report.Line($"Erkennung gesamt: {outcome.Milliseconds:0} ms");
    }

    private (string Headline, string Body, ToolTipIcon Icon) Act(IReadOnlyList<ScanResult> results, ScanReport report)
    {
        var s = _settings;
        var texts = results.Select(r => r.Text).ToList();
        var problems = new List<string>();

        report.Line($"Gefunden: {results.Count}");
        for (int i = 0; i < results.Count; i++)
            report.Line($"  {i + 1}. [{results[i].Format}] bei ({results[i].X:0},{results[i].Y:0}): {Shorten(results[i].Text, 300)}");

        report.Line();
        report.Line("Aktionen:");
        if (s.CopyToClipboard)
        {
            try
            {
                Clipboard.SetDataObject(string.Join(Environment.NewLine, texts), copy: true, retryTimes: 10, retryDelay: 100);
                report.Line($"  Zwischenablage: OK ({texts.Count} Zeile(n))");
            }
            catch (Exception ex)
            {
                report.Error("Zwischenablage", ex);
                problems.Add("Zwischenablage nicht verfügbar");
            }
        }
        else
        {
            report.Line("  Zwischenablage: aus");
        }

        if (s.OpenInBrowser)
        {
            var targets = new List<(Uri Uri, string Kind)>();
            foreach (var t in texts)
            {
                var web = WebTarget.TryGetWebUri(t);
                if (web is not null) targets.Add((web, "Link"));
                else if (s.SearchNonUrls) targets.Add((WebTarget.BuildSearchUri(s.SearchTemplate, t), "Websuche"));
                else report.Line($"  Browser: übersprungen (kein Link, Websuche aus): {Shorten(t, 120)}");
            }
            targets = targets.DistinctBy(x => x.Uri.AbsoluteUri).ToList();

            foreach (var (uri, kind) in targets.Take(s.MaxBrowserTabs))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    report.Line($"  Browser: {kind} geöffnet: {Shorten(uri.AbsoluteUri, 300)}");
                }
                catch (Exception ex)
                {
                    report.Error($"Browser starten ({uri.Host})", ex);
                    problems.Add("Browser konnte nicht gestartet werden");
                    break;
                }
            }
            if (targets.Count > s.MaxBrowserTabs)
            {
                report.Line($"  Browser: {targets.Count - s.MaxBrowserTabs} weitere übersprungen (Limit {s.MaxBrowserTabs})");
                problems.Add($"nur {s.MaxBrowserTabs} von {targets.Count} Links geöffnet");
            }
        }
        else
        {
            report.Line("  Browser: aus");
        }

        var first = Shorten(texts[0], 120);
        var body = texts.Count == 1 ? first : $"{first}\n(+{texts.Count - 1} weitere)";
        if (problems.Count > 0) body += "\n" + string.Join("; ", problems);

        return (texts.Count == 1 ? "Code erkannt" : $"{texts.Count} Codes erkannt", body,
            problems.Count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private static string Shorten(string text, int max)
    {
        var t = text.Replace('\r', ' ').Replace('\n', ' ');
        return t.Length > max ? t[..max] + "…" : t;
    }

    private void Notify(string title, string text, ToolTipIcon icon)
    {
        if (!_settings.ShowNotifications && icon is ToolTipIcon.Info) return;
        if (text.Length > 250) text = text[..250] + "…";
        _tray.ShowBalloonTip(4000, title, text, icon);
    }

    // ---- Debug ------------------------------------------------------------------------------------------------

    private void ShowDebugWindow(string headline, string text, CaptureResult? shot)
    {
        if (_debugWindow is null || _debugWindow.IsDisposed) _debugWindow = new DebugWindow();
        _debugWindow.ShowReport(headline, text, shot?.Pixels, shot?.Width ?? 0, shot?.Height ?? 0);
    }

    private void ShowLastReport()
    {
        if (_lastReport is not null) ShowDebugWindow(_lastHeadline, _lastReport, null);
    }

    private void ToggleDebug()
    {
        _settings.DebugMode = !_settings.DebugMode;
        _debugItem.Checked = _settings.DebugMode;
        _store.Save(_settings);
        Log.Info("Debug-Modus " + (_settings.DebugMode ? "an" : "aus"));
    }

    // ---- Einstellungen ----------------------------------------------------------------------------------------

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        using var form = _settingsForm = new SettingsForm(_settings, AutoStart.IsEnabled());
        try
        {
            while (form.ShowDialog() == DialogResult.OK)
            {
                var updated = form.Result;
                var old = _settings.Hotkey;
                if (!_hotkeys.Register(updated.Hotkey, out var error))
                {
                    _hotkeys.Register(old, out _); // alten Hotkey wiederherstellen
                    MessageBox.Show(form, error, "Hotkey nicht verfügbar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }

                _settings = updated;
                _store.Save(_settings);
                if (form.AutoStartEnabled != AutoStart.IsEnabled())
                    AutoStart.Set(form.AutoStartEnabled);
                _autostartItem.Checked = AutoStart.IsEnabled();
                _debugItem.Checked = _settings.DebugMode;
                UpdateTooltip();
                break;
            }
        }
        finally
        {
            _settingsForm = null;
        }
    }

    private void ToggleAutostart()
    {
        var target = !AutoStart.IsEnabled();
        if (!AutoStart.Set(target))
            Notify("Autostart", "Der Autostart konnte nicht geändert werden.", ToolTipIcon.Warning);
        _autostartItem.Checked = AutoStart.IsEnabled();
    }

    protected override void ExitThreadCore()
    {
        _selection?.Cancel();
        _updates.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _hotkeys.Dispose();
        _debugWindow?.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
