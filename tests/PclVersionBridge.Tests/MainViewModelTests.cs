using PclVersionBridge.Core;
using PclVersionBridge.Core.Catalog;
using PclVersionBridge.Core.Install;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Presentation;

namespace PclVersionBridge.Tests;

public sealed class MainViewModelTests
{
    private static readonly VersionChoice Version = new("26.3",
        new Uri("https://piston-meta.mojang.com/26.3.json"),
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    [Fact]
    public async Task SuggestedDirectoryStillNeedsConfirmation()
    {
        using var root = new TestRoot();
        var installer = new FakeInstaller();
        var viewModel = new MainViewModel(new FakeCatalog([Version]), installer, root.GameRoot);
        await viewModel.LoadVersionsAsync();

        Assert.Equal(root.GameRoot, viewModel.DirectoryPath);
        Assert.False(viewModel.CanInstall);
        viewModel.ConfirmDirectory();
        Assert.True(viewModel.CanInstall);

        viewModel.DirectoryPath = root.GameRoot + "-changed";
        Assert.False(viewModel.CanInstall);
    }

    [Fact]
    public async Task OfflineCatalogShowsErrorAndRetryLoadsVersions()
    {
        var catalog = new FakeCatalog([Version]) { FailNext = true };
        var viewModel = new MainViewModel(catalog, new FakeInstaller());

        await viewModel.LoadVersionsAsync();
        Assert.NotEmpty(viewModel.Error);
        Assert.Empty(viewModel.Versions);

        await viewModel.LoadVersionsAsync();
        Assert.Empty(viewModel.Error);
        Assert.Equal("26.3", Assert.Single(viewModel.Versions).Id);
    }

    [Fact]
    public async Task SearchFiltersHistoricalReleasesAndSelectsFirstMatch()
    {
        var older = Version with { Id = "1.20.1" };
        var viewModel = new MainViewModel(new FakeCatalog([Version, older]), new FakeInstaller());
        await viewModel.LoadVersionsAsync();

        viewModel.SearchText = "1.20";
        Assert.Equal("1.20.1", Assert.Single(viewModel.FilteredVersions).Id);
        Assert.Equal("1.20.1", viewModel.SelectedVersion?.Id);

        viewModel.SearchText = "missing";
        Assert.Empty(viewModel.FilteredVersions);
        Assert.Null(viewModel.SelectedVersion);
    }

    [Fact]
    public async Task InstallReportsProgressAndCompletes()
    {
        using var root = new TestRoot();
        var installer = new FakeInstaller();
        var viewModel = new MainViewModel(new FakeCatalog([Version]), installer, root.GameRoot);
        await viewModel.LoadVersionsAsync();
        viewModel.ConfirmDirectory();

        await viewModel.InstallAsync();

        Assert.Equal(root.GameRoot, installer.LastTarget);
        Assert.Equal("安装完成。回到 PCL 选择 26.3。", viewModel.Status);
        Assert.Empty(viewModel.Error);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task CancelStopsInFlightInstall()
    {
        using var root = new TestRoot();
        var installer = new FakeInstaller { WaitForCancellation = true };
        var viewModel = new MainViewModel(new FakeCatalog([Version]), installer, root.GameRoot);
        await viewModel.LoadVersionsAsync();
        viewModel.ConfirmDirectory();
        Assert.False(viewModel.IsInstalling);

        var running = viewModel.InstallAsync();
        await installer.Started.Task;
        Assert.True(viewModel.IsInstalling);
        viewModel.Cancel();
        await running;

        Assert.Equal("安装已取消。", viewModel.Status);
        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.IsBusy);
    }

    private sealed class FakeCatalog(IReadOnlyList<VersionChoice> versions) : IVersionCatalog
    {
        public bool FailNext { get; set; }

        public Task<IReadOnlyList<VersionChoice>> ListReleasesAsync(CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InstallException("网络不可用");
            }
            return Task.FromResult(versions);
        }
    }

    private sealed class FakeInstaller : IInstallService
    {
        public string? LastTarget { get; private set; }
        public bool WaitForCancellation { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InstallAsync(VersionChoice version, string targetRoot,
            IProgress<InstallProgress> progress, CancellationToken cancellationToken)
        {
            LastTarget = targetRoot;
            progress.Report(new InstallProgress("下载", 1, 2, "client.jar"));
            Started.SetResult();
            if (WaitForCancellation)
                await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class TestRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pcl-bridge-vm-test-" + Guid.NewGuid().ToString("N"));
        public string GameRoot { get; }

        public TestRoot() => GameRoot = Directory.CreateDirectory(System.IO.Path.Combine(Path, ".minecraft")).FullName;

        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(Path);
            var temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(full).StartsWith("pcl-bridge-vm-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test cleanup path");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
}
