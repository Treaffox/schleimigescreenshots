using SchleimigeScreenshots.Core;

namespace SchleimigeScreenshots.Tests;

public class TextUrlExtractorTests
{
    [Fact]
    public void Finds_urls_and_ignores_plain_text()
    {
        var urls = TextUrlExtractor.Extract("Hallo Welt, das ist normaler Text.\nBesuche https://example.com/a?b=1, oder www.beispiel.de.\nEnde");
        Assert.Equal(["https://example.com/a?b=1", "www.beispiel.de"], urls);
    }

    [Fact]
    public void Fixes_ocr_letter_for_zero_in_version_segment() =>
        Assert.Equal(["https://github.com/a/b/releases/tag/v1.2.0"], TextUrlExtractor.Extract("ne: https://github.com/a/b/releases/tag/v1.2.O"));

    [Fact]
    public void Leaves_words_without_digits_alone() =>
        Assert.Equal(["https://example.com/foo.lol"], TextUrlExtractor.Extract("https://example.com/foo.lol"));

    [Fact]
    public void Plain_text_yields_nothing() =>
        Assert.Empty(TextUrlExtractor.Extract("Kein Link hier, nur Text 4006381333931 und datei.txt"));

    [Fact]
    public void Joins_urls_broken_at_line_end() =>
        Assert.Equal(["https://example.com/pfad/seite"], TextUrlExtractor.Extract("https://example.com/pfad/\nseite"));

    [Fact]
    public void Removes_duplicates_and_unsafe_schemes() =>
        Assert.Equal(["https://a.de"], TextUrlExtractor.Extract("https://a.de https://A.de javascript:alert(1)"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input(string? text) => Assert.Empty(TextUrlExtractor.Extract(text));
}
