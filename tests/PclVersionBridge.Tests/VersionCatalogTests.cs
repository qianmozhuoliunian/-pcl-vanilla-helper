using System.Net;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Catalog;

namespace PclVersionBridge.Tests;

public sealed class VersionCatalogTests
{
    [Fact]
    public async Task ListsOnlyReleasesWithTheirManifestHashes()
    {
        using var client = ClientFor("""
            {"latest":{"release":"26.3","snapshot":"26.4-snapshot-5"},"versions":[
              {"id":"26.4-snapshot-5","type":"snapshot","url":"https://piston-meta.mojang.com/snapshot.json","sha1":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
              {"id":"26.3","type":"release","url":"https://piston-meta.mojang.com/26.3.json","sha1":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}
            """);

        var releases = await new VersionCatalog(client).ListReleasesAsync(CancellationToken.None);

        var only = Assert.Single(releases);
        Assert.Equal("26.3", only.Id);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", only.ManifestSha1);
    }

    [Theory]
    [InlineData("https://evil.example/version.json", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("https://piston-meta.mojang.com/version.json", "bad")]
    [InlineData("https://piston-meta.mojang.com/version.json", "")]
    public async Task RejectsUntrustedOrUnverifiableRelease(string url, string sha1)
    {
        using var client = ClientFor($"{{\"versions\":[{{\"id\":\"26.3\",\"type\":\"release\",\"url\":\"{url}\",\"sha1\":\"{sha1}\"}}]}}");

        await Assert.ThrowsAsync<InstallException>(() =>
            new VersionCatalog(client).ListReleasesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RejectsMalformedCatalog()
    {
        using var client = ClientFor("not json");

        await Assert.ThrowsAsync<InstallException>(() =>
            new VersionCatalog(client).ListReleasesAsync(CancellationToken.None));
    }

    private static HttpClient ClientFor(string body) => new(new FixedHandler(body));

    private sealed class FixedHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body) });
    }
}
