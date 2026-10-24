using System.Net;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.Tests;

public sealed class ModpackNetworkTests
{
    [Fact]
    public void ProductionClientHasValidIdentifyingUserAgent()
    {
        using var client = ModpackNetwork.CreateClient();
        Assert.Contains("VanillaReach/0.2.0", client.DefaultRequestHeaders.UserAgent.ToString());
    }
    [Theory]
    [InlineData("http://cdn.modrinth.com/file")]
    [InlineData("https://cdn.modrinth.com.evil.example/file")]
    [InlineData("https://127.0.0.1/file")]
    [InlineData("https://user:password@cdn.modrinth.com/file")]
    public async Task RejectsUntrustedDestinationBeforeTransport(string url)
    {
        var transport = new PackHttp(_ => throw new InvalidOperationException("Transport must not run"));
        using var client = new HttpClient(new ModpackHostHandler(transport));
        await Assert.ThrowsAsync<InstallException>(() => client.GetAsync(url));
    }
    [Fact]
    public async Task RejectsRedirectOutsideAllowedHosts()
    {
        using var client = new HttpClient(new ModpackHostHandler(new PackHttp(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        { Headers = { Location = new Uri("https://localhost/private") } })));
        await Assert.ThrowsAsync<InstallException>(() => client.GetAsync("https://cdn.modrinth.com/file"));
    }
    [Fact]
    public async Task RejectsOversizeJsonAndRateLimit()
    {
        using var client = new HttpClient(new PackHttp(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("123456") }));
        await Assert.ThrowsAsync<InstallException>(() => new ModpackNetwork(client)
            .GetJsonAsync(new Uri("https://api.modrinth.com/v2/search"), 5, default));
        using var limited = new HttpClient(new PackHttp(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        await Assert.ThrowsAsync<InstallException>(() => new ModpackNetwork(limited)
            .GetJsonAsync(new Uri("https://api.modrinth.com/v2/search"), 100, default));
    }
    [Fact]
    public async Task BadHashDoesNotCreateDestination()
    {
        using var files = new ModpackTestFiles();
        using var client = new HttpClient(new PackHttp(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(ModpackTestFiles.Payload) }));
        var destination = Path.Combine(files.Root, "bad.jar");
        await Assert.ThrowsAsync<InstallException>(() => new ModpackNetwork(client).DownloadAsync(
            new Uri("https://cdn.modrinth.com/file"), destination, 5, new string('0', 40), new string('0', 128), default));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(files.Root));
    }
}

internal sealed class PackHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}
