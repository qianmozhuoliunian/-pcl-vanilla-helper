using System.Security.Cryptography;
using System.Text;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Install;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.Tests;

public sealed class ModpackInstallCoordinatorTests
{
    [Fact]
    public async Task CommitsIndependentInstanceAndReusesVerifiedParent()
    {
        using var files = new ModpackTestFiles();
        var root = Directory.CreateDirectory(Path.Combine(files.Root, ".minecraft")).FullName;
        var preview = await new MrpackReader().ReadAsync(files.Pack(), default);
        using var client = ContentClient();
        var source = new FakeFabric();
        var installer = new ModpackInstallCoordinator(source, new ModpackContentSource(new ModpackNetwork(client)), new InstallCommitter());
        await installer.InstallAsync(preview, "pack-one", root, new HashSet<string>(), new Progress<InstallProgress>(), default);
        await installer.InstallAsync(preview, "pack-two", root, new HashSet<string>(), new Progress<InstallProgress>(), default);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(root, "versions", "pack-two", "mods", "example.jar")));
        Assert.Contains("VersionArgumentIndie:1", await File.ReadAllTextAsync(Path.Combine(root, "versions", "pack-one", "PCL", "Setup.ini")));
        Assert.False(Directory.Exists(Path.Combine(root, "mods")));
        Assert.True(File.Exists(Path.Combine(root, "versions", "1.20.1", "1.20.1.json")));
    }
    [Fact]
    public async Task ParentConflictPreservesUserDataAndDoesNotCommitPack()
    {
        using var files = new ModpackTestFiles();
        var root = Directory.CreateDirectory(Path.Combine(files.Root, ".minecraft")).FullName;
        var parent = Directory.CreateDirectory(Path.Combine(root, "versions", "1.20.1")).FullName;
        await File.WriteAllTextAsync(Path.Combine(parent, "1.20.1.json"), "user config");
        var preview = await new MrpackReader().ReadAsync(files.Pack(), default);
        using var client = ContentClient();
        var installer = new ModpackInstallCoordinator(new FakeFabric(), new ModpackContentSource(new ModpackNetwork(client)), new InstallCommitter());
        await Assert.ThrowsAsync<InstallException>(() => installer.InstallAsync(preview, "pack", root,
            new HashSet<string>(), new Progress<InstallProgress>(), default));
        Assert.Equal("user config", await File.ReadAllTextAsync(Path.Combine(parent, "1.20.1.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "versions", "pack")));
        Assert.Empty(Directory.GetDirectories(files.Root, ".pcl-version-bridge-stage-*"));
    }
    [Fact]
    public async Task CancelBeforeCommitLeavesNoPackAndKeepsExistingSave()
    {
        using var files = new ModpackTestFiles();
        var root = Directory.CreateDirectory(Path.Combine(files.Root, ".minecraft")).FullName;
        var saves = Directory.CreateDirectory(Path.Combine(root, "saves")).FullName;
        await File.WriteAllTextAsync(Path.Combine(saves, "world"), "valuable");
        var preview = await new MrpackReader().ReadAsync(files.Pack(), default);
        using var client = ContentClient();
        using var cancel = new CancellationTokenSource();
        var committer = new InstallCommitter(afterPreflight: cancel.Cancel);
        var installer = new ModpackInstallCoordinator(new FakeFabric(), new ModpackContentSource(new ModpackNetwork(client)), committer);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(preview, "pack", root,
            new HashSet<string>(), new Progress<InstallProgress>(), cancel.Token));
        Assert.False(Directory.Exists(Path.Combine(root, "versions", "pack")));
        Assert.Equal("valuable", await File.ReadAllTextAsync(Path.Combine(saves, "world")));
    }
    private static HttpClient ContentClient() => new(new PackHttp(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
    { Content = new ByteArrayContent(ModpackTestFiles.Payload) }));
    private sealed class FakeFabric : IFabricInstallSource
    {
        public async Task<FabricStaging> StageAsync(MrpackPlan plan, string id, string root, IProgress<InstallProgress> progress, CancellationToken token)
        {
            async Task<StagedFile> File(string relative, string text)
            {
                var path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await System.IO.File.WriteAllTextAsync(path, text, token);
                var bytes = await System.IO.File.ReadAllBytesAsync(path, token);
                return new StagedFile(relative, Convert.ToHexString(SHA1.HashData(bytes)), bytes.Length);
            }
            var choice = new VersionChoice(plan.MinecraftVersion, new Uri("https://piston-meta.mojang.com/base.json"), new string('0', 40));
            var parent = await File($"versions/{plan.MinecraftVersion}/{plan.MinecraftVersion}.json", "parent");
            var child = await File($"versions/{id}/{id}.json", "{\"id\":\"" + id + "\",\"inheritsFrom\":\"" + plan.MinecraftVersion + "\"}");
            return new FabricStaging(new StagedVersion(choice, root, new[] { parent }),
                new StagedVersion(choice with { Id = id }, root, new[] { child }));
        }
    }
}
