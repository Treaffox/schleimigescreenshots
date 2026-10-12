using System.Globalization;
using System.Runtime.InteropServices;

namespace SchleimigeScreenshots;

/// <summary>Umgebungsangaben für den Debug-Bericht.</summary>
internal static class DebugInfo
{
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

    public static string Version => AppVersion.Informational;

    public static List<string> Describe(AppSettings s)
    {
        var lines = new List<string>
        {
            $"App: Schleimige Screenshots {Version}  ({Environment.ProcessPath})",
            $".NET: {RuntimeInformation.FrameworkDescription} | OS: {RuntimeInformation.OSDescription} " +
            $"| Prozess: {(Environment.Is64BitProcess ? "64" : "32")}-Bit, OS: {(Environment.Is64BitOperatingSystem ? "64" : "32")}-Bit " +
            $"| CPUs: {Environment.ProcessorCount}",
            $"Sitzung: Remotedesktop={(SystemInformation.TerminalServerSession ? "ja" : "nein")} | Kultur={CultureInfo.CurrentUICulture.Name} " +
            $"| DPI-Modus: {DpiMode()}",
            $"Hotkey: {s.Hotkey} | Zwischenablage={Yn(s.CopyToClipboard)} | Browser={Yn(s.OpenInBrowser)} " +
            $"| Websuche={Yn(s.SearchNonUrls)} | max. Tabs={s.MaxBrowserTabs} | Bereichsauswahl={Yn(s.SelectRegion)} | Debug-Modus={Yn(s.DebugMode)}",
            $"Updates: automatisch prüfen={Yn(s.AutoCheckUpdates)} | automatisch installieren={Yn(s.AutoInstallUpdates)} " +
            $"| Vorabversionen={Yn(s.IncludePreReleases)} | Installation={UpdateService.DetectInstallKind()} | letzte Prüfung={(s.LastUpdateCheckUtc is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "nie")}",
        };

        var v = SystemInformation.VirtualScreen;
        lines.Add($"Monitore ({Screen.AllScreens.Length}), virtueller Desktop {v.Width}x{v.Height} @ ({v.X},{v.Y}):");
        int n = 1;
        foreach (var scr in Screen.AllScreens)
            lines.Add($"  {n++}: {scr.DeviceName}  {scr.Bounds.Width}x{scr.Bounds.Height} @ ({scr.Bounds.X},{scr.Bounds.Y})" +
                      $"{(scr.Primary ? "  primär" : "")}  {scr.BitsPerPixel} bpp");
        return lines;
    }

    private static string Yn(bool b) => b ? "ja" : "nein";

    private static string DpiMode()
    {
        try
        {
            var ctx = GetThreadDpiAwarenessContext();
            if (AreDpiAwarenessContextsEqual(ctx, new IntPtr(-4))) return "PerMonitorV2";
            if (AreDpiAwarenessContextsEqual(ctx, new IntPtr(-3))) return "PerMonitor";
            if (AreDpiAwarenessContextsEqual(ctx, new IntPtr(-2))) return "System";
            if (AreDpiAwarenessContextsEqual(ctx, new IntPtr(-1)) || AreDpiAwarenessContextsEqual(ctx, new IntPtr(-5)))
                return "UNAWARE (Windows skaliert die Aufnahme -> unscharf!)";
            return "unbekannt";
        }
        catch (Exception ex)
        {
            return "nicht ermittelbar (" + ex.GetType().Name + ")";
        }
    }
}
