using System.Drawing.Imaging;

namespace SchleimigeScreenshots;

internal static class ScreenCapture
{
    /// <summary>Fotografiert den gesamten virtuellen Desktop (alle Monitore) als BGRA-Bytes.</summary>
    public static (byte[] Pixels, int Width, int Height) CaptureVirtualScreen()
    {
        var bounds = SystemInformation.VirtualScreen;
        int w = bounds.Width, h = bounds.Height;
        if (w <= 0 || h <= 0) throw new InvalidOperationException("Kein Bildschirm gefunden.");

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            // CaptureBlt: erfasst auch "layered"/transparente Fenster.
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size,
                CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
        }

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var pixels = new byte[w * h * 4];
            int rowBytes = w * 4;
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
            return (pixels, w, h);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
