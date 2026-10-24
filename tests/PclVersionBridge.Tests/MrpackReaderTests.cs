using PclVersionBridge.Core;
using PclVersionBridge.Core.Modpacks;

namespace PclVersionBridge.Tests;

public sealed class MrpackReaderTests
{
    [Fact]
    public async Task ReadsPinnedDependenciesAndClientSelection()
    {
        using var files = new ModpackTestFiles();
        var path = files.Pack(ModpackTestFiles.Index([
            ModpackTestFiles.FileEntry(), ModpackTestFiles.FileEntry("mods/optional.jar", "optional"),
            ModpackTestFiles.FileEntry("mods/server.jar", "unsupported")]));
        var preview = await new MrpackReader().ReadAsync(path, default);
        Assert.Equal("1.20.1", preview.Plan.MinecraftVersion);
        Assert.Equal("0.16.0", preview.Plan.FabricVersion);
        Assert.Equal(2, preview.Plan.Files.Count);
        Assert.True(preview.Plan.Files.Single(f => f.Path == "mods/optional.jar").Optional);
        Assert.Equal(128, preview.ArchiveSha512.Length);
    }

    [Theory]
    [InlineData("../outside.jar")]
    [InlineData("mods/../../outside.jar")]
    [InlineData("C:/outside.jar")]
    [InlineData("mods/CON.jar")]
    [InlineData("PCL/Setup.ini")]
    [InlineData("assets/index.json")]
    [InlineData("launcher_accounts.json")]
    public async Task RejectsUnsafeOrLauncherOwnedPaths(string path)
    {
        using var files = new ModpackTestFiles();
        await Assert.ThrowsAsync<InstallException>(() => new MrpackReader().ReadAsync(
            files.Pack(ModpackTestFiles.Index([ModpackTestFiles.FileEntry(path)])), default));
    }

    [Theory]
    [InlineData("fabric-loader", "forge")]
    [InlineData("minecraft", "unknown-dependency")]
    [InlineData("sha512", "missing-sha512")]
    [InlineData("cdn.modrinth.com", "localhost")]
    public async Task RejectsUnsupportedDependenciesHashesAndHosts(string from, string to)
    {
        using var files = new ModpackTestFiles();
        var path = files.Pack(ModpackTestFiles.Index().Replace(from, to));
        await Assert.ThrowsAsync<InstallException>(() => new MrpackReader().ReadAsync(path, default));
    }

    [Fact]
    public async Task RejectsCaseCollisionsAndFileDirectoryConflicts()
    {
        using var files = new ModpackTestFiles();
        var reader = new MrpackReader();
        await Assert.ThrowsAsync<InstallException>(() => reader.ReadAsync(files.Pack(ModpackTestFiles.Index([
            ModpackTestFiles.FileEntry("mods/a.jar"), ModpackTestFiles.FileEntry("mods/A.jar")])), default));
        await Assert.ThrowsAsync<InstallException>(() => reader.ReadAsync(files.Pack(null,
            ("overrides/config/a", "x"), ("overrides/config/a/b", "y")), default));
    }

    [Fact]
    public async Task PreservesValidOverrideLayerOrderAndIgnoresServerLayer()
    {
        using var files = new ModpackTestFiles();
        var plan = (await new MrpackReader().ReadAsync(files.Pack(null,
            ("overrides/config/example.txt", "base"), ("client-overrides/config/example.txt", "client"),
            ("server-overrides/config/server.txt", "server")), default)).Plan;
        Assert.Equal(new[] { 0, 1 }, plan.Overrides.Select(o => o.Layer));
        Assert.All(plan.Overrides, o => Assert.Equal("config/example.txt", o.Path));
    }

    [Fact]
    public async Task RejectsOversizeIndexAndSymlinkEntry()
    {
        using var files = new ModpackTestFiles();
        var oversized = files.Pack(new string(' ', 8 * 1024 * 1024 + 1));
        await Assert.ThrowsAsync<InstallException>(() => new MrpackReader().ReadAsync(oversized, default));
        var linkPack = files.Pack();
        using (var zip = System.IO.Compression.ZipFile.Open(linkPack, System.IO.Compression.ZipArchiveMode.Update))
            zip.CreateEntry("overrides/link").ExternalAttributes = 0xA1FF << 16;
        await Assert.ThrowsAsync<InstallException>(() => new MrpackReader().ReadAsync(linkPack, default));
    }
}
