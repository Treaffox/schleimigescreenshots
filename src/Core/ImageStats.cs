namespace SchleimigeScreenshots.Core;

/// <summary>
/// Grobe Kennzahlen eines BGRA-Bildes (Stichprobe). Zeigt z. B., ob eine Aufnahme leer/einfarbig ist –
/// dann liegt das Problem bei der Bildschirmaufnahme und nicht bei der Codeerkennung.
/// </summary>
public sealed record ImageStats(
    int MinLuma, int MaxLuma, double MeanLuma,
    double ZeroAlphaShare, double NearBlackShare, double NearWhiteShare,
    int DistinctColors, bool DistinctColorsCapped)
{
    private const int ColorCap = 5000;

    /// <summary>Praktisch einfarbig – eine echte Desktop-Aufnahme hat nie so wenig Kontrast.</summary>
    public bool LooksBlank => MaxLuma - MinLuma < 4;

    public static ImageStats Compute(byte[] bgra, int width, int height, int step = 4)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4)
            throw new ArgumentException("Ungültige Bilddaten.");

        step = Math.Max(1, step);
        int min = 255, max = 0, n = 0, zeroAlpha = 0, black = 0, white = 0;
        long sum = 0;
        var colors = new HashSet<int>();
        bool capped = false;

        for (int y = 0; y < height; y += step)
        for (int x = 0; x < width; x += step)
        {
            int i = (y * width + x) * 4;
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            int luma = (b * 29 + g * 150 + r * 77) >> 8;
            if (luma < min) min = luma;
            if (luma > max) max = luma;
            sum += luma;
            n++;
            if (bgra[i + 3] == 0) zeroAlpha++;
            if (luma < 16) black++;
            if (luma > 239) white++;
            if (!capped)
            {
                colors.Add((r << 16) | (g << 8) | b);
                capped = colors.Count >= ColorCap;
            }
        }

        return new ImageStats(min, max, (double)sum / n, (double)zeroAlpha / n, (double)black / n, (double)white / n,
            colors.Count, capped);
    }
}
