using System.Net;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Modpacks;
using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Tests;

public sealed class ModpackContentSourceTests
{
    private static IProgress<InstallProgress> Progress => new Progress<InstallProgress>();
    [Fact]
    public async Task DownloadsRequiredFilesAndAppliesClientOverrides()
    {
        using var files = new ModpackTestFiles();
        var pack = files.Pack(ModpackTestFiles.Index([ModpackTestFiles.FileEntry(),
            ModpackTestFiles.FileEntry("mods/optional.jar", "optional")]),
            ("overrides/config/test.txt", "base"), ("client-overrides/config/test.txt", "client"));
        var preview = await new MrpackReader().ReadAsync(pack, default);
        using var client = Client();
        var destination = Path.Combine(files.Root, "instance");
        var staged = await new ModpackContentSource(new ModpackNetwork(client)).StageAsync(preview,
            destination, new HashSet<string>(), Progress, default);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(destination, "mods", "example.jar")));
        Assert.Equal("client", await File.ReadAllTextAsync(Path.Combine(destination, "config", "test.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "mods", "optional.jar")));
        Assert.Equal(2, staged.Count);
    }
    [Fact]
    public async Task ChangedArchiveCannotBeInstalledUsingOldPreview()
    {
        using var files = new ModpackTestFiles();
        var preview = await new MrpackReader().ReadAsync(files.Pack(), default);
        await File.AppendAllTextAsync(preview.ArchivePath, "changed");
        using var client = Client();
        var destination = Path.Combine(files.Root, "instance");
        await Assert.ThrowsAsync<InstallException>(() => new ModpackContentSource(new ModpackNetwork(client))
            .StageAsync(preview, destination, new HashSet<string>(), Progress, default));
        Assert.False(Directory.Exists(destination));
    }
    [Fact]
    public async Task PackCannotOverwriteAnExistingGeneratedLaunchFile()
    {
        using var files = new ModpackTestFiles();
        var preview = await new MrpackReader().ReadAsync(files.Pack(null, ("overrides/instance.json", "bad")), default);
        var destination = Directory.CreateDirectory(Path.Combine(files.Root, "instance")).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "instance.json"), "generated");
        using var client = Client();
        await Assert.ThrowsAsync<InstallException>(() => new ModpackContentSource(new ModpackNetwork(client))
            .StageAsync(preview, destination, new HashSet<string>(), Progress, default));
        Assert.Equal("generated", await File.ReadAllTextAsync(Path.Combine(destination, "instance.json")));
    }
    private static HttpClient Client() => new(new PackHttp(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(ModpackTestFiles.Payload) }));
}
