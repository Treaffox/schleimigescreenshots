using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SchleimigeScreenshots;

internal sealed record CaptureResult(
    byte[] Pixels, int Width, int Height, Rectangle Bounds, string Method, double Milliseconds, IReadOnlyList<string> Notes);

internal static class ScreenCapture
{
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000; // auch "layered"/transparente Fenster erfassen

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    /// <summary>Fotografiert den gesamten virtuellen Desktop (alle Monitore) als BGRA-Bytes.</summary>
    public static CaptureResult CaptureVirtualScreen()
    {
        var watch = Stopwatch.StartNew();
        var notes = new List<string>();
        var bounds = SystemInformation.VirtualScreen;
        int w = bounds.Width, h = bounds.Height;
        if (w <= 0 || h <= 0) throw new InvalidOperationException("Kein Bildschirm gefunden.");

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        string method;
        using (var g = Graphics.FromImage(bmp))
            method = Blit(g, bounds, notes);

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var pixels = new byte[w * h * 4];
            int rowBytes = w * 4;
            for (int y = 0; y < h; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
            return new CaptureResult(pixels, w, h, bounds, method, watch.Elapsed.TotalMilliseconds, notes);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Kopiert den Bildschirm per BitBlt. Bewusst nicht Graphics.CopyFromScreen: dort ist
    /// SourceCopy|CaptureBlt kein erlaubter Wert und wirft InvalidEnumArgumentException.
    /// </summary>
    private static string Blit(Graphics g, Rectangle b, List<string> notes)
    {
        var hdcSrc = GetDC(IntPtr.Zero);
        if (hdcSrc == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC für den Bildschirm fehlgeschlagen");
        try
        {
            var hdcDest = g.GetHdc();
            try
            {
                if (BitBlt(hdcDest, 0, 0, b.Width, b.Height, hdcSrc, b.Left, b.Top, SRCCOPY | CAPTUREBLT))
                    return "BitBlt SRCCOPY|CAPTUREBLT";

                notes.Add("BitBlt mit CAPTUREBLT fehlgeschlagen: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
                if (BitBlt(hdcDest, 0, 0, b.Width, b.Height, hdcSrc, b.Left, b.Top, SRCCOPY))
                    return "BitBlt SRCCOPY (Fallback ohne CAPTUREBLT)";

                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt fehlgeschlagen");
            }
            finally
            {
                g.ReleaseHdc(hdcDest);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }

    /// <summary>Speichert BGRA-Bytes als PNG (für den Debug-Bericht).</summary>
    public static void SavePng(byte[] pixels, int width, int height, string path)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        // 24 Bit ohne Alpha: das unbenutzte vierte Byte der Aufnahme darf nie Transparenz bedeuten.
        using var opaque = bmp.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format24bppRgb);
        opaque.Save(path, ImageFormat.Png);
    }
}
