using System.Diagnostics;
using ZXing;

namespace SchleimigeScreenshots.Core;

/// <summary>
/// Selbsttest der Erkennung: rendert einen QR-Code in ein künstliches Bild (Alpha = 0 wie bei GDI-Aufnahmen)
/// und scannt es mit derselben Pipeline wie echte Screenshots. Trennt "Erkennung kaputt" von "Aufnahme kaputt".
/// </summary>
public static class SelfTest
{
    public const string Payload = "https://example.com/selbsttest";

    public static (bool Ok, string Message) Run()
    {
        try
        {
            var watch = Stopwatch.StartNew();
            const int w = 1600, h = 900, scale = 4;
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = 255; px[i + 3] = 0; }

            var matrix = new MultiFormatWriter().encode(Payload, BarcodeFormat.QR_CODE, 1, 1,
                new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 4 });
            for (int y = 0; y < matrix.Height * scale; y++)
            for (int x = 0; x < matrix.Width * scale; x++)
            {
                int i = ((300 + y) * w + 700 + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = matrix[x / scale, y / scale] ? (byte)0 : (byte)255;
            }

            var outcome = CodeScanner.ScanDetailed(px, w, h);
            var ok = outcome.Results.Any(r => r.Text == Payload);
            var errors = outcome.Passes.SelectMany(p => p.Regions).Count(r => r.Error is not null);
            return ok
                ? (true, $"Selbsttest OK – Testcode erkannt ({watch.ElapsedMilliseconds} ms)")
                : (false, $"Selbsttest FEHLGESCHLAGEN – Testcode nicht erkannt ({outcome.Results.Count} Treffer, {errors} Regionsfehler)");
        }
        catch (Exception ex)
        {
            return (false, "Selbsttest FEHLGESCHLAGEN – Ausnahme:\n" + ex);
        }
    }
}
