using ZXing;
using ZXing.Common;
using ZXing.Multi;

namespace SchleimigeScreenshots.Core;

/// <summary>Ein erkannter Code samt ungefährer Position (Bildmitte des Codes, in Pixeln).</summary>
public sealed record ScanResult(string Text, string Format, double X, double Y);

/// <summary>
/// Erkennt QR-Codes, Barcodes usw. in einem BGRA-Bild. Das Bild wird als Ganzes sowie in
/// überlappenden Kacheln durchsucht, weil kleine Codes auf großen (Mehrmonitor-)Screenshots
/// sonst leicht übersehen werden.
/// </summary>
public static class CodeScanner
{
    private readonly record struct Region(int X, int Y, int Width, int Height);

    public static IReadOnlyList<ScanResult> Scan(byte[] bgra, int width, int height, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Ungültige Bildgröße.");
        if (bgra.Length < (long)width * height * 4)
            throw new ArgumentException("Pixeldaten sind kleiner als Breite*Höhe*4.", nameof(bgra));

        var source = new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGR32);
        var regions = BuildRegions(width, height);

        var found = ScanRegions(source, regions, inverted: false, ct);
        if (found.Count == 0)
            found = ScanRegions(source, regions, inverted: true, ct); // helle Codes auf dunklem Grund (Dark Mode)

        return found
            .OrderBy(r => Math.Round(r.Y / 40.0)) // grob zeilenweise, dann von links nach rechts
            .ThenBy(r => r.X)
            .ToList();
    }

    private static List<Region> BuildRegions(int w, int h)
    {
        var list = new List<Region> { new(0, 0, w, h) };
        if (w < 500 && h < 500) return list;

        foreach (var n in new[] { 2, 3 })
        {
            int tw = w / n, th = h / n;
            int ox = tw / 4, oy = th / 4; // 25 % Überlappung, damit Codes an Kachelkanten nicht zerschnitten werden
            for (int iy = 0; iy < n; iy++)
            for (int ix = 0; ix < n; ix++)
            {
                int x = Math.Max(0, ix * tw - ox);
                int y = Math.Max(0, iy * th - oy);
                int right = Math.Min(w, (ix + 1) * tw + ox);
                int bottom = Math.Min(h, (iy + 1) * th + oy);
                list.Add(new Region(x, y, right - x, bottom - y));
            }
        }
        return list;
    }

    private static List<ScanResult> ScanRegions(LuminanceSource source, List<Region> regions, bool inverted, CancellationToken ct)
    {
        var results = new List<ScanResult>();
        var gate = new object();
        var options = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        };

        Parallel.ForEach(regions, options, region =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                LuminanceSource src = region.Width == source.Width && region.Height == source.Height
                    ? source
                    : source.crop(region.X, region.Y, region.Width, region.Height);
                if (inverted) src = new InvertedLuminanceSource(src);

                var bitmap = new BinaryBitmap(new HybridBinarizer(src));
                // ZXing-Reader sind nicht threadsicher -> pro Region eigene Instanz.
                var reader = new GenericMultipleBarcodeReader(new MultiFormatReader());
                var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true };
                var decoded = reader.decodeMultiple(bitmap, hints);
                if (decoded is null) return;

                lock (gate)
                {
                    foreach (var r in decoded)
                    {
                        if (string.IsNullOrEmpty(r.Text)) continue;
                        var (cx, cy) = Center(r);
                        results.Add(new ScanResult(r.Text, r.BarcodeFormat.ToString(), cx + region.X, cy + region.Y));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Eine fehlerhafte Region darf nie den ganzen Scan kippen.
            }
        });

        // Überlappende Kacheln liefern denselben Code mehrfach -> nach Format+Text entduplizieren.
        return results
            .GroupBy(r => (r.Format, r.Text))
            .Select(g => g.First())
            .ToList();
    }

    private static (double X, double Y) Center(Result r)
    {
        var pts = r.ResultPoints;
        if (pts is null || pts.Length == 0) return (0, 0);
        return (pts.Average(p => (double)p.X), pts.Average(p => (double)p.Y));
    }
}
