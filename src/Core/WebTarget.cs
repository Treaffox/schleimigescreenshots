using System.Text.RegularExpressions;

namespace SchleimigeScreenshots.Core;

/// <summary>Entscheidet, was im Browser geöffnet wird. Es werden ausschließlich http(s)-Adressen geöffnet.</summary>
public static partial class WebTarget
{
    public const string DefaultSearchTemplate = "https://www.google.com/search?q={0}";
    private const int MaxSearchLength = 500;

    [GeneratedRegex(@"^www\.[^\s/]+\.[a-z]{2,}(?:[/?#]\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WwwPattern();

    /// <summary>Liefert die Adresse, wenn der Text eine Webadresse ist (http/https oder "www.…"), sonst null.</summary>
    public static Uri? TryGetWebUri(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.Any(char.IsWhiteSpace) || t.Any(char.IsControl)) return null;

        if (WwwPattern().IsMatch(t)) t = "https://" + t;

        return Uri.TryCreate(t, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(uri.Host)
            ? uri
            : null;
    }

    /// <summary>Baut eine Websuche-Adresse für beliebigen Text (z. B. EAN-Nummern).</summary>
    public static Uri BuildSearchUri(string? template, string text)
    {
        var q = text.Trim();
        if (q.Length > MaxSearchLength) q = q[..MaxSearchLength];

        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{0}")) template = DefaultSearchTemplate;
        var uri = template.Replace("{0}", Uri.EscapeDataString(q));

        if (Uri.TryCreate(uri, UriKind.Absolute, out var result)
            && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps))
            return result;

        return new Uri(DefaultSearchTemplate.Replace("{0}", Uri.EscapeDataString(q)));
    }
}
