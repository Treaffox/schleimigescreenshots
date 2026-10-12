using SchleimigeScreenshots.Core;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace SchleimigeScreenshots.Tests;

public class ScannerTests
{
    private static byte[] NewCanvas(int w, int h, byte value = 255)
    {
        var px = new byte[w * h * 4];
        Array.Fill(px, value);
        return px;
    }

    private static void Draw(byte[] canvas, int cw, string text, BarcodeFormat format, int x0, int y0, int scale, int w, int h, bool invert = false)
    {
        var matrix = new MultiFormatWriter().encode(text, format, w, h, new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 4 });
        for (int y = 0; y < matrix.Height * scale; y++)
        for (int x = 0; x < matrix.Width * scale; x++)
        {
            bool black = matrix[x / scale, y / scale] ^ invert;
            int i = ((y0 + y) * cw + x0 + x) * 4;
            canvas[i] = canvas[i + 1] = canvas[i + 2] = black ? (byte)0 : (byte)255;
            canvas[i + 3] = 255;
        }
    }

    [Fact]
    public void Finds_small_qr_on_4k_screen()
    {
        const int w = 3840, h = 2160;
        var img = NewCanvas(w, h);
        Draw(img, w, "https://example.com/a?b=1", BarcodeFormat.QR_CODE, 2900, 1500, 3, 25, 25);
        var r = CodeScanner.Scan(img, w, h);
        Assert.Contains(r, x => x.Text == "https://example.com/a?b=1");
    }

    [Fact]
    public void Finds_multiple_codes_of_different_types()
    {
        const int w = 3840, h = 1080;
        var img = NewCanvas(w, h);
        Draw(img, w, "hello one", BarcodeFormat.QR_CODE, 100, 100, 4, 29, 29);
        Draw(img, w, "second code", BarcodeFormat.QR_CODE, 3000, 600, 4, 29, 29);
        Draw(img, w, "4006381333931", BarcodeFormat.EAN_13, 1500, 300, 3, 95, 1);
        var r = CodeScanner.Scan(img, w, h);
        Assert.Contains(r, x => x.Text == "hello one");
        Assert.Contains(r, x => x.Text == "second code");
        Assert.Contains(r, x => x.Text == "4006381333931");
        Assert.Equal(r.Count, r.Select(x => (x.Format, x.Text)).Distinct().Count());
    }

    [Fact]
    public void Finds_inverted_dark_mode_code()
    {
        const int w = 800, h = 600;
        var img = NewCanvas(w, h, 0);
        Draw(img, w, "dark mode", BarcodeFormat.QR_CODE, 200, 100, 6, 29, 29, invert: true);
        Assert.Contains(CodeScanner.Scan(img, w, h), x => x.Text == "dark mode");
    }

    [Fact]
    public void Blank_screen_yields_nothing()
    {
        Assert.Empty(CodeScanner.Scan(NewCanvas(1920, 1080), 1920, 1080));
    }

    [Fact]
    public void Rejects_bad_input()
    {
        Assert.Throws<ArgumentException>(() => CodeScanner.Scan(new byte[10], 100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => CodeScanner.Scan(new byte[10], 0, 100));
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => CodeScanner.Scan(NewCanvas(1920, 1080), 1920, 1080, cts.Token));
    }
}
