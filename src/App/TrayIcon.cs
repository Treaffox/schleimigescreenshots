using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SchleimigeScreenshots;

/// <summary>Zeichnet das Tray-Symbol zur Laufzeit (kein Binär-Asset nötig).</summary>
internal static class TrayIcon
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create()
    {
        const int size = 64;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(30, 41, 59));
            g.FillRectangle(bg, 0, 0, size, size);

            void Finder(int x, int y)
            {
                g.FillRectangle(Brushes.White, x, y, 22, 22);
                g.FillRectangle(bg, x + 4, y + 4, 14, 14);
                g.FillRectangle(Brushes.White, x + 8, y + 8, 6, 6);
            }
            Finder(5, 5); Finder(37, 5); Finder(5, 37);

            foreach (var (x, y) in new[] { (37, 37), (49, 37), (43, 43), (37, 49), (49, 49), (55, 43) })
                g.FillRectangle(Brushes.White, x, y, 6, 6);
        }

        var hIcon = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
        finally { DestroyIcon(hIcon); }
    }
}
