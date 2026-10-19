using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using RapidOcrNet;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SchleimigeScreenshots;

/// <summary>
/// Texterkennung (OCR). Hauptengine ist RapidOCR (PP-OCRv5, ONNX, lokal, Modelle in der exe eingebettet);
/// scheitert sie, springt die in Windows eingebaute Engine ein.
/// </summary>
internal static class TextRecognizer
{
    private const int MinSide = 40; // kleinere Bilder liefern keinen brauchbaren Text
    private static readonly string[] ModelFiles =
    [
        "ch_PP-OCRv5_mobile_det.onnx",
        "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
        "latin_PP-OCRv5_rec_mobile_infer.onnx",
        "ppocrv5_latin_dict.txt",
    ];

    private static readonly object EngineLock = new();
    private static RapidOcr? _rapid;

    /// <summary>Name der Engine, die beim letzten Aufruf Text geliefert hat (für den Debug-Bericht).</summary>
    public static string LastEngine { get; private set; } = "";
    /// <summary>Fehler der Hauptengine, falls auf den Fallback ausgewichen wurde.</summary>
    public static string? LastRapidError { get; private set; }

    /// <summary>Liefert den erkannten Text oder null, wenn keine Engine verfügbar ist.</summary>
    public static async Task<string?> RecognizeAsync(byte[] bgra, int width, int height, CancellationToken ct = default)
    {
        LastRapidError = null;
        try
        {
            var text = await Task.Run(() => RecognizeRapid(bgra, width, height, ct), ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                LastEngine = "RapidOCR (PP-OCRv5)";
                return text;
            }
            LastRapidError = "RapidOCR fand keinen Text";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LastRapidError = ex.GetType().Name + ": " + ex.Message;
        }
        LastEngine = "Windows-OCR (Fallback)";
        return await RecognizeWindowsAsync(bgra, width, height, ct);
    }

    private static string RecognizeRapid(byte[] bgra, int width, int height, CancellationToken ct)
    {
        var ocr = GetRapid();
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bitmap.GetPixels(), Math.Min(bgra.Length, bitmap.ByteCount));
        // Bildschirmaufnahmen haben je nach Treiber Alpha=0; RapidOCR soll nur die Farben sehen.
        unsafe
        {
            var px = (byte*)bitmap.GetPixels();
            for (long i = 3, n = bitmap.ByteCount; i < n; i += 4) px[i] = 255;
        }
        if (Environment.GetEnvironmentVariable("SCHLEIMIGE_OCR_DUMP") is { Length: > 0 } dump)
        {
            using var fs = File.Create(dump);
            bitmap.Encode(fs, SKEncodedImageFormat.Png, 100);
        }
        // Standard deckelt die lange Seite auf 1024 px – bei Bildschirmtext ginge dabei kleine Schrift verloren.
        var options = RapidOcrOptions.Default with { ImgResize = Math.Max(width, height) };
        lock (EngineLock)
        {
            ct.ThrowIfCancellationRequested();
            var result = ocr.Detect(bitmap, options);
            return string.Join("\n", result.TextBlocks.Select(b => b.Text));
        }
    }

    private static RapidOcr GetRapid()
    {
        lock (EngineLock)
        {
            if (_rapid is not null) return _rapid;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchleimigeScreenshots", "models", "ppocrv5");
            Directory.CreateDirectory(dir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in ModelFiles)
            {
                var target = Path.Combine(dir, name);
                using var src = asm.GetManifestResourceStream("ocr/" + name) ?? throw new FileNotFoundException("Eingebettetes OCR-Modell fehlt: " + name);
                if (File.Exists(target) && new FileInfo(target).Length == src.Length) continue;
                var tmp = target + ".tmp";
                using (var dst = File.Create(tmp)) src.CopyTo(dst);
                File.Move(tmp, target, overwrite: true);
            }
            var ocr = new RapidOcr();
            ocr.InitModels(
                Path.Combine(dir, ModelFiles[0]), Path.Combine(dir, ModelFiles[1]),
                Path.Combine(dir, ModelFiles[2]), Path.Combine(dir, ModelFiles[3]), 2);
            return _rapid = ocr;
        }
    }

    private static async Task<string?> RecognizeWindowsAsync(byte[] bgra, int width, int height, CancellationToken ct)
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
