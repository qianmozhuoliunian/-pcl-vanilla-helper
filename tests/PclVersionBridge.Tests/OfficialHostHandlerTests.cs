using System.Net;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Download;

namespace PclVersionBridge.Tests;

public sealed class OfficialHostHandlerTests
{
    [Fact]
    public async Task RejectsDirectUntrustedHostBeforeNetworkAccess()
    {
        using var inner = new CountingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(new OfficialHostHandler(inner));

        await Assert.ThrowsAsync<InstallException>(() => client.GetAsync("https://evil.example/file"));
        Assert.Equal(0, inner.Requests);
    }

    [Fact]
    public async Task RejectsRedirectToUntrustedHost()
    {
        using var inner = new CountingHandler(HttpStatusCode.Redirect,
            new Uri("https://evil.example/file"));
        using var client = new HttpClient(new OfficialHostHandler(inner));

        await Assert.ThrowsAsync<InstallException>(() =>
            client.GetAsync("https://piston-meta.mojang.com/file"));
        Assert.Equal(1, inner.Requests);
    }

    private sealed class CountingHandler(HttpStatusCode status, Uri? location = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            var response = new HttpResponseMessage(status);
            response.Headers.Location = location;
            return Task.FromResult(response);
        }
    }
}
