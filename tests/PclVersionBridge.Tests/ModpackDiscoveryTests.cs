using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.Tests;

public sealed class ModpackDiscoveryTests
{
    [Fact]
    public async Task SearchUsesOfficialIconAndSummaryAndExplicitSort()
    {
        Uri? requested=null;
        using var client=new HttpClient(new PackHttp(r=> { requested=r.RequestUri; return Json("""
        {"hits":[{"project_id":"abc12345","slug":"example","title":"Example","author":"Author","description":"Author original","icon_url":"https://cdn.modrinth.com/data/abc12345/icon.png","downloads":123}],"offset":0,"total_hits":1}
        """); }));
        var page=await new ModrinthCatalog(new ModpackNetwork(client)).SearchAsync(new ModpackSearchRequest("中文 & pack","1.20.1",0,"updated"),default);
        var item=Assert.Single(page.Hits);
        Assert.Equal("Author original",item.Description);
        Assert.Equal("cdn.modrinth.com",item.IconUri!.Host);
        Assert.Equal(123,item.Downloads);
        Assert.Contains("index=updated",requested!.Query);
        Assert.Contains("%26",requested.Query);
    }
    [Theory]
    [InlineData("https://evil.example/icon.png")]
    [InlineData("http://cdn.modrinth.com/icon.png")]
    [InlineData("https://cdn.modrinth.com:444/icon.png")]
    [InlineData("")]
    public async Task MissingOrUntrustedIconDoesNotInventReplacement(string icon)
    {
        var response=JsonSerializer.Serialize(new {hits=new[]{new {project_id="abc12345",slug="example",title="Example",author="Author",description="",icon_url=icon}},offset=0,total_hits=1});
        using var client=new HttpClient(new PackHttp(_=>Json(response)));
        var item=Assert.Single((await new ModrinthCatalog(new ModpackNetwork(client)).SearchAsync("",null,0,default)).Hits);
        Assert.Null(item.IconUri); Assert.Equal("",item.Description); Assert.Null(item.Downloads);
    }
    [Theory]
    [InlineData("abc12345","modpack",true)]
    [InlineData("other123","modpack",false)]
    [InlineData("abc12345","mod",false)]
    public async Task DetailsUseSourceTextAndValidateIdentityAndType(string id,string type,bool valid)
    {
        var json=JsonSerializer.Serialize(new {id,project_type=type,title="Example",description="Original <script> is just text",icon_url=(string?)null,downloads=12,game_versions=new[]{"1.20.1"},categories=new[]{"optimization"}});
        using var client=new HttpClient(new PackHttp(_=>Json(json)));
        var catalog=new ModrinthCatalog(new ModpackNetwork(client));
        if(!valid) {await Assert.ThrowsAsync<InstallException>(()=>catalog.GetProjectAsync("abc12345",default));return;}
        var detail=await catalog.GetProjectAsync("abc12345",default);
        Assert.Equal("Original <script> is just text",detail.Description);
        Assert.Null(detail.IconUri); Assert.Equal("modrinth.com",detail.ProjectUrl.Host);
        Assert.Equal(new[]{"1.20.1"},detail.GameVersions);
    }
    [Fact]
    public async Task VersionsFilterMinecraftInRequestAndResponse()
    {
        object Version(string id,string mc)=>new {id,project_id="abc12345",name=id,version_number=id,version_type="release",game_versions=new[]{mc},loaders=new[]{"fabric"},date_published="2026-01-01T00:00:00Z",files=new[]{new{primary=true,filename="pack.mrpack",url="https://cdn.modrinth.com/pack.mrpack",size=5,hashes=new{sha1=new string('0',40),sha512=new string('0',128)}}}};
        Uri? requested=null;
        using var client=new HttpClient(new PackHttp(r=>{requested=r.RequestUri;return Json(JsonSerializer.Serialize(new[]{Version("one","1.20.1"),Version("two","1.21.1")}));}));
        var versions=await new ModrinthCatalog(new ModpackNetwork(client)).GetVersionsAsync("abc12345",false,"1.20.1",default);
        Assert.Equal("one",Assert.Single(versions).Id);
        Assert.Contains("game_versions=[\"1.20.1\"]",Uri.UnescapeDataString(requested!.Query));
    }
    [Fact]
    public async Task ImageFetchIsBoundedAndRejectsUnexpectedContent()
    {
        using var huge=new HttpClient(new PackHttp(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[2*1024*1024+1])}));
        await Assert.ThrowsAsync<InstallException>(()=>new ModpackNetwork(huge).GetImageBytesAsync(new Uri("https://cdn.modrinth.com/icon.png"),default));
        using var html=new HttpClient(new PackHttp(_=>new(HttpStatusCode.OK){Content=new StringContent("<html>not an image</html>")}));
        await Assert.ThrowsAsync<InstallException>(()=>new ModpackNetwork(html).GetImageBytesAsync(new Uri("https://cdn.modrinth.com/icon.png"),default));
        await Assert.ThrowsAsync<InstallException>(()=>new ModpackNetwork(html).GetImageBytesAsync(new Uri("https://evil.example/icon.png"),default));
        var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");
        using var png=new HttpClient(new PackHttp(_=> {var content=new ByteArrayContent(bytes);content.Headers.ContentType=new MediaTypeHeaderValue("image/png");return new(HttpStatusCode.OK){Content=content};}));
        Assert.Equal(bytes,await new ModpackNetwork(png).GetImageBytesAsync(new Uri("https://cdn.modrinth.com/icon.png"),default));
    }
    private static HttpResponseMessage Json(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json)};
    [Fact]
    public async Task MissingSummaryAndCountRemainMissingInsteadOfInvented()
    {
        using var client=new HttpClient(new PackHttp(_=>Json("""
        {"hits":[{"project_id":"abc12345","slug":"example","title":"Example","author":"Author","description":null,"icon_url":null,"downloads":null}],"offset":0,"total_hits":1}
        """)));
        var item=Assert.Single((await new ModrinthCatalog(new ModpackNetwork(client)).SearchAsync("",null,0,default)).Hits);
        Assert.Equal("项目未提供简介。",item.DescriptionText);Assert.Null(item.Downloads);Assert.Null(item.IconUri);
    }
}
