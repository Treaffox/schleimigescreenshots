using SchleimigeScreenshots.Core;

namespace SchleimigeScreenshots;

internal sealed class SettingsForm : Form
{
    private readonly HotkeyBox _hotkey = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _selectRegion = new() { Text = "Beim Scannen Bereich auswählen (wie ein Screenshot-Programm)", AutoSize = true };
    private readonly CheckBox _clipboard = new() { Text = "Ergebnis in die Zwischenablage kopieren", AutoSize = true };
    private readonly CheckBox _browser = new() { Text = "Im Standardbrowser öffnen", AutoSize = true };
    private readonly CheckBox _search = new() { Text = "Kein Link (z. B. Barcode-Nummer): per Websuche öffnen", AutoSize = true };
    private readonly CheckBox _textUrls = new() { Text = "Auch Webadressen im Text erkennen (nur Links, kein normaler Text)", AutoSize = true };
    private readonly TextBox _template = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _notify = new() { Text = "Benachrichtigung anzeigen", AutoSize = true };
    private readonly CheckBox _autoCheckUpdates = new() { Text = "Automatisch nach Updates suchen (GitHub)", AutoSize = true };
    private readonly CheckBox _autoInstallUpdates = new() { Text = "Updates automatisch installieren (die App startet dabei kurz neu)", AutoSize = true };
    private readonly CheckBox _includePreReleases = new() { Text = "Auch Vorabversionen (Pre-releases) verwenden", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Mit Windows starten", AutoSize = true };
    private readonly CheckBox _debug = new() { Text = "Debug-Modus: nach jedem Scan einen kopierbaren Bericht zeigen", AutoSize = true };
    private readonly AppSettings _original;

    public AppSettings Result { get; private set; }
    public bool AutoStartEnabled => _autostart.Checked;

    public SettingsForm(AppSettings current, bool autostart)
    {
        _original = current;
        Result = current;
        Text = "Schleimige Screenshots – Einstellungen";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Icon = TrayIcon.Create();

        _hotkey.Gesture = current.Hotkey;
        _selectRegion.Checked = current.SelectRegion;
        _clipboard.Checked = current.CopyToClipboard;
        _browser.Checked = current.OpenInBrowser;
        _search.Checked = current.SearchNonUrls;
        _textUrls.Checked = current.RecognizeTextUrls;
        _template.Text = current.SearchTemplate;
        _notify.Checked = current.ShowNotifications;
        _autoCheckUpdates.Checked = current.AutoCheckUpdates;
        _autoInstallUpdates.Checked = current.AutoInstallUpdates;
        _includePreReleases.Checked = current.IncludePreReleases;
        _autoInstallUpdates.Enabled = _includePreReleases.Enabled = _autoCheckUpdates.Checked;
        _autoCheckUpdates.CheckedChanged += (_, _) => _autoInstallUpdates.Enabled = _includePreReleases.Enabled = _autoCheckUpdates.Checked;
        _autostart.Checked = autostart;
        _debug.Checked = current.DebugMode;
        _search.Enabled = _template.Enabled = _browser.Checked;
        _browser.CheckedChanged += (_, _) => _search.Enabled = _template.Enabled = _browser.Checked;

        var ok = new Button { Text = "Speichern", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(90, 0) };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 0) };
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) => Collect();

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([cancel, ok]);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Row(string? label, Control c)
        {
            if (label is null) { layout.Controls.Add(c, 0, layout.RowCount); layout.SetColumnSpan(c, 2); }
            else
            {
                layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 6) }, 0, layout.RowCount);
                layout.Controls.Add(c, 1, layout.RowCount);
            }
            layout.RowCount++;
        }

        Row("Hotkey:", _hotkey);
        Row(null, new Label { Text = "In das Feld klicken und die gewünschte Tastenkombination drücken.", AutoSize = true, ForeColor = SystemColors.GrayText });
        Row(null, _selectRegion);
        Row(null, _clipboard);
        Row(null, _browser);
        Row(null, _textUrls);
        Row(null, _search);
        Row("Suchadresse:", _template);
        Row(null, _notify);
        Row(null, _autoCheckUpdates);
        Row(null, _autoInstallUpdates);
        Row(null, _includePreReleases);
        Row(null, _autostart);
        Row(null, _debug);
        Row(null, buttons);

        Controls.Add(layout);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(480, 0);
    }

    private void Collect()
    {
        var template = _template.Text.Trim();
        if (!template.Contains("{0}") || !Uri.TryCreate(template.Replace("{0}", "x"), UriKind.Absolute, out var u)
            || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            template = WebTarget.DefaultSearchTemplate;

        // Klon des Originals: nicht im Dialog angezeigte Einstellungen (z. B. MaxBrowserTabs) bleiben erhalten.
        var s = _original.Clone();
        s.Hotkey = _hotkey.Gesture;
        s.SelectRegion = _selectRegion.Checked;
        s.CopyToClipboard = _clipboard.Checked;
        s.OpenInBrowser = _browser.Checked;
        s.SearchNonUrls = _search.Checked;
        s.RecognizeTextUrls = _textUrls.Checked;
        s.SearchTemplate = template;
        s.ShowNotifications = _notify.Checked;
        s.AutoCheckUpdates = _autoCheckUpdates.Checked;
        s.AutoInstallUpdates = _autoInstallUpdates.Checked;
        s.IncludePreReleases = _includePreReleases.Checked;
        s.DebugMode = _debug.Checked;
        Result = s;
    }
}
