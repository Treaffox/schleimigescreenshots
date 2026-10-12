using SchleimigeScreenshots.Core;
using ZXing;

namespace SchleimigeScreenshots.Tests;

public class CaptureFormatTests
{
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)255)]
    public void Alpha_byte_of_screen_capture_must_not_matter(byte alpha)
    {
        const int w = 1280, h = 720;
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = 255; px[i + 3] = alpha; }
        var m = new MultiFormatWriter().encode("alpha-probe", BarcodeFormat.QR_CODE, 25, 25,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 4 });
        for (int y = 0; y < m.Height * 6; y++)
        for (int x = 0; x < m.Width * 6; x++)
        {
            int i = ((200 + y) * w + 300 + x) * 4;
            byte v = m[x / 6, y / 6] ? (byte)0 : (byte)255;
            px[i] = px[i + 1] = px[i + 2] = v; px[i + 3] = alpha;
        }
        Assert.Contains(CodeScanner.Scan(px, w, h), r => r.Text == "alpha-probe");
    }
}
