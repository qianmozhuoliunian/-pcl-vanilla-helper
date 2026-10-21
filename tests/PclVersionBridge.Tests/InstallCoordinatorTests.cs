using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Install;
using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Tests;

public sealed class InstallCoordinatorTests
{
    private const string AbcHash = "a9993e364706816aba3e25717850c26c9cd0d89d";
    private static readonly VersionChoice Version = new("26.3",
        new Uri("https://piston-meta.mojang.com/version"), AbcHash);

    [Fact]
    public async Task StagesInSiblingAndLeavesOnlyCompleteVersionInTarget()
    {
        using var temp = new TestRoot();
        var source = new FakeSource();
        var coordinator = new InstallCoordinator(source, new InstallCommitter());

        await coordinator.InstallAsync(Version, temp.GameRoot,
            new Progress<InstallProgress>(), CancellationToken.None);

        Assert.Equal("abc", await File.ReadAllTextAsync(Path.Combine(temp.GameRoot,
            "versions", "26.3", "26.3.json")));
        Assert.Equal("abc", await File.ReadAllTextAsync(Path.Combine(temp.GameRoot,
            "assets", "objects", "ab", "hash")));
        Assert.StartsWith(temp.Path, source.StagingRoot!);
        Assert.Empty(Directory.GetDirectories(temp.Path, ".pcl-version-bridge-stage-*"));
    }

    [Fact]
    public async Task CancellationCleansOnlyTemporaryStage()
    {
        using var temp = new TestRoot();
        var source = new FakeSource { ThrowCancellation = true };
        var coordinator = new InstallCoordinator(source, new InstallCommitter());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.InstallAsync(Version, temp.GameRoot,
                new Progress<InstallProgress>(), CancellationToken.None));

        Assert.Empty(Directory.GetDirectories(temp.Path, ".pcl-version-bridge-stage-*"));
        Assert.False(Directory.Exists(Path.Combine(temp.GameRoot, "versions")));
    }

    [Fact]
    public async Task InstallingIntoPopulatedDirectoryPreservesSaveConfigAndOtherVersion()
    {
        using var temp = new TestRoot();
        var existing = new[]
        {
            Path.Combine(temp.GameRoot, "saves", "world", "level.dat"),
            Path.Combine(temp.GameRoot, "options.txt"),
            Path.Combine(temp.GameRoot, "versions", "1.20.1", "1.20.1.json")
        };
        foreach (var path in existing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "old-user-data");
        }
        var before = existing.Select(path => File.ReadAllBytes(path)).ToArray();

        await new InstallCoordinator(new FakeSource(), new InstallCommitter()).InstallAsync(
            Version, temp.GameRoot, new Progress<InstallProgress>(), CancellationToken.None);

        for (var index = 0; index < existing.Length; index++)
            Assert.Equal(before[index], File.ReadAllBytes(existing[index]));
        Assert.True(File.Exists(Path.Combine(temp.GameRoot, "versions", "26.3", "26.3.json")));
    }

    private sealed class FakeSource : IInstallSource
    {
        public bool ThrowCancellation { get; init; }
        public string? StagingRoot { get; private set; }

        public async Task<StagedVersion> StageAsync(VersionChoice version, string stagingRoot,
            IProgress<InstallProgress> progress, CancellationToken cancellationToken)
        {
            StagingRoot = stagingRoot;
            var shared = Path.Combine(stagingRoot, "assets", "objects", "ab", "hash");
            var json = Path.Combine(stagingRoot, "versions", "26.3", "26.3.json");
            Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
            Directory.CreateDirectory(Path.GetDirectoryName(json)!);
            await File.WriteAllTextAsync(shared, "abc");
            await File.WriteAllTextAsync(json, "abc");
            if (ThrowCancellation) throw new OperationCanceledException();
            return new StagedVersion(version, stagingRoot,
                [new StagedFile(Path.Combine("assets", "objects", "ab", "hash"), AbcHash, 3),
                 new StagedFile(Path.Combine("versions", "26.3", "26.3.json"), AbcHash, 3)]);
        }
    }

    private sealed class TestRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pcl-bridge-coordinator-test-" + Guid.NewGuid().ToString("N"));
        public string GameRoot { get; }

        public TestRoot() => GameRoot = Directory.CreateDirectory(System.IO.Path.Combine(Path, ".minecraft")).FullName;

        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(Path);
            var temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(full).StartsWith("pcl-bridge-coordinator-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test cleanup path");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
}
