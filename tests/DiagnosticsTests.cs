using SchleimigeScreenshots.Core;

namespace SchleimigeScreenshots.Tests;

public class DiagnosticsTests
{
    private static byte[] Solid(int w, int h, byte value, byte alpha = 255)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = value; px[i + 3] = alpha; }
        return px;
    }

    [Fact]
    public void Blank_capture_is_recognised()
    {
        var st = ImageStats.Compute(Solid(1920, 1080, 0, alpha: 0), 1920, 1080);
        Assert.True(st.LooksBlank);
        Assert.Equal(1.0, st.ZeroAlphaShare, 3);
        Assert.Equal(1.0, st.NearBlackShare, 3);
        Assert.Equal(1, st.DistinctColors);
    }

    [Fact]
    public void Busy_capture_is_not_blank()
    {
        var rnd = new Random(1);
        var px = new byte[640 * 480 * 4];
        rnd.NextBytes(px);
        var st = ImageStats.Compute(px, 640, 480);
        Assert.False(st.LooksBlank);
        Assert.InRange(st.MeanLuma, 100, 155);
        Assert.True(st.DistinctColors > 1000);
    }

    [Fact]
    public void ImageStats_rejects_bad_input() =>
        Assert.Throws<ArgumentException>(() => ImageStats.Compute(new byte[8], 100, 100));

    [Fact]
    public void Blank_screen_runs_both_passes_over_all_regions()
    {
        var outcome = CodeScanner.ScanDetailed(Solid(1920, 1080, 255), 1920, 1080);
        Assert.Empty(outcome.Results);
        Assert.Equal(["normal", "invertiert"], outcome.Passes.Select(p => p.Name));
        Assert.All(outcome.Passes, p => Assert.Equal(1 + 4 + 9, p.Regions.Count));
        Assert.All(outcome.Passes.SelectMany(p => p.Regions), r => Assert.Null(r.Error));
        Assert.True(outcome.Milliseconds > 0);
    }

    [Fact]
    public void Small_image_is_scanned_as_one_region()
    {
        var outcome = CodeScanner.ScanDetailed(Solid(300, 300, 255), 300, 300);
        Assert.All(outcome.Passes, p => Assert.Single(p.Regions));
    }

    [Fact]
    public void Selftest_passes()
    {
        var (ok, message) = SelfTest.Run();
        Assert.True(ok, message);
    }
}
