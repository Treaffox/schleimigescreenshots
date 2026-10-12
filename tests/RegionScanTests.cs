using SchleimigeScreenshots.Core;

namespace SchleimigeScreenshots.Tests;

/// <summary>Ausschnitte (vom Nutzer gewählter Bildschirmbereich) und feine Codes in kleinen Bildern.</summary>
public class RegionScanTests
{
    [Fact]
    public void Crop_copies_the_requested_rectangle()
    {
        const int w = 8, h = 6;
        var img = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            img[i] = (byte)x; img[i + 1] = (byte)y; img[i + 2] = 7; img[i + 3] = 255;
        }

        var crop = ImageOps.Crop(img, w, h, 2, 1, 3, 4);

        Assert.Equal(3 * 4 * 4, crop.Length);
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 3; x++)
        {
            int i = (y * 3 + x) * 4;
            Assert.Equal((byte)(x + 2), crop[i]);
            Assert.Equal((byte)(y + 1), crop[i + 1]);
        }
    }

    [Theory]
    [InlineData(-1, 0, 2, 2)]
    [InlineData(0, 0, 0, 2)]
    [InlineData(7, 0, 2, 2)]
    [InlineData(0, 5, 2, 2)]
    public void Crop_rejects_rectangles_outside_the_image(int x, int y, int w, int h) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.Crop(new byte[8 * 6 * 4], 8, 6, x, y, w, h));

    [Fact]
    public void Upscale_keeps_flat_areas_and_scales_the_size()
    {
        var img = new byte[10 * 10 * 4];
        for (int i = 0; i < img.Length; i += 4) { img[i] = img[i + 1] = img[i + 2] = 200; img[i + 3] = 255; }

        var up = ImageOps.UpscaleLuminance(img, 10, 10, 3);

        Assert.Equal(30 * 30, up.Length);
        Assert.All(up, v => Assert.InRange(v, 199, 201));
    }

    // Bei diesen Modulgrößen (mit festem Seed) scheitert der normale Durchlauf, erst die Vergrößerung findet den Code.
    [Theory]
    [InlineData(1.5)]
    [InlineData(1.6)]
    public void Tiny_modules_in_a_small_region_are_found_by_upscaling(double moduleSize)
    {
        const string text = "https://www.example.org/pfad?token=abc123XYZ";
        const int w = 1920, h = 1080;
        var px = RealisticScreenTests.Background(w, h, dark: false, seed: 5);
        RealisticScreenTests.DrawSmooth(px, w, RealisticScreenTests.Encode(text), moduleSize, 900, 300, invert: false);

        // wie bei der Bereichsauswahl: nur den Ausschnitt um den Code scannen
        var crop = ImageOps.Crop(px, w, h, 850, 250, 400, 400);
        var outcome = CodeScanner.ScanDetailed(crop, 400, 400);

        Assert.Contains(outcome.Results, r => r.Text == text);
        Assert.Contains(outcome.Passes, p => p.Name.StartsWith('x')); // erst die Vergrößerung hat ihn gefunden
        var hit = outcome.Results.First(r => r.Text == text);
        Assert.InRange(hit.X, 50, 350);   // Position wird auf Originalkoordinaten zurückgerechnet
        Assert.InRange(hit.Y, 50, 350);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(1.6)]
    [InlineData(1.7)]
    [InlineData(1.8)]
    [InlineData(1.9)]
    [InlineData(2.0)]
    [InlineData(2.2)]
    public void Fine_codes_in_a_selected_region_are_found(double moduleSize)
    {
        const string text = "https://www.example.org/pfad?token=abc123XYZ";
        const int w = 1920, h = 1080;
        var px = RealisticScreenTests.Background(w, h, dark: false, seed: 9);
        RealisticScreenTests.DrawSmooth(px, w, RealisticScreenTests.Encode(text), moduleSize, 900, 300, invert: false);

        var crop = ImageOps.Crop(px, w, h, 850, 250, 400, 400);

        Assert.Contains(CodeScanner.Scan(crop, 400, 400), r => r.Text == text);
    }

    [Fact]
    public void Large_images_are_not_upscaled()
    {
        var px = new byte[1920 * 1080 * 4];
        Array.Fill(px, (byte)255);

        var outcome = CodeScanner.ScanDetailed(px, 1920, 1080);

        Assert.DoesNotContain(outcome.Passes, p => p.Name.StartsWith('x'));
    }

    [Fact]
    public void Blank_small_region_tries_all_passes_and_finds_nothing()
    {
        var px = new byte[300 * 300 * 4];
        Array.Fill(px, (byte)255);

        var outcome = CodeScanner.ScanDetailed(px, 300, 300);

        Assert.Empty(outcome.Results);
        Assert.Equal(["normal", "invertiert", "x2", "x2 invertiert", "x3", "x3 invertiert"], outcome.Passes.Select(p => p.Name));
    }
}
