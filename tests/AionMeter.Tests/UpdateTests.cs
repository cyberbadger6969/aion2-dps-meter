using System.Net;
using System.Security.Cryptography;
using AionMeter.Core.Updates;

namespace AionMeter.Tests;

public class UpdateTests
{
    // Shape of GitHub's /releases/latest answer (trimmed), as the meter receives it.
    private const string Release = """
        {"tag_name":"v0.2.0","html_url":"https://github.com/o/r/releases/tag/v0.2.0","draft":false,"prerelease":false,
         "published_at":"2026-10-07T10:00:00Z","body":"## What's new\nUpdate check.",
         "assets":[
           {"name":"AION2DpsMeter-v0.2.0-source.zip","size":10,"browser_download_url":"https://e/src.zip","digest":"sha256:aa"},
           {"name":"AION2DpsMeter-Setup-v0.2.0.exe","size":52786865,"browser_download_url":"https://e/setup.exe","digest":"sha256:93AF8A"},
           {"name":"AION2DpsMeter-v0.2.0-win-x64.zip","size":65388791,"browser_download_url":"https://e/win.zip"}]}
        """;

    [Fact]
    public void Latest_release_gives_version_installer_and_digest()
    {
        var r = UpdateFeed.Parse(Release)!;
        Assert.Equal(new Version(0, 2, 0), r.Version);
        Assert.Equal("https://github.com/o/r/releases/tag/v0.2.0", r.PageUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero), r.PublishedAt);
        Assert.Equal("AION2DpsMeter-Setup-v0.2.0.exe", r.Installer!.Name);
        Assert.Equal(52786865, r.Installer.Size);
        Assert.Equal("93af8a", r.Installer.Sha256);
        Assert.Equal("AION2DpsMeter-v0.2.0-win-x64.zip", r.Portable!.Name);
        Assert.Null(r.Portable.Sha256);
    }

    [Fact]
    public void Drafts_prereleases_and_odd_tags_are_not_updates()
    {
        Assert.Null(UpdateFeed.Parse(Release.Replace("\"prerelease\":false", "\"prerelease\":true")));
        Assert.Null(UpdateFeed.Parse(Release.Replace("\"draft\":false", "\"draft\":true")));
        Assert.Null(UpdateFeed.Parse(Release.Replace("\"v0.2.0\"", "\"nightly\"")));
    }

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.2", "0.2.0")]
    [InlineData("v1.0.3-beta", "1.0.3")]
    [InlineData("V2", "2.0.0")]
    public void Tags_read_as_major_minor_patch(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateFeed.ParseVersion(tag));

    [Fact]
    public void Assembly_version_equals_its_release_tag()
    {
        Assert.Equal(UpdateFeed.ParseVersion("v0.1.0"), UpdateFeed.Normalize(new Version(0, 1, 0, 0)));
        Assert.True(UpdateFeed.ParseVersion("v0.10.0") > UpdateFeed.ParseVersion("v0.9.3"));
    }

    [Fact]
    public async Task Downloads_are_checked_against_size_and_sha256()
    {
        var body = new byte[300_000];
        new Random(7).NextBytes(body);
        var sha = Convert.ToHexStringLower(SHA256.HashData(body));
        var dir = Path.Combine(Path.GetTempPath(), "aionmeter-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var feed = new UpdateFeed("test", new FixedResponse(body));
            var progress = new List<double>();
            var path = await feed.DownloadAsync(new ReleaseAsset("Setup.exe", body.Length, "https://e/Setup.exe", sha), dir,
                new SyncProgress(progress.Add));
            Assert.Equal(body, await File.ReadAllBytesAsync(path));
            Assert.Equal(1.0, progress[^1]);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                feed.DownloadAsync(new ReleaseAsset("Tampered.exe", body.Length, "https://e/T.exe", new string('0', 64)), dir, null));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                feed.DownloadAsync(new ReleaseAsset("Short.exe", body.Length + 1, "https://e/S.exe", null), dir, null));
            // Nothing but the good file is left behind.
            Assert.Equal(["Setup.exe"], Directory.GetFiles(dir).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class FixedResponse(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
