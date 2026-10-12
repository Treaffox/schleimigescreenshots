using System.Net;
using System.Security.Cryptography;
using System.Text;
using SchleimigeScreenshots.Core.Updates;

namespace SchleimigeScreenshots.Tests;

public class SemVerTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("V10.0.1", 10, 0, 1, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("7", 7, 0, 0, "")]
    [InlineData("1.0.1+433cd2654e5fcecee75ec340a17c8d4823bc820e", 1, 0, 1, "")]
    [InlineData("2.0.0-beta.1", 2, 0, 0, "beta.1")]
    [InlineData("2.0.0-rc.2+build5", 2, 0, 0, "rc.2")]
    public void Parses_versions(string text, int major, int minor, int patch, string pre)
    {
        Assert.True(SemVer.TryParse(text, out var v));
        Assert.Equal(new SemVer(major, minor, patch, pre), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3-")]
    [InlineData("-1.0.0")]
    [InlineData("1..2")]
    public void Rejects_garbage(string text) => Assert.False(SemVer.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.0-beta", 1)]          // Release > Vorabversion
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", 1)]
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2", 1)] // numerisch, nicht textlich
    [InlineData("1.0.0-beta", "1.0.0-alpha", 1)]
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]       // numerisch < alphanumerisch
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    public void Compares_like_semver(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(SemVer.Parse(a).CompareTo(SemVer.Parse(b))));

    [Fact]
    public void Operators_and_ToString()
    {
        Assert.True(SemVer.Parse("1.1.0") > SemVer.Parse("1.0.1"));
        Assert.True(SemVer.Parse("1.0.1") <= SemVer.Parse("1.0.1"));
        Assert.Equal("1.2.3-beta.1", SemVer.Parse("v1.2.3-beta.1+abc").ToString());
    }
}

/// <summary>HttpMessageHandler, der Anfragen per Delegate beantwortet (kein Netz nötig).</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!.ToString());
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class UpdateCheckerTests
{
    private static readonly Uri Feed = new("https://api.example.test/releases");
    private const string Prefix = "https://github.example/dl/";

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Release(string tag, bool prerelease = false, bool draft = false, string assets = "") =>
        $$"""{"tag_name":"{{tag}}","draft":{{draft.ToString().ToLower()}},"prerelease":{{prerelease.ToString().ToLower()}},"html_url":"https://github.example/r/{{tag}}","body":"Notizen {{tag}}","assets":[{{assets}}]}""";

    private static string Asset(string name, string? digest = null, long size = 100, string urlPrefix = Prefix) =>
        $$"""{"name":"{{name}}","size":{{size}},"browser_download_url":"{{urlPrefix}}{{name}}"{{(digest is null ? "" : $$""","digest":"sha256:{{digest}}" """)}}}""";

    private static UpdateChecker Checker(string feedJson, FakeHandler? handler = null, string? prefix = Prefix)
    {
        handler ??= new FakeHandler(_ => FakeHandler.Json(feedJson));
        return new UpdateChecker(new HttpClient(handler), Feed, prefix);
    }

    private static string RealFixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-releases.json"));

    [Fact]
    public async Task Real_github_response_yields_update_with_digests()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => FakeHandler.Json(RealFixture()))), Feed,
            "https://github.com/Treaffox/schleimigescreenshots/releases/download/");

        var update = await checker.CheckAsync(SemVer.Parse("1.0.0"), includePreReleases: true);

        Assert.NotNull(update);
        Assert.Equal("v1.0.1", update.Tag);
        Assert.True(update.IsPreRelease);
        Assert.Equal("SchleimigeScreenshots-Setup-1.0.1.exe", update.Setup?.Name);
        Assert.Equal("SchleimigeScreenshots.exe", update.Portable?.Name);
        Assert.Matches("^[0-9a-f]{64}$", update.Setup!.Sha256!);
        Assert.Matches("^[0-9a-f]{64}$", update.Portable!.Sha256!);
        Assert.StartsWith("https://github.com/Treaffox/schleimigescreenshots/releases/download/v1.0.1/", update.Setup.Url);
    }

    [Fact]
    public async Task No_update_when_current_is_latest_or_newer()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => FakeHandler.Json(RealFixture()))), Feed);
        Assert.Null(await checker.CheckAsync(SemVer.Parse("1.0.1"), true));
        Assert.Null(await checker.CheckAsync(SemVer.Parse("1.2.0"), true));
    }

    [Fact]
    public async Task Prereleases_are_skipped_unless_requested()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => FakeHandler.Json(RealFixture()))), Feed);
        Assert.Null(await checker.CheckAsync(SemVer.Parse("1.0.0"), includePreReleases: false)); // beide Releases sind Pre-releases
        Assert.NotNull(await checker.CheckAsync(SemVer.Parse("1.0.0"), includePreReleases: true));
    }

    [Fact]
    public async Task Picks_highest_version_and_ignores_drafts()
    {
        var json = "[" + string.Join(",", [
            Release("v1.1.0", assets: Asset("SchleimigeScreenshots.exe", Sha("a"))),
            Release("v1.3.0", draft: true),
            Release("v1.2.0", assets: Asset("SchleimigeScreenshots.exe", Sha("b"))),
            Release("nonsense"),
            Release("v1.0.0")]) + "]";

        var update = await Checker(json).CheckAsync(SemVer.Parse("1.0.0"), true);

        Assert.Equal("v1.2.0", update!.Tag);
        Assert.Equal("Notizen v1.2.0", update.Notes);
    }

    [Fact]
    public async Task Missing_digest_is_filled_from_sha256sums_file()
    {
        var setup = "SchleimigeScreenshots-Setup-1.1.0.exe";
        var sums = $"{Sha("setup")}  {setup}\n{Sha("portable")} *SchleimigeScreenshots.exe\n";
        var json = "[" + Release("v1.1.0", assets: string.Join(",", [Asset(setup), Asset("SchleimigeScreenshots.exe"), Asset("SHA256SUMS.txt")])) + "]";
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sums) }
            : FakeHandler.Json(json));

        var update = await Checker(json, handler).CheckAsync(SemVer.Parse("1.0.0"), true);

        Assert.Equal(Sha("setup"), update!.Setup!.Sha256);
        Assert.Equal(Sha("portable"), update.Portable!.Sha256);
    }

    [Fact]
    public async Task Without_any_checksum_the_asset_has_no_digest()
    {
        var json = "[" + Release("v1.1.0", assets: Asset("SchleimigeScreenshots-Setup-1.1.0.exe")) + "]";

        var update = await Checker(json).CheckAsync(SemVer.Parse("1.0.0"), true);

        Assert.NotNull(update!.Setup);
        Assert.Null(update.Setup.Sha256); // der Aufrufer installiert dann nicht automatisch
        Assert.Null(update.Portable);
    }

    [Fact]
    public async Task Assets_outside_the_trusted_prefix_are_ignored()
    {
        var json = "[" + Release("v1.1.0", assets: string.Join(",", [
            Asset("SchleimigeScreenshots-Setup-1.1.0.exe", Sha("evil"), urlPrefix: "https://evil.example/"),
            Asset("SchleimigeScreenshots.exe", Sha("ok")),
        ])) + "]";

        var update = await Checker(json).CheckAsync(SemVer.Parse("1.0.0"), true);

        Assert.Null(update!.Setup);
        Assert.NotNull(update.Portable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Ratenlimit")]
    [InlineData(HttpStatusCode.TooManyRequests, "Ratenlimit")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task Http_errors_become_readable_exceptions(HttpStatusCode status, string expectedText)
    {
        var checker = Checker("", new FakeHandler(_ => new HttpResponseMessage(status)));
        var ex = await Assert.ThrowsAsync<UpdateCheckException>(() => checker.CheckAsync(SemVer.Parse("1.0.0"), true));
        Assert.Contains(expectedText, ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"message\":\"object, not array\"}")]
    public async Task Garbage_responses_become_exceptions(string body) =>
        await Assert.ThrowsAsync<UpdateCheckException>(() => Checker(body).CheckAsync(SemVer.Parse("1.0.0"), true));

    [Fact]
    public async Task Network_failure_becomes_exception()
    {
        var checker = Checker("", new FakeHandler(_ => throw new HttpRequestException("kein Netz")));
        var ex = await Assert.ThrowsAsync<UpdateCheckException>(() => checker.CheckAsync(SemVer.Parse("1.0.0"), true));
        Assert.Contains("kein Netz", ex.Message);
    }
}

public class UpdateDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "schleimige-test-" + Guid.NewGuid().ToString("N"));

    public UpdateDownloaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Payload(int size) => Enumerable.Range(0, size).Select(i => (byte)(i * 31 % 251)).ToArray();
    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static HttpClient ClientFor(byte[] data, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new FakeHandler(_ => new HttpResponseMessage(status) { Content = new ByteArrayContent(data) }));

    [Fact]
    public async Task Downloads_and_verifies()
    {
        var data = Payload(300_000);
        var asset = new ReleaseAsset("x.exe", "https://h.example/x.exe", data.Length, Sha(data));
        var target = Path.Combine(_dir, "sub", "x.exe");
        var progress = new List<double>();

        await UpdateDownloader.DownloadAsync(ClientFor(data), asset, target, progress: new Progress<double>(progress.Add));
        await Task.Delay(50); // Progress<T> meldet asynchron

        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        Assert.False(File.Exists(target + ".partial"));
        Assert.Contains(progress, p => p >= 0.99);
    }

    [Fact]
    public async Task Wrong_checksum_leaves_nothing_behind()
    {
        var data = Payload(10_000);
        var asset = new ReleaseAsset("x.exe", "https://h.example/x.exe", data.Length, new string('0', 64));
        var target = Path.Combine(_dir, "x.exe");

        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateDownloader.DownloadAsync(ClientFor(data), asset, target));

        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".partial"));
    }

    [Fact]
    public async Task Existing_file_is_not_touched_when_verification_fails()
    {
        var target = Path.Combine(_dir, "x.exe");
        await File.WriteAllTextAsync(target, "alt");
        var data = Payload(1_000);
        var asset = new ReleaseAsset("x.exe", "https://h.example/x.exe", data.Length, new string('a', 64));

        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateDownloader.DownloadAsync(ClientFor(data), asset, target));

        Assert.Equal("alt", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Size_mismatch_is_rejected()
    {
        var data = Payload(5_000);
        var asset = new ReleaseAsset("x.exe", "https://h.example/x.exe", data.Length + 1, Sha(data));
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateDownloader.DownloadAsync(ClientFor(data), asset, Path.Combine(_dir, "x.exe")));
    }

    [Fact]
    public async Task Http_error_is_reported()
    {
        var asset = new ReleaseAsset("x.exe", "https://h.example/x.exe", 10, new string('a', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            UpdateDownloader.DownloadAsync(ClientFor([], HttpStatusCode.NotFound), asset, Path.Combine(_dir, "x.exe")));
    }

    [Fact]
    public async Task Refuses_assets_without_checksum()
    {
        var asset = new ReleaseAsset("x.exe", "https://h.example/x.exe", 10, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateDownloader.DownloadAsync(ClientFor([1, 2, 3]), asset, Path.Combine(_dir, "x.exe")));
    }
}
