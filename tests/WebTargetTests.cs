using SchleimigeScreenshots.Core;

namespace SchleimigeScreenshots.Tests;

public class WebTargetTests
{
    [Theory]
    [InlineData("https://example.com/x?y=1", "https://example.com/x?y=1")]
    [InlineData("  http://example.com  ", "http://example.com/")]
    [InlineData("www.example.de/pfad", "https://www.example.de/pfad")]
    public void Accepts_web_urls(string input, string expected) =>
        Assert.Equal(expected, WebTarget.TryGetWebUri(input)?.AbsoluteUri);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("mailto:a@b.de")]
    [InlineData("WIFI:S:x;T:WPA;P:y;;")]
    [InlineData("hallo welt https://x.de")]
    [InlineData("4006381333931")]
    [InlineData("datei.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_everything_else(string? input) => Assert.Null(WebTarget.TryGetWebUri(input));

    [Fact]
    public void Search_uri_is_escaped_and_http() =>
        Assert.Equal("https://www.google.com/search?q=a%20b%26c", WebTarget.BuildSearchUri(null, "a b&c").AbsoluteUri);

    [Fact]
    public void Bad_template_falls_back() =>
        Assert.StartsWith("https://www.google.com/", WebTarget.BuildSearchUri("javascript:{0}", "x").AbsoluteUri);
}
