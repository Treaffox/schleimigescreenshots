using System.Diagnostics;
using SchleimigeScreenshots.Core;

namespace SchleimigeScreenshots;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _store = new();
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeys = new();
    private readonly Icon _icon = TrayIcon.Create();
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _scanItem;
    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private bool _busy;

    public TrayApplicationContext()
    {
        _settings = _store.Load();

        _scanItem = new ToolStripMenuItem("Jetzt scannen", null, (_, _) => BeginScan());
        _autostartItem = new ToolStripMenuItem("Mit Windows starten", null, (_, _) => ToggleAutostart())
        {
            Checked = AutoStart.IsEnabled(),
        };
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _scanItem,
            new ToolStripMenuItem("Einstellungen …", null, (_, _) => ShowSettings()),
            _autostartItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Beenden", null, (_, _) => ExitThread()),
        ]);

        _tray = new NotifyIcon { Icon = _icon, ContextMenuStrip = menu, Visible = true };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        _hotkeys.Pressed += BeginScan;
        if (_hotkeys.Register(_settings.Hotkey, out var error))
        {
            UpdateTooltip();
        }
        else
        {
            Log.Error("Hotkey-Registrierung fehlgeschlagen: " + error);
            UpdateTooltip();
            // Nach dem Start Einstellungen öffnen, damit der Nutzer eine freie Kombination wählen kann.
            var ctx = SynchronizationContext.Current;
            ctx?.Post(_ =>
            {
                MessageBox.Show(error + "\n\nBitte wähle eine andere Tastenkombination.",
                    "Schleimige Screenshots", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowSettings();
            }, null);
        }
        Log.Info($"Gestartet, Hotkey {_settings.Hotkey}");
    }

    private void UpdateTooltip() =>
        _tray.Text = $"Schleimige Screenshots – {_settings.Hotkey}";

    private async void BeginScan()
    {
        if (_busy) return;
        _busy = true;
        _scanItem.Enabled = false;
        try
        {
            var shot = ScreenCapture.CaptureVirtualScreen();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IReadOnlyList<ScanResult> results;
            try
            {
                results = await Task.Run(() => CodeScanner.Scan(shot.Pixels, shot.Width, shot.Height, cts.Token));
            }
            catch (OperationCanceledException)
            {
                Notify("Zeitüberschreitung", "Die Suche nach Codes hat zu lange gedauert.", ToolTipIcon.Warning);
                return;
            }

            if (results.Count == 0)
            {
                Log.Info($"Kein Code gefunden ({shot.Width}x{shot.Height})");
                Notify("Kein Code gefunden", "Auf dem Bildschirm wurde kein QR-Code oder Barcode erkannt.", ToolTipIcon.Info);
                return;
            }

            Log.Info($"{results.Count} Code(s) gefunden: {string.Join(", ", results.Select(r => r.Format))}");
            Handle(results);
        }
        catch (Exception ex)
        {
            Log.Error("Scan fehlgeschlagen", ex);
            Notify("Fehler", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            _scanItem.Enabled = true;
        }
    }

    private void Handle(IReadOnlyList<ScanResult> results)
    {
        var s = _settings;
        var texts = results.Select(r => r.Text).ToList();
        var problems = new List<string>();

        if (s.CopyToClipboard)
        {
            try { Clipboard.SetDataObject(string.Join(Environment.NewLine, texts), copy: true, retryTimes: 10, retryDelay: 100); }
            catch (Exception ex) { Log.Error("Zwischenablage", ex); problems.Add("Zwischenablage nicht verfügbar"); }
        }

        int opened = 0;
        if (s.OpenInBrowser)
        {
            var targets = texts
                .Select(t => WebTarget.TryGetWebUri(t) ?? (s.SearchNonUrls ? WebTarget.BuildSearchUri(s.SearchTemplate, t) : null))
                .Where(u => u is not null)
                .Select(u => u!)
                .DistinctBy(u => u.AbsoluteUri)
                .ToList();

            foreach (var uri in targets.Take(s.MaxBrowserTabs))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    opened++;
                }
                catch (Exception ex) { Log.Error("Browser starten: " + uri.Host, ex); problems.Add("Browser konnte nicht gestartet werden"); break; }
            }
            if (targets.Count > s.MaxBrowserTabs)
                problems.Add($"nur {s.MaxBrowserTabs} von {targets.Count} Links geöffnet");
        }

        var first = texts[0].Replace('\r', ' ').Replace('\n', ' ');
        if (first.Length > 120) first = first[..120] + "…";
        var body = texts.Count == 1 ? first : $"{first}\n(+{texts.Count - 1} weitere)";
        if (problems.Count > 0) body += "\n" + string.Join("; ", problems);

        Notify(texts.Count == 1 ? "Code erkannt" : $"{texts.Count} Codes erkannt", body,
            problems.Count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private void Notify(string title, string text, ToolTipIcon icon)
    {
        if (!_settings.ShowNotifications && icon is ToolTipIcon.Info) return;
        if (text.Length > 250) text = text[..250] + "…";
        _tray.ShowBalloonTip(4000, title, text, icon);
    }

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
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
