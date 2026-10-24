using System.Net;
using System.Text.Json;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Friends;
using PclVersionBridge.Core.Modpacks;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Presentation;

namespace PclVersionBridge.Tests;

public sealed class PinnedModpackTests
{
    private static readonly string Hash = new('a',128);
    private static string Version(string project="abc12345",string id="ver12345",string loader="fabric",string mc="1.20.1",string? hash=null,string url="https://cdn.modrinth.com/exact.mrpack") => JsonSerializer.Serialize(new
    {
        id, project_id=project, name="Publisher version",version_number="v1",version_type="release",loaders=new[]{loader}, game_versions=new[]{mc},
        files=new object[]{new{primary=true,filename="other.mrpack",url="https://cdn.modrinth.com/other.mrpack",size=5,hashes=new{sha1=new string('0',40),sha512=new string('b',128)}},
            new{primary=false,filename="exact.mrpack",url,size=5,hashes=new{sha1=new string('0',40),sha512=hash??Hash}}}
    });
    private static ModrinthCatalog Catalog(string version,string type="modpack")
    {
        var client=new HttpClient(new PackHttp(r=>new(HttpStatusCode.OK){Content=new StringContent(
            r.RequestUri!.Host=="piston-meta.mojang.com" ? "{\"versions\":[{\"id\":\"1.20.1\",\"type\":\"release\"},{\"id\":\"24w01a\",\"type\":\"snapshot\"}]}" :
            r.RequestUri.AbsolutePath.Contains("/project/") ? JsonSerializer.Serialize(new{id="abc12345",project_type=type,title="From publisher"}) : version)}));
        return new ModrinthCatalog(new ModpackNetwork(client));
    }
    [Fact]
    public async Task ChoosesPinnedArchiveRatherThanDifferentPrimaryFile()
    {
        var release=await Catalog(Version()).GetPinnedReleaseAsync("abc12345","ver12345",Hash.ToUpperInvariant(),default);
        Assert.Equal("ver12345",release.Id);Assert.Equal("/exact.mrpack",release.FileUrl.AbsolutePath);
        Assert.Equal(Hash.ToUpperInvariant(),release.Sha512);
    }
    [Theory]
    [InlineData("wrong","ver12345","fabric","1.20.1","modpack")]
    [InlineData("abc12345","wrong","fabric","1.20.1","modpack")]
    [InlineData("abc12345","ver12345","forge","1.20.1","modpack")]
    [InlineData("abc12345","ver12345","fabric","24w01a","modpack")]
    [InlineData("abc12345","ver12345","fabric","1.20.1","mod")]
    public async Task RejectsWrongIdentityUnsupportedLoaderSnapshotOrProjectType(string project,string id,string loader,string mc,string type)
    {
        await Assert.ThrowsAsync<InstallException>(()=>Catalog(Version(project,id,loader,mc),type).GetPinnedReleaseAsync("abc12345","ver12345",Hash,default));
    }
    [Fact]
    public async Task MissingHashOrUntrustedUrlNeverFallsBackToAnotherFile()
    {
        await Assert.ThrowsAsync<InstallException>(()=>Catalog(Version(hash:new string('c',128))).GetPinnedReleaseAsync("abc12345","ver12345",Hash,default));
        await Assert.ThrowsAsync<InstallException>(()=>Catalog(Version(url:"https://evil.example/exact.mrpack")).GetPinnedReleaseAsync("abc12345","ver12345",Hash,default));
        await Assert.ThrowsAsync<InstallException>(()=>Catalog(Version()).GetPinnedReleaseAsync("../unsafe","ver12345",Hash,default));
    }
    [Fact]
    public async Task InvitationOnlyPreviewsExactReferenceAndKeepsInstallGuards()
    {
        using var files=new ModpackTestFiles();
        var content=new CaptureContent();var vm=new ModpackViewModel(Catalog(Version()),content,new NoInstall(),new InstallationGate(),files.Root);
        await vm.PreviewInvitationAsync(new PackReference("abc12345","ver12345",Hash),default);
        Assert.Equal("ver12345",content.Release!.Id);Assert.NotNull(vm.Preview);Assert.False(vm.CanInstall);
        Assert.False(Directory.Exists(Path.Combine(files.Root,"versions")));
    }
    [Fact]
    public async Task DirectoryChangedDuringInvitationDownloadCannotRestoreOldPreview()
    {
        using var files=new ModpackTestFiles();var content=new CaptureContent { Deferred=new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var vm=new ModpackViewModel(Catalog(Version()),content,new NoInstall(),new InstallationGate(),files.Root);
        var task=vm.PreviewInvitationAsync(new PackReference("abc12345","ver12345",Hash),default);
        Assert.True(vm.IsBusy);vm.SetDirectory(files.Root,false);
        content.Deferred.SetResult(new PackPreview("unused",Hash,new MrpackPlan("Fixture","v1","1.20.1","0.16.0",[],[])));
        await task;Assert.Null(vm.Preview);Assert.False(vm.CanInstall);Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task ArchiveMinecraftMustMatchSourceAndFailureDoesNotLeaveInstallablePreview()
    {
        using var files=new ModpackTestFiles();var content=new CaptureContent { MinecraftVersion="24w01a" };
        var vm=new ModpackViewModel(Catalog(Version()),content,new NoInstall(),new InstallationGate(),files.Root);
        await vm.PreviewInvitationAsync(new PackReference("abc12345","ver12345",Hash),default);
        Assert.Null(vm.Preview);Assert.False(vm.CanInstall);Assert.NotEmpty(vm.Error);
    }
    private sealed class CaptureContent:IModpackContentSource
    {
        public ModpackRelease? Release;
        public TaskCompletionSource<PackPreview>? Deferred;
        public string MinecraftVersion="1.20.1";
        public Task<PackPreview> DownloadArchiveAsync(ModpackRelease r,string path,CancellationToken t)
        {Release=r;return Deferred?.Task ?? Task.FromResult(new PackPreview(path,Hash,new MrpackPlan("Publisher pack","v1",MinecraftVersion,"0.16.0",[],[])){SourceProjectId=r.ProjectId,SourceVersionId=r.Id});}
        public Task<PackPreview> ReadAsync(string p,CancellationToken t)=>throw new NotSupportedException();
        public Task<IReadOnlyList<StagedFile>> StageAsync(PackPreview p,string d,IReadOnlySet<string> o,IProgress<InstallProgress> g,CancellationToken t)=>throw new NotSupportedException();
    }
    private sealed class NoInstall:IModpackInstallService
    {public Task InstallAsync(PackPreview p,string i,string d,IReadOnlySet<string> o,IProgress<InstallProgress> g,CancellationToken t)=>throw new Exception("Invitation must not install");}
}
