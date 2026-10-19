namespace SchleimigeScreenshots;

/// <summary>Kleines Fortschrittsfenster für den Update-Download (das Setup ist ~75 MB groß).</summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 36, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 22, Maximum = 1000 };

    public UpdateProgressForm(string version)
    {
        Text = "Update auf Version " + version;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(380, 90);
        Padding = new Padding(14, 12, 14, 8);
        Controls.Add(_bar);
        Controls.Add(_status);
        _status.Text = "Update wird heruntergeladen …";
    }

    /// <summary>Download-Fortschritt 0..1.</summary>
    public void SetDownload(double fraction)
    {
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Value = (int)Math.Clamp(fraction * 1000, 0, 1000);
        _status.Text = $"Update wird heruntergeladen … {fraction:P0}";
    }

    public void SetInstalling()
    {
        _bar.Style = ProgressBarStyle.Marquee;
        _status.Text = "Installiere … die App startet gleich von selbst neu.";
    }
}
