namespace SchleimigeScreenshots.Core;

/// <summary>Kleine Bildwerkzeuge für BGRA-Pixelpuffer (4 Bytes je Pixel, Zeile für Zeile, ohne Zeilenversatz).</summary>
public static class ImageOps
{
    /// <summary>Schneidet ein Rechteck aus; es muss vollständig im Bild liegen. Liefert eine Kopie.</summary>
    public static byte[] Crop(byte[] bgra, int width, int height, int x, int y, int w, int h)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4)
            throw new ArgumentException("Ungültige Bilddaten.", nameof(bgra));
        if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > width || y + h > height)
            throw new ArgumentOutOfRangeException(nameof(w), $"Ausschnitt {x},{y} {w}x{h} liegt nicht im Bild {width}x{height}.");

        var result = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(bgra, ((y + row) * width + x) * 4, result, row * w * 4, w * 4);
        return result;
    }

    /// <summary>Helligkeit (0..255) je Pixel, gewichtet wie in ZXing (Grünanteil betont).</summary>
    public static byte[] Luminance(byte[] bgra, int width, int height)
    {
        var lum = new byte[width * height];
        for (int i = 0, p = 0; i < lum.Length; i++, p += 4)
            lum[i] = (byte)((bgra[p] * 29 + bgra[p + 1] * 150 + bgra[p + 2] * 77) >> 8);
        return lum;
    }

    /// <summary>Vergrößert die Helligkeit eines Bildes bilinear um einen ganzzahligen Faktor (≥ 2).</summary>
    public static byte[] UpscaleLuminance(byte[] bgra, int width, int height, int factor)
    {
        if (factor < 2) throw new ArgumentOutOfRangeException(nameof(factor));
        var src = Luminance(bgra, width, height);
        int w2 = width * factor, h2 = height * factor;
        var dst = new byte[w2 * h2];

        // Stützstellen und Gewichte je Zielspalte/-zeile einmal vorberechnen
        var x0 = new int[w2]; var x1 = new int[w2]; var fx = new float[w2];
        for (int x = 0; x < w2; x++)
        {
            float s = (x + 0.5f) / factor - 0.5f;
            int i0 = (int)MathF.Floor(s);
            fx[x] = s - i0;
            x0[x] = Math.Clamp(i0, 0, width - 1);
            x1[x] = Math.Clamp(i0 + 1, 0, width - 1);
        }

        Parallel.For(0, h2, y =>
        {
            float s = (y + 0.5f) / factor - 0.5f;
            int j0 = (int)MathF.Floor(s);
            float fy = s - j0;
            int r0 = Math.Clamp(j0, 0, height - 1) * width, r1 = Math.Clamp(j0 + 1, 0, height - 1) * width;
            int o = y * w2;
            for (int x = 0; x < w2; x++)
            {
                float top = src[r0 + x0[x]] + (src[r0 + x1[x]] - src[r0 + x0[x]]) * fx[x];
                float bottom = src[r1 + x0[x]] + (src[r1 + x1[x]] - src[r1 + x0[x]]) * fx[x];
                dst[o + x] = (byte)(top + (bottom - top) * fy + 0.5f);
            }
        });
        return dst;
    }
}
