using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Catalog;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.Tests;

public sealed class FabricInstallSourceTests
{
    [Fact]
    public async Task PinsLoaderAndVerifiesMavenSidecarAndInheritedVersion()
    {
        using var files = new ModpackTestFiles();
        var preview = await new MrpackReader().ReadAsync(files.Pack(), default);
        var requests = new List<string>();
        var hash = Convert.ToHexString(SHA1.HashData(ModpackTestFiles.Payload));
        using var client = new HttpClient(new PackHttp(request =>
        {
            requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("profile/json"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                { id = "fabric-default", inheritsFrom = "1.20.1", mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                    libraries = new[] { new { name = "net.fabricmc:fabric-loader:0.16.0", url = "https://maven.fabricmc.net/" } } })) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri.AbsolutePath.EndsWith(".sha1")
                ? new StringContent(hash) : new ByteArrayContent(ModpackTestFiles.Payload) };
        }));
        var source = new FabricInstallSource(new Catalog(), new BaseSource(), new ModpackNetwork(client));
        var staged = await source.StageAsync(preview.Plan, "my-pack", files.Root, new Progress<InstallProgress>(), default);
        var profile = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(files.Root, "versions/my-pack/my-pack.json")));
        Assert.Equal("my-pack", profile.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.20.1", profile.RootElement.GetProperty("inheritsFrom").GetString());
        Assert.Contains(requests, r => r.Contains("/loader/1.20.1/0.16.0/profile/json"));
        Assert.Contains(requests, r => r.EndsWith(".sha1"));
        Assert.Equal(2, staged.Instance.Files.Count);
        await StagedVerifier.VerifyAsync(staged.Instance, default);
    }
    private sealed class Catalog : IVersionCatalog
    {
        public Task<IReadOnlyList<VersionChoice>> ListReleasesAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<VersionChoice>>(new[] { new VersionChoice("1.20.1", new Uri("https://piston-meta.mojang.com/base.json"), new string('0',40)) });
    }
    private sealed class BaseSource : IInstallSource
    {
        public Task<StagedVersion> StageAsync(VersionChoice version, string root, IProgress<InstallProgress> progress, CancellationToken token) =>
            Task.FromResult(new StagedVersion(version, root, Array.Empty<StagedFile>()));
    }
}
