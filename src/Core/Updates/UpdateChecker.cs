using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchleimigeScreenshots.Core.Updates;

/// <summary>Eine herunterladbare Datei eines Releases. <see cref="Sha256"/> ist null, wenn kein Prüfwert bekannt ist.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>Ein neueres Release, das installiert werden kann.</summary>
public sealed record UpdateInfo(
    string Tag, SemVer Version, bool IsPreRelease, string PageUrl, string Notes,
    ReleaseAsset? Setup, ReleaseAsset? Portable);

public sealed class UpdateCheckException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Fragt die GitHub-Releases ab (Liste, nicht /latest: das ignoriert Vorabversionen) und liefert das neueste Release,
/// das neuer als die laufende Version ist. Alle Netzzugriffe laufen über den übergebenen HttpClient (testbar).
/// </summary>
public sealed class UpdateChecker(HttpClient http, Uri feedUrl, string? requiredAssetUrlPrefix = null)
{
    public const string SetupNamePrefix = "SchleimigeScreenshots-Setup-";
    public const string PortableName = "SchleimigeScreenshots.exe";
    public const string ChecksumsName = "SHA256SUMS.txt";

    private static readonly Regex ChecksumLine = new(@"^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>.+?)\s*$", RegexOptions.Compiled);

    public async Task<UpdateInfo?> CheckAsync(SemVer current, bool includePreReleases, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        string json;
        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new UpdateCheckException($"GitHub hat die Anfrage abgelehnt (HTTP {(int)response.StatusCode}, vermutlich Ratenlimit). Bitte später erneut versuchen.");
            if (!response.IsSuccessStatusCode)
                throw new UpdateCheckException($"GitHub antwortete mit HTTP {(int)response.StatusCode}.");
            json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateCheckException("Keine Verbindung zu GitHub: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UpdateCheckException("Zeitüberschreitung bei der Verbindung zu GitHub.", ex);
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new UpdateCheckException("Die Antwort von GitHub war kein gültiges JSON.", ex); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new UpdateCheckException("Unerwartetes Antwortformat von GitHub.");

            JsonElement? best = null;
            SemVer bestVersion = current;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (GetBool(release, "draft")) continue;
                if (GetBool(release, "prerelease") && !includePreReleases) continue;
                if (!SemVer.TryParse(GetString(release, "tag_name"), out var version)) continue;
                if (version.IsPreRelease && !includePreReleases) continue;
                if (version > bestVersion)
                {
                    best = release;
                    bestVersion = version;
                }
            }

            if (best is not { } chosen) return null;
            return await BuildInfoAsync(chosen, bestVersion, ct).ConfigureAwait(false);
        }
    }

    private async Task<UpdateInfo> BuildInfoAsync(JsonElement release, SemVer version, CancellationToken ct)
    {
        var assets = new List<(string Name, string Url, long Size, string? Digest)>();
        if (release.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in list.EnumerateArray())
            {
                var name = GetString(a, "name");
                var url = GetString(a, "browser_download_url");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                if (requiredAssetUrlPrefix is not null && !url.StartsWith(requiredAssetUrlPrefix, StringComparison.Ordinal)) continue;

                var digest = GetString(a, "digest");
                if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    digest = digest["sha256:".Length..];
                else
                    digest = null;
                assets.Add((name, url, a.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0, digest));
            }
        }

        // Fehlende Prüfwerte aus SHA256SUMS.txt ergänzen
        Dictionary<string, string>? sums = null;
        var sumsFile = assets.FirstOrDefault(a => a.Name == ChecksumsName);
        if (sumsFile.Url is not null && assets.Any(a => a.Digest is null && a.Name != ChecksumsName))
            sums = await TryReadChecksumsAsync(sumsFile.Url, ct).ConfigureAwait(false);

        ReleaseAsset? Find(Func<string, bool> match)
        {
            foreach (var a in assets)
            {
                if (!match(a.Name)) continue;
                var hash = a.Digest ?? (sums is not null && sums.TryGetValue(a.Name, out var h) ? h : null);
                return new ReleaseAsset(a.Name, a.Url, a.Size, hash?.ToLowerInvariant());
            }
            return null;
        }

        var exactSetup = $"{SetupNamePrefix}{version.Major}.{version.Minor}.{version.Patch}.exe";
        var setup = Find(n => string.Equals(n, exactSetup, StringComparison.OrdinalIgnoreCase))
                    ?? Find(n => n.StartsWith(SetupNamePrefix, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        var portable = Find(n => string.Equals(n, PortableName, StringComparison.OrdinalIgnoreCase));

        return new UpdateInfo(
            Tag: GetString(release, "tag_name") ?? version.ToString(),
            Version: version,
            IsPreRelease: GetBool(release, "prerelease") || version.IsPreRelease,
            PageUrl: GetString(release, "html_url") ?? "",
            Notes: (GetString(release, "body") ?? "").Trim(),
            Setup: setup,
            Portable: portable);
    }

    private async Task<Dictionary<string, string>?> TryReadChecksumsAsync(string url, CancellationToken ct)
    {
        try
        {
            var text = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split('\n'))
            {
                var m = ChecksumLine.Match(line.TrimEnd('\r'));
                if (m.Success) result[m.Groups["name"].Value] = m.Groups["hash"].Value;
            }
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null; // ohne Prüfwert wird später nicht automatisch installiert
        }
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
