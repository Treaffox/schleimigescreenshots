using System.Diagnostics;
using ZXing;
using ZXing.Common;
using ZXing.Multi;

namespace SchleimigeScreenshots.Core;

/// <summary>Ein erkannter Code samt ungefährer Position (Bildmitte des Codes, in Pixeln).</summary>
public sealed record ScanResult(string Text, string Format, double X, double Y);

/// <summary>Diagnose einer durchsuchten Bildregion (für den Debug-Bericht).</summary>
public sealed record RegionDiagnostics(int X, int Y, int Width, int Height, int Hits, double Milliseconds, string? Error);

/// <summary>Diagnose eines Suchdurchlaufs über alle Regionen.</summary>
public sealed record PassDiagnostics(string Name, double Milliseconds, IReadOnlyList<RegionDiagnostics> Regions);

/// <summary>Ergebnis eines Scans samt Diagnosedaten.</summary>
public sealed record ScanOutcome(IReadOnlyList<ScanResult> Results, IReadOnlyList<PassDiagnostics> Passes, double Milliseconds);

/// <summary>
/// Erkennt QR-Codes, Barcodes usw. in einem BGRA-Bild. Das Bild wird als Ganzes sowie in
/// überlappenden Kacheln durchsucht, weil kleine Codes auf großen (Mehrmonitor-)Screenshots
/// sonst leicht übersehen werden.
/// </summary>
public static class CodeScanner
{
    private readonly record struct Region(int X, int Y, int Width, int Height);

    public static IReadOnlyList<ScanResult> Scan(byte[] bgra, int width, int height, CancellationToken ct = default) =>
        ScanDetailed(bgra, width, height, ct).Results;

    /// <summary>Wie <see cref="Scan"/>, liefert aber zusätzlich Diagnosedaten (Regionen, Zeiten, Fehler).</summary>
    public static ScanOutcome ScanDetailed(byte[] bgra, int width, int height, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Ungültige Bildgröße.");
        if (bgra.Length < (long)width * height * 4)
            throw new ArgumentException("Pixeldaten sind kleiner als Breite*Höhe*4.", nameof(bgra));

        var total = Stopwatch.StartNew();
        var source = new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGR32);
        var regions = BuildRegions(width, height);
        var passes = new List<PassDiagnostics>();

        var found = RunPass("normal", source, regions, inverted: false, ct, passes);
        if (found.Count == 0)
            found = RunPass("invertiert", source, regions, inverted: true, ct, passes); // helle Codes auf dunklem Grund (Dark Mode)

        var ordered = found
            .OrderBy(r => Math.Round(r.Y / 40.0)) // grob zeilenweise, dann von links nach rechts
            .ThenBy(r => r.X)
            .ToList();
        return new ScanOutcome(ordered, passes, total.Elapsed.TotalMilliseconds);
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

    private static List<ScanResult> RunPass(string name, LuminanceSource source, List<Region> regions, bool inverted,
        CancellationToken ct, List<PassDiagnostics> passes)
    {
        var passWatch = Stopwatch.StartNew();
        var results = new List<ScanResult>();
        var diagnostics = new RegionDiagnostics[regions.Count]; // jeder Index wird nur von einem Thread geschrieben
        var gate = new object();
        var options = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        };

        Parallel.For(0, regions.Count, options, index =>
        {
            ct.ThrowIfCancellationRequested();
            var region = regions[index];
            var watch = Stopwatch.StartNew();
            int hits = 0;
            string? error = null;
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

                if (decoded is not null)
                {
                    lock (gate)
                    {
                        foreach (var r in decoded)
                        {
                            if (string.IsNullOrEmpty(r.Text)) continue;
                            var (cx, cy) = Center(r);
                            results.Add(new ScanResult(r.Text, r.BarcodeFormat.ToString(), cx + region.X, cy + region.Y));
                            hits++;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Eine fehlerhafte Region darf nie den ganzen Scan kippen – der Fehler landet im Debug-Bericht.
                error = ex.ToString();
            }
            diagnostics[index] = new RegionDiagnostics(region.X, region.Y, region.Width, region.Height, hits,
                watch.Elapsed.TotalMilliseconds, error);
        });

        passes.Add(new PassDiagnostics(name, passWatch.Elapsed.TotalMilliseconds, diagnostics));

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
