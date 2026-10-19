using System.Text.RegularExpressions;

namespace SchleimigeScreenshots.Core;

/// <summary>
/// Zieht Webadressen aus erkanntem Fließtext (OCR). Normaler Text wird ignoriert, es zählen nur
/// http(s)-Adressen und "www.…"-Adressen.
/// </summary>
public static partial class TextUrlExtractor
{
    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>""'«»„“”]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    /// <summary>Liefert alle Webadressen im Text (ohne Duplikate, in Lesereihenfolge).</summary>
    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var found = new List<string>();
        foreach (Match m in UrlPattern().Matches(Normalize(text)))
        {
            var candidate = FixVersionSegments(TrimTrailing(m.Value));
            if (WebTarget.TryGetWebUri(candidate) is null) continue;
            if (!found.Contains(candidate, StringComparer.OrdinalIgnoreCase)) found.Add(candidate);
        }
        return found;
    }

    /// <summary>OCR trennt lange Adressen oft am Zeilenumbruch: "https://x.de/a-\nb" bzw. "https://x.de/a\n/b" wieder zusammenfügen.</summary>
    private static string Normalize(string text) =>
        UrlBreakPattern().Replace(text.Replace("\r", ""), "$1");

    [GeneratedRegex(@"((?:https?://|www\.)\S*[/\-_=&?])\n(?=\S)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlBreakPattern();

    /// <summary>OCR liest in Versionsnummern gern "O" statt "0" und "l" statt "1" (v1.2.O); in reinen Versions-/Zahlensegmenten des Pfads korrigieren.</summary>
    private static string FixVersionSegments(string url)
    {
        int start = url.IndexOf("://", StringComparison.Ordinal);
        int slash = url.IndexOf('/', start < 0 ? 0 : start + 3);
        if (slash < 0) return url;
        return url[..slash] + VersionSegmentPattern().Replace(url[slash..], mm =>
            mm.Value.Any(char.IsAsciiDigit) ? mm.Value.Replace('O', '0').Replace('o', '0').Replace('l', '1').Replace('I', '1') : mm.Value);
    }

    [GeneratedRegex(@"(?<=/)v?[\dOolI]+(?:\.[\dOolI]+)+(?=$|[/?#])", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSegmentPattern();

    private static string TrimTrailing(string s) => s.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '>');
}
