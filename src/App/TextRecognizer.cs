using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SchleimigeScreenshots;

/// <summary>Texterkennung (OCR) über die in Windows eingebaute Engine; läuft lokal und braucht keine Zusatzpakete.</summary>
internal static class TextRecognizer
{
    private const int MinSide = 40; // kleinere Bilder liefern keinen brauchbaren Text

    /// <summary>Liefert den erkannten Text oder null, wenn Windows keine OCR-Sprache installiert hat.</summary>
    public static async Task<string?> RecognizeAsync(byte[] bgra, int width, int height, CancellationToken ct = default)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        if (engine is null) return null;

        // Die Engine verträgt nur begrenzte Bildgrößen (MaxImageDimension); größere Bilder werden kachelweise gelesen.
        int max = (int)OcrEngine.MaxImageDimension;
        var lines = new List<string>();
        for (int y = 0; y < height; y += max - 64)
        for (int x = 0; x < width; x += max - 64)
        {
            ct.ThrowIfCancellationRequested();
            int w = Math.Min(max, width - x), h = Math.Min(max, height - y);
            if (w < MinSide || h < MinSide) continue;
            var tile = x == 0 && y == 0 && w == width && h == height
                ? bgra
                : Core.ImageOps.Crop(bgra, width, height, x, y, w, h);

            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(tile.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
            var result = await engine.RecognizeAsync(bitmap);
            foreach (var line in result.Lines) lines.Add(line.Text);
        }
        return string.Join("\n", lines);
    }
}
