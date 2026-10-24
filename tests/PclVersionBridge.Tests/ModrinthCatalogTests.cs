using System.Net;
using System.Text.Json;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.Tests;

public sealed class ModrinthCatalogTests
{
    [Fact]
    public async Task SearchEncodesTextAndFacetsAndParsesResults()
    {
        Uri? requested = null;
        using var client = new HttpClient(new PackHttp(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"hits":[{"project_id":"abc12345","slug":"example","title":"Example","author":"Author","description":"Pack"}],"offset":20,"total_hits":21}
                """) };
        }));
        var page = await new ModrinthCatalog(new ModpackNetwork(client)).SearchAsync("a&b 中文", "1.20.1", 20, default);
        Assert.Equal("Example", Assert.Single(page.Hits).Title);
        var query = Uri.UnescapeDataString(requested!.Query);
        Assert.Contains("project_type:modpack", query);
        Assert.Contains("categories:fabric", query);
        Assert.Contains("versions:1.20.1", query);
        Assert.Contains("offset=20", query);
        Assert.Contains("a%26b", requested.Query);
    }
    [Fact]
    public async Task OnlyOffersFabricMrpacksAndHonorsPrereleaseChoice()
    {
        object Version(string id, string loader, string type, string date) => new
        {
            id, project_id = "abc12345", name = id, version_number = id, version_type = type,
            game_versions = new[] { "1.20.1" }, loaders = new[] { loader }, date_published = date,
            files = new[] { new { primary = true, filename = "pack.mrpack", url = "https://cdn.modrinth.com/pack.mrpack",
                size = 5, hashes = new { sha1 = new string('0', 40), sha512 = new string('0', 128) } } }
        };
        var json = JsonSerializer.Serialize(new[] { Version("release1", "fabric", "release", "2026-01-01T00:00:00Z"),
            Version("beta2", "fabric", "beta", "2026-02-01T00:00:00Z"), Version("forge3", "forge", "release", "2026-03-01T00:00:00Z") });
        using var client = new HttpClient(new PackHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
        var catalog = new ModrinthCatalog(new ModpackNetwork(client));
        Assert.Equal("release1", Assert.Single(await catalog.GetVersionsAsync("abc12345", false, default)).Id);
        Assert.Equal(new[] { "beta2", "release1" }, (await catalog.GetVersionsAsync("abc12345", true, default)).Select(v => v.Id));
    }
}
