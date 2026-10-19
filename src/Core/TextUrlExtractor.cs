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
            var candidate = TrimTrailing(m.Value);
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

    private static string TrimTrailing(string s) => s.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '>');
}
