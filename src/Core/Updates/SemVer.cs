using System.Globalization;

namespace SchleimigeScreenshots.Core.Updates;

/// <summary>Semantische Version (MAJOR.MINOR.PATCH[-vorabversion][+build]); Build-Metadaten zählen beim Vergleich nicht.</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string PreRelease = "") : IComparable<SemVer>
{
    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>Akzeptiert z. B. "1.2.3", "v1.2.3", "1.2", "1.2.3-beta.1", "1.2.3+abc123".</summary>
    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length > 0 && (t[0] == 'v' || t[0] == 'V')) t = t[1..];

        var plus = t.IndexOf('+');
        if (plus >= 0) t = t[..plus];

        var pre = "";
        var dash = t.IndexOf('-');
        if (dash >= 0)
        {
            pre = t[(dash + 1)..];
            t = t[..dash];
            if (pre.Length == 0) return false;
        }

        var parts = t.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }

        version = new SemVer(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public static SemVer Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"Keine gültige Version: '{text}'");

    public int CompareTo(SemVer other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // Eine Vorabversion ist kleiner als die Release-Version gleicher Nummer.
        if (!IsPreRelease && !other.IsPreRelease) return 0;
        if (!IsPreRelease) return 1;
        if (!other.IsPreRelease) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string a, string b)
    {
        var x = a.Split('.');
        var y = b.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var xNum = int.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out var xi);
            var yNum = int.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out var yi);
            int c;
            if (xNum && yNum) c = xi.CompareTo(yi);
            else if (xNum) c = -1;          // numerische Bezeichner sind kleiner als alphanumerische
            else if (yNum) c = 1;
            else c = string.CompareOrdinal(x[i], y[i]);
            if (c != 0) return c;
        }
        return x.Length.CompareTo(y.Length);
    }

    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (IsPreRelease ? "-" + PreRelease : "");
}
