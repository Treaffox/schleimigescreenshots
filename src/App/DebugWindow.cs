using System.Diagnostics;

namespace SchleimigeScreenshots;

/// <summary>Zeigt den Debug-Bericht eines Scans als markier- und kopierbaren Text.</summary>
internal sealed class DebugWindow : Form
{
    private readonly Label _headline = new() { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
    private readonly TextBox _text = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9.5f), BackColor = SystemColors.Window, HideSelection = false,
    };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 0) };
    private readonly Button _copy = new() { Text = "Alles kopieren", AutoSize = true, MinimumSize = new Size(110, 0) };
    private readonly Button _save = new() { Text = "Aufnahme speichern …", AutoSize = true, MinimumSize = new Size(110, 0) };
    private readonly Button _selfTest = new() { Text = "Selbsttest", AutoSize = true, MinimumSize = new Size(90, 0) };
    private readonly Button _folder = new() { Text = "Log-Ordner", AutoSize = true, MinimumSize = new Size(90, 0) };
    private readonly Button _close = new() { Text = "Schließen", AutoSize = true, MinimumSize = new Size(90, 0) };
    private readonly Icon _icon = TrayIcon.Create();

    private byte[]? _pixels;
    private int _pixelWidth, _pixelHeight;

    public DebugWindow()
    {
        Text = "Schleimige Screenshots – Debug";
        Icon = _icon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(960, 680);
        MinimumSize = new Size(560, 380);
        TopMost = true; // der Bericht soll nie hinter anderen Fenstern verschwinden
        KeyPreview = true;

        _copy.Click += (_, _) => CopyAll();
        _save.Click += (_, _) => SaveCapture();
        _selfTest.Click += (_, _) => RunSelfTest();
        _folder.Click += (_, _) => OpenLogFolder();
        _close.Click += (_, _) => Close();
        CancelButton = _close;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        buttons.Controls.AddRange([_copy, _save, _selfTest, _folder, _close]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_headline, 0, 0);
        layout.Controls.Add(_text, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        Controls.Add(layout);
    }

    public void ShowReport(string headline, string report, byte[]? pixels, int width, int height)
    {
        _pixels = pixels;
        _pixelWidth = width;
        _pixelHeight = height;
        _headline.Text = headline;
        _text.Text = report;
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;
        _save.Enabled = pixels is not null;
        _status.Text = "Tipp: „Alles kopieren“ und den Text dort einfügen, wo du Hilfe suchst.";

        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (!Visible) Show();
        Activate();
        BringToFront();
        _text.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Schließen versteckt nur – Größe/Position bleiben erhalten; die große Aufnahme wird freigegeben.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _pixels = null;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon.Dispose();
        base.Dispose(disposing);
    }

    private void CopyAll()
    {
        try
        {
            Clipboard.SetDataObject(_text.Text, copy: true, retryTimes: 10, retryDelay: 100);
            _status.Text = $"In die Zwischenablage kopiert ({_text.Text.Length} Zeichen).";
        }
        catch (Exception ex)
        {
            _status.Text = "Kopieren fehlgeschlagen – bitte Text markieren (Strg+A) und mit Strg+C kopieren.";
            Log.Error("Debug-Fenster: Zwischenablage", ex);
        }
    }

    private void SaveCapture()
    {
        if (_pixels is null) return;
        using var dialog = new SaveFileDialog
        {
            Title = "Aufnahme speichern",
            Filter = "PNG-Bild (*.png)|*.png",
            FileName = $"aufnahme-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            ScreenCapture.SavePng(_pixels, _pixelWidth, _pixelHeight, dialog.FileName);
            _status.Text = "Aufnahme gespeichert: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            _status.Text = "Speichern fehlgeschlagen: " + ex.Message;
            Log.Error("Debug-Fenster: Aufnahme speichern", ex);
        }
    }

    private async void RunSelfTest()
    {
        _selfTest.Enabled = false;
        _status.Text = "Selbsttest läuft …";
        try
        {
            var (ok, message) = await Task.Run(Core.SelfTest.Run);
            _text.AppendText($"{Environment.NewLine}[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            _status.Text = message.Split('\n')[0];
            if (!ok) Log.Error("Selbsttest: " + message);
        }
        catch (Exception ex)
        {
            _text.AppendText($"{Environment.NewLine}Selbsttest-Ausnahme: {ex}{Environment.NewLine}");
            _status.Text = "Selbsttest fehlgeschlagen.";
        }
        finally
        {
            _selfTest.Enabled = true;
        }
    }

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Log-Ordner öffnen", ex);
        }
    }
}
