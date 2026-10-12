using System.Text.Json;
using SchleimigeScreenshots.Core;

namespace SchleimigeScreenshots;

public sealed class AppSettings
{
    public HotkeyGesture Hotkey { get; set; } = HotkeyGesture.Default;
    public bool CopyToClipboard { get; set; } = true;
    public bool OpenInBrowser { get; set; } = true;
    /// <summary>Text, der keine Webadresse ist (z. B. EAN), per Websuche öffnen.</summary>
    public bool SearchNonUrls { get; set; } = true;
    public string SearchTemplate { get; set; } = WebTarget.DefaultSearchTemplate;
    public bool ShowNotifications { get; set; } = true;
    /// <summary>Schutz vor Tab-Flut, wenn viele Codes auf dem Bildschirm sind.</summary>
    public int MaxBrowserTabs { get; set; } = 5;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SchleimigeScreenshots", "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json);
                if (s is not null) return Sanitize(s);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Einstellungen unlesbar, verwende Standardwerte", ex);
            try { File.Move(_path, _path + ".bad", overwrite: true); } catch { }
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Einstellungen konnten nicht gespeichert werden", ex);
        }
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        if (s.Hotkey is null || !s.Hotkey.IsValid) s.Hotkey = HotkeyGesture.Default;
        s.MaxBrowserTabs = Math.Clamp(s.MaxBrowserTabs, 1, 20);
        if (string.IsNullOrWhiteSpace(s.SearchTemplate) || !s.SearchTemplate.Contains("{0}"))
            s.SearchTemplate = WebTarget.DefaultSearchTemplate;
        return s;
    }
}
