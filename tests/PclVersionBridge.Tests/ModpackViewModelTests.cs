using PclVersionBridge.Core;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Modpacks;
using PclVersionBridge.Core.Presentation;

namespace PclVersionBridge.Tests;

public sealed class ModpackViewModelTests
{
    [Fact]
    public async Task PreviewRequiresConfirmedDirectoryAndChangingItInvalidatesPreview()
    {
        using var files = new ModpackTestFiles();
        using var client = new HttpClient(new PackHttp(_ => throw new Exception("unexpected download")));
        var vm = new ModpackViewModel(new Catalog(), new ModpackContentSource(new ModpackNetwork(client)), new Installer(), new InstallationGate(), files.Root);
        Assert.False(vm.CanInstall);
        vm.SetDirectory(Directory.CreateDirectory(Path.Combine(files.Root,".minecraft")).FullName, true);
        await vm.ImportAsync(files.Pack());
        Assert.True(vm.CanInstall);
        vm.SetDirectory(vm.DirectoryPath, false);
        Assert.Null(vm.Preview);
        Assert.False(vm.CanInstall);
    }
    [Fact]
    public async Task SharedGateBlocksInstallAndCancelRestoresRetry()
    {
        using var files = new ModpackTestFiles();
        using var client = new HttpClient(new PackHttp(_ => throw new Exception("unexpected download")));
        var gate = new InstallationGate();
        var vm = new ModpackViewModel(new Catalog(), new ModpackContentSource(new ModpackNetwork(client)), new Installer(), gate, files.Root);
        vm.SetDirectory(Directory.CreateDirectory(Path.Combine(files.Root,".minecraft")).FullName,true);
        await vm.ImportAsync(files.Pack());
        using (gate.Acquire()) { Assert.False(vm.CanInstall); }
        Assert.True(vm.CanInstall);
        var task = vm.InstallAsync();
        Assert.True(gate.IsBusy);
        vm.Cancel();
        await task;
        Assert.False(gate.IsBusy);
        Assert.True(vm.CanInstall);
        Assert.Contains("取消",vm.Status);
    }
    [Fact]
    public async Task SearchEmptyAndFailureAreReadableAndRetryable()
    {
        using var files = new ModpackTestFiles();
        using var client = new HttpClient();
        var catalog = new Catalog();
        var vm = new ModpackViewModel(catalog,new ModpackContentSource(new ModpackNetwork(client)),new Installer(),new InstallationGate(),files.Root);
        await vm.SearchAsync();
        Assert.Contains("没有",vm.Status);
        catalog.Fail = true;
        await vm.SearchAsync();
        Assert.NotEmpty(vm.Error);
        Assert.DoesNotContain("private",vm.Error);
        Assert.False(vm.IsBusy);
        catalog.Fail = false;
        await vm.SearchAsync();
        Assert.Empty(vm.Error);
    }
    [Fact]
    public async Task SlowImportCannotRestorePreviewAfterDirectoryChanged()
    {
        using var files = new ModpackTestFiles();
        var preview = await new MrpackReader().ReadAsync(files.Pack(),default);
        var source = new DeferredContent();
        var vm = new ModpackViewModel(new Catalog(),source,new Installer(),new InstallationGate(),files.Root);
        vm.SetDirectory(files.Root,true);
        var pending = vm.ImportAsync("unused");
        vm.SetDirectory(files.Root,false);
        source.Result.SetResult(preview);
        await pending;
        Assert.Null(vm.Preview);
        Assert.False(vm.CanInstall);
    }
    private sealed class DeferredContent : IModpackContentSource
    {
        public TaskCompletionSource<PackPreview> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PackPreview> ReadAsync(string p,CancellationToken t) => Result.Task;
        public Task<PackPreview> DownloadArchiveAsync(ModpackRelease r,string p,CancellationToken t) => throw new NotSupportedException();
        public Task<IReadOnlyList<StagedFile>> StageAsync(PackPreview p,string d,IReadOnlySet<string> o,IProgress<InstallProgress> g,CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class Catalog : IModrinthCatalog
    {
        public bool Fail;
        public Task<ModpackSearchPage> SearchAsync(string q,string? game,int offset,CancellationToken token) =>
            Fail ? throw new IOException("C:/private/user/path") : Task.FromResult(new ModpackSearchPage(Array.Empty<ModpackProject>(),offset,0));
        public Task<IReadOnlyList<ModpackRelease>> GetVersionsAsync(string id,bool pre,CancellationToken token) => Task.FromResult<IReadOnlyList<ModpackRelease>>(Array.Empty<ModpackRelease>());
    }
    private sealed class Installer : IModpackInstallService
    {
        public Task InstallAsync(PackPreview p,string id,string root,IReadOnlySet<string> opt,IProgress<InstallProgress> progress,CancellationToken token) => Task.Delay(Timeout.Infinite,token);
    }
}
