using SchleimigeScreenshots.Core;
using ZXing;
using ZXing.Common;

namespace SchleimigeScreenshots.Tests;

/// <summary>Bildschirm-ähnliche Bilder: geglättete, krumm skalierte Codes auf unruhigem Hintergrund.</summary>
public class RealisticScreenTests
{
    internal static BitMatrix Encode(string text, BarcodeFormat f = BarcodeFormat.QR_CODE) =>
        new MultiFormatWriter().encode(text, f, 1, 1, new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 4 });

    /// <summary>Webseiten-artiger Hintergrund: Verlauf, Textzeilen-Blöcke, leichtes Rauschen.</summary>
    internal static byte[] Background(int w, int h, bool dark, int seed)
    {
        var rnd = new Random(seed);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double g = dark ? 24 + 20.0 * x / w : 235 + 20.0 * y / h;
            bool textLine = (y / 14) % 3 == 0 && ((x / 7 + y / 14 * 5) % 9) < 6 && x > 80 && x < w - 80;
            double v = textLine ? (dark ? 190 : 60) : g;
            v += rnd.Next(-3, 4);
            var b = (byte)Math.Clamp(v, 0, 255);
            int i = (y * w + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = b;
            px[i + 3] = 0;
        }
        return px;
    }

    /// <summary>Zeichnet den Code mit Flächenmittelung (wie ein skalierter Browser/Viewer), Modulgröße in Pixeln beliebig.</summary>
    internal static void DrawSmooth(byte[] px, int w, BitMatrix m, double moduleSize, int x0, int y0, bool invert)
    {
        int size = (int)Math.Ceiling(m.Width * moduleSize);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double sx0 = x / moduleSize, sx1 = (x + 1) / moduleSize, sy0 = y / moduleSize, sy1 = (y + 1) / moduleSize;
            double sum = 0, area = 0;
            for (int my = (int)sy0; my <= Math.Min(m.Height - 1, (int)sy1); my++)
            for (int mx = (int)sx0; mx <= Math.Min(m.Width - 1, (int)sx1); mx++)
            {
                double ox = Math.Min(sx1, mx + 1) - Math.Max(sx0, mx);
                double oy = Math.Min(sy1, my + 1) - Math.Max(sy0, my);
                if (ox <= 0 || oy <= 0) continue;
                bool black = m[mx, my] ^ invert;
                sum += (black ? 0 : 255) * ox * oy;
                area += ox * oy;
            }
            if (area <= 0) continue;
            int i = ((y0 + y) * w + x0 + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = (byte)(sum / area);
        }
    }

    [Theory]
    [InlineData(2.0, false)]
    [InlineData(2.37, false)]
    [InlineData(3.1, false)]
    [InlineData(4.5, false)]
    [InlineData(2.37, true)]
    [InlineData(3.1, true)]
    public void Finds_smoothly_scaled_code_on_busy_background(double moduleSize, bool dark)
    {
        const int w = 1920, h = 1080;
        var px = Background(w, h, dark, seed: 7);
        DrawSmooth(px, w, Encode("https://www.example.org/pfad?token=abc123XYZ"), moduleSize, 1100, 400, invert: dark);
        var r = CodeScanner.Scan(px, w, h);
        Assert.Contains(r, x => x.Text == "https://www.example.org/pfad?token=abc123XYZ");
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(3.3)]
    public void Finds_code_on_ultrawide_multi_monitor_desktop(double moduleSize)
    {
        const int w = 5760, h = 1080; // 3 x Full HD nebeneinander
        var px = Background(w, h, dark: false, seed: 3);
        DrawSmooth(px, w, Encode("zweiter-monitor-code"), moduleSize, 4300, 300, invert: false);
        Assert.Contains(CodeScanner.Scan(px, w, h), x => x.Text == "zweiter-monitor-code");
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(2.3)]
    [InlineData(3.0)]
    public void Finds_ean13_at_fractional_scaling(double moduleSize)
    {
        const int w = 1920, h = 1080;
        var px = Background(w, h, false, 11);
        var m = new MultiFormatWriter().encode("4006381333931", BarcodeFormat.EAN_13, 95, 1);
        int barW = (int)(m.Width * moduleSize), quiet = (int)(4 * moduleSize); // m enthält die Ruhezonen bereits
        // zusätzlich weißer Rand, darin die Balken pixelgerundet
        for (int y = 270; y < 450; y++)
        for (int x = 700 - quiet; x < 700 + barW + quiet; x++)
        {
            int i = (y * w + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 255;
        }
        for (int y = 300; y < 420; y++)
        for (int x = 0; x < barW; x++)
        {
            int i = (y * w + 700 + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = m[(int)(x / moduleSize), 0] ? (byte)0 : (byte)255;
        }
        Assert.Contains(CodeScanner.Scan(px, w, h), x => x.Text == "4006381333931");
    }
}
