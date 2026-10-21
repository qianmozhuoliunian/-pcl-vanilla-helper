using System.Net;
using System.Security.Cryptography;
using System.Text;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Tests;

public sealed class CmlInstallSourceTests
{
    [Fact]
    public async Task StagesAndVerifiesMinimalVanillaVersionWithoutJavaRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-bridge-adapter-test-" + Guid.NewGuid().ToString("N"), ".minecraft");
        var assetIndex = "{\"objects\":{}}";
        var versionJson = $$$"""
            {"id":"26.3","type":"release","time":"2026-10-20T00:00:00+00:00","releaseTime":"2026-10-20T00:00:00+00:00",
             "mainClass":"net.minecraft.client.main.Main","assets":"test",
             "assetIndex":{"id":"test","sha1":"{{{Sha(assetIndex)}}}","size":{{{Encoding.UTF8.GetByteCount(assetIndex)}}},"url":"https://piston-meta.mojang.com/asset-index.json"},
             "downloads":{"client":{"sha1":"a9993e364706816aba3e25717850c26c9cd0d89d","size":3,"url":"https://piston-data.mojang.com/client.jar"}},
             "libraries":[],"arguments":{"game":[],"jvm":[]}}
            """;
        var manifest = $$$"""
            {"latest":{"release":"26.3"},"versions":[{"id":"26.3","type":"release","url":"https://piston-meta.mojang.com/26.3.json","sha1":"{{{Sha(versionJson)}}}"}]}
            """;
        using var client = new HttpClient(new OfficialHostHandler(new FixtureHandler(manifest, versionJson, assetIndex)));
        var choice = new VersionChoice("26.3", new Uri("https://piston-meta.mojang.com/26.3.json"), Sha(versionJson));

        try
        {
            var result = await new CmlInstallSource(client).StageAsync(choice, root,
                new Progress<InstallProgress>(), CancellationToken.None);

            Assert.Contains(result.Files, f => f.RelativePath == Path.Combine("versions", "26.3", "26.3.json"));
            Assert.Contains(result.Files, f => f.RelativePath == Path.Combine("assets", "indexes", "test.json"));
            Assert.Equal("abc", await File.ReadAllTextAsync(Path.Combine(root, "versions", "26.3", "26.3.jar")));
            Assert.False(Directory.Exists(Path.Combine(root, "runtime")));
        }
        finally
        {
            var parent = Directory.GetParent(root)!.FullName;
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    private static string Sha(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private sealed class FixtureHandler(string manifest, string version, string index) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var content = path switch
            {
                "/mc/game/version_manifest_v2.json" => manifest,
                "/26.3.json" => version,
                "/asset-index.json" => index,
                "/client.jar" => "abc",
                _ => throw new InvalidOperationException($"Unexpected request {path}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(content, Encoding.UTF8) });
        }
    }
}
