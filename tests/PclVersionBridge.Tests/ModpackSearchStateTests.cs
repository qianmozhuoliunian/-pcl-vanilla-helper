using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Modpacks;
using PclVersionBridge.Core.Presentation;
namespace PclVersionBridge.Tests;

public sealed class ModpackSearchStateTests
{
    [Fact]
    public async Task PagingUsesSubmittedCriteriaAndBoundariesAreAccurate()
    {
        using var files=new ModpackTestFiles();using var http=new HttpClient();
        var catalog=new Catalog();var vm=Vm(catalog,http,files.Root);
        vm.Query="old";vm.GameVersion="1.20.1";vm.Sort="updated";
        await vm.SearchAsync();Assert.True(vm.CanNextPage);Assert.False(vm.CanPreviousPage);
        vm.Query="new";vm.GameVersion="1.21.1";vm.Sort="downloads";
        Assert.True(vm.HasPendingSearch);Assert.False(vm.CanNextPage);
        await vm.ChangePageAsync(20);
        var sent=catalog.Requests.Last();
        Assert.Equal(new ModpackSearchRequest("old","1.20.1",20,"updated"),sent);
        await vm.SearchAsync();
        Assert.Equal(new ModpackSearchRequest("new","1.21.1",0,"downloads"),catalog.Requests.Last());
        Assert.False(vm.HasPendingSearch);
        await vm.ChangePageAsync(40);Assert.False(vm.CanNextPage);Assert.True(vm.CanPreviousPage);
        var count=catalog.Requests.Count;await vm.ChangePageAsync(60);Assert.Equal(count,catalog.Requests.Count);
    }
    [Fact]
    public async Task FailedNewSearchClearsPreviousResultsAndSelection()
    {
        using var files=new ModpackTestFiles();using var http=new HttpClient();
        var catalog=new Catalog();var vm=Vm(catalog,http,files.Root);
        await vm.SearchAsync();vm.SelectedProject=vm.Projects.First();
        catalog.Fail=true;await vm.SearchAsync();
        Assert.Empty(vm.Projects);Assert.Null(vm.SelectedProject);Assert.Equal(0,vm.Total);Assert.False(vm.CanNextPage);
        Assert.NotEmpty(vm.Error);
    }
    [Fact]
    public async Task MinecraftAndPrereleaseChangesInvalidateVersionsAndPreview()
    {
        using var files=new ModpackTestFiles();using var http=new HttpClient();
        var catalog=new Catalog();var vm=Vm(catalog,http,files.Root);
        await vm.SearchAsync();vm.SelectedProject=vm.Projects.First();vm.GameVersion="1.20.1";
        await vm.LoadVersionsAsync();Assert.Equal("1.20.1",catalog.RequestedMinecraft);
        vm.SelectedRelease=Assert.Single(vm.Releases);
        await vm.ImportAsync(files.Pack());Assert.NotNull(vm.Preview);
        vm.GameVersion="1.21.1";Assert.Null(vm.Preview);Assert.Empty(vm.Releases);Assert.Null(vm.SelectedRelease);
        await vm.LoadVersionsAsync();vm.SelectedRelease=Assert.Single(vm.Releases);
        vm.IncludePrerelease=true;Assert.Empty(vm.Releases);Assert.Null(vm.SelectedRelease);
    }
    [Fact]
    public async Task SlowSearchCannotPublishResultsForChangedDraft()
    {
        using var files=new ModpackTestFiles();using var http=new HttpClient();
        var catalog=new Catalog{Pending=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var vm=Vm(catalog,http,files.Root);vm.Query="old";var task=vm.SearchAsync();vm.Query="new";
        catalog.Pending.SetResult(new ModpackSearchPage(new[]{Project()},0,1));await task;
        Assert.Empty(vm.Projects);Assert.False(vm.CanNextPage);
    }
    private static ModpackViewModel Vm(Catalog catalog,HttpClient http,string root)=>new(catalog,new ModpackContentSource(new ModpackNetwork(http)),new Installer(),new InstallationGate(),root);
    private static ModpackProject Project()=>new("abc12345","fixture","Fixture pack","Fixture author","Fixture description");
    private sealed class Catalog:IModrinthCatalog
    {
        public List<ModpackSearchRequest> Requests=new();public bool Fail;public string? RequestedMinecraft;
        public TaskCompletionSource<ModpackSearchPage>? Pending;
        public Task<ModpackSearchPage> SearchAsync(string q,string? mc,int offset,CancellationToken token)=>SearchAsync(new(q,mc,offset),token);
        public Task<ModpackSearchPage> SearchAsync(ModpackSearchRequest request,CancellationToken token)
        {
            Requests.Add(request);if(Fail)throw new IOException("private path");
            return Pending?.Task??Task.FromResult(new ModpackSearchPage(Enumerable.Range(0,Math.Min(20,45-request.Offset)).Select(_=>Project()).ToArray(),request.Offset,45));
        }
        public Task<IReadOnlyList<ModpackRelease>> GetVersionsAsync(string id,bool pre,CancellationToken token)=>GetVersionsAsync(id,pre,null,token);
        public Task<IReadOnlyList<ModpackRelease>> GetVersionsAsync(string id,bool pre,string? mc,CancellationToken token)
        {RequestedMinecraft=mc;return Task.FromResult<IReadOnlyList<ModpackRelease>>(new[]{new ModpackRelease("fixture",id,"Fixture","1",new[]{mc??"1.20.1"},"release",new Uri("https://cdn.modrinth.com/fixture.mrpack"),5,new string('0',40),new string('0',128))});}
    }
    private sealed class Installer:IModpackInstallService
    {public Task InstallAsync(PackPreview p,string id,string root,IReadOnlySet<string> opt,IProgress<InstallProgress> progress,CancellationToken token)=>Task.CompletedTask;}
}
