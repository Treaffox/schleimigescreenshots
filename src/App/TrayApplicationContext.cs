using System.Diagnostics;
using SchleimigeScreenshots.Core;

namespace SchleimigeScreenshots;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string TriggerHotkey = "Hotkey";
    private const string TriggerMenu = "Menü „Jetzt scannen“";

    private readonly SettingsStore _store = new();
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeys = new();
    private readonly Icon _icon = TrayIcon.Create();
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _scanItem;
    private readonly ToolStripMenuItem _debugItem;
    private readonly ToolStripMenuItem _lastReportItem;
    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private DebugWindow? _debugWindow;
    private string? _lastReport;
    private string _lastHeadline = "";
    private bool _busy;

    public TrayApplicationContext()
    {
        _settings = _store.Load();

        _scanItem = new ToolStripMenuItem("Jetzt scannen", null, (_, _) => BeginScan(TriggerMenu));
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
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _scanItem,
            new ToolStripMenuItem("Einstellungen …", null, (_, _) => ShowSettings()),
            new ToolStripSeparator(),
            _debugItem,
            _lastReportItem,
            new ToolStripSeparator(),
            _autostartItem,
            new ToolStripMenuItem("Beenden", null, (_, _) => ExitThread()),
        ]);

        _tray = new NotifyIcon { Icon = _icon, ContextMenuStrip = menu, Visible = true };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        _hotkeys.Pressed += () => BeginScan(TriggerHotkey);
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
    }

    private void UpdateTooltip() =>
        _tray.Text = $"Schleimige Screenshots – {_settings.Hotkey}";

    // ---- Scan -------------------------------------------------------------------------------------------------

    private async void BeginScan(string trigger)
    {
        if (_busy)
        {
            Log.Info($"Scan ({trigger}) ignoriert: ein Scan läuft bereits");
            return;
        }

        _busy = true;
        _scanItem.Enabled = false;
        try
        {
            await ScanAsync(trigger);
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
            _scanItem.Enabled = true;
        }
    }

    private async Task ScanAsync(string trigger)
    {
        var total = Stopwatch.StartNew();
        var report = new ScanReport();
        report.Title($"Schleimige Screenshots – Debug-Bericht ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
        report.Line($"Auslöser: {trigger}");
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
        CaptureResult? shot = null;

        try
        {
            // Debug-Fenster bzw. das gerade geschlossene Kontextmenü dürfen nicht im Screenshot landen.
            var debugVisible = _debugWindow is { Visible: true };
            if (debugVisible) _debugWindow!.Hide();
            if (debugVisible || trigger == TriggerMenu) await Task.Delay(300);

            report.Section("Aufnahme");
            shot = ScreenCapture.CaptureVirtualScreen();
            DescribeCapture(report, shot);

            report.Section("Erkennung");
            var outcome = await DetectAsync(shot);
            DescribeDetection(report, outcome);

            report.Section("Ergebnis");
            if (outcome.Results.Count == 0)
            {
                headline = "Kein Code gefunden";
                body = "Auf dem Bildschirm wurde kein QR-Code oder Barcode erkannt.";
                report.Line("Kein Code gefunden.");
            }
            else
            {
                (headline, body, icon) = Act(outcome.Results, report);
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

        report.Section("Zusammenfassung");
        report.Line($"{headline} – Gesamtdauer {total.ElapsedMilliseconds} ms");

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

    private static void DescribeCapture(ScanReport report, CaptureResult shot)
    {
        report.Line($"Methode: {shot.Method} | Dauer: {shot.Milliseconds:0} ms");
        report.Line($"Größe: {shot.Width} x {shot.Height} px ({shot.Pixels.Length / 1048576.0:0.0} MB) @ ({shot.Bounds.X},{shot.Bounds.Y})");
        foreach (var note in shot.Notes) report.Warn(note);

        try
        {
            var watch = Stopwatch.StartNew();
            var st = ImageStats.Compute(shot.Pixels, shot.Width, shot.Height);
            report.Line($"Pixelstatistik (Stichprobe, {watch.ElapsedMilliseconds} ms): Helligkeit min/Ø/max = {st.MinLuma} / {st.MeanLuma:0.0} / {st.MaxLuma}" +
                        $" | fast schwarz {st.NearBlackShare:P0} | fast weiß {st.NearWhiteShare:P0}" +
                        $" | Alpha=0 bei {st.ZeroAlphaShare:P0} | Farben {st.DistinctColors}{(st.DistinctColorsCapped ? "+" : "")}");
            if (st.LooksBlank)
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
        _tray.Visible = false;
        _tray.Dispose();
        _hotkeys.Dispose();
        _debugWindow?.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
