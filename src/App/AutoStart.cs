using Microsoft.Win32;

namespace SchleimigeScreenshots;

/// <summary>Autostart pro Benutzer über HKCU\...\Run – keine Administratorrechte nötig.</summary>
internal static class AutoStart
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SchleimigeScreenshots";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Autostart konnte nicht geändert werden", ex);
            return false;
        }
    }
}
