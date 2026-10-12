using System.Reflection;
using SchleimigeScreenshots.Core.Updates;

namespace SchleimigeScreenshots;

internal static class AppVersion
{
    /// <summary>Vollständige Versionsangabe inkl. Commit, z. B. "1.1.0+433cd26…" (Anzeige und Debug-Bericht).</summary>
    public static string Informational { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    /// <summary>Für den Versionsvergleich ohne Build-Metadaten; unbekannt = 0.0.0 (dann gilt jedes Release als neuer).</summary>
    public static SemVer Current { get; } = SemVer.TryParse(Informational, out var v) ? v : new SemVer(0, 0, 0);
}
