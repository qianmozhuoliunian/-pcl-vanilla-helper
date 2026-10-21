using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Install;
using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Tests;

public sealed class InstallCommitterTests
{
    [Fact]
    public async Task ReusesMatchingSharedFileAndCommitsVersionLast()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");
        var shared = dirs.Target("assets", "objects", "ab", "hash");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllTextAsync(shared, "abc");

        await new InstallCommitter().CommitAsync(staged, dirs.TargetRoot, CancellationToken.None);

        Assert.Equal("abc", await File.ReadAllTextAsync(shared));
        Assert.True(File.Exists(dirs.Target("versions", "26.3", "26.3.json")));
    }

    [Fact]
    public async Task ConflictLeavesExistingBytesAndVersionUntouched()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");
        var shared = dirs.Target("assets", "objects", "ab", "hash");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllTextAsync(shared, "user-data");

        await Assert.ThrowsAsync<InstallException>(() =>
            new InstallCommitter().CommitAsync(staged, dirs.TargetRoot, CancellationToken.None));

        Assert.Equal("user-data", await File.ReadAllTextAsync(shared));
        Assert.False(Directory.Exists(dirs.Target("versions", "26.3")));
    }

    [Fact]
    public async Task RechecksFileChangedAfterPreflight()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");
        var shared = dirs.Target("assets", "objects", "ab", "hash");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        await File.WriteAllTextAsync(shared, "abc");
        var committer = new InstallCommitter(afterPreflight: () => File.WriteAllText(shared, "changed"));

        await Assert.ThrowsAsync<InstallException>(() =>
            committer.CommitAsync(staged, dirs.TargetRoot, CancellationToken.None));

        Assert.Equal("changed", await File.ReadAllTextAsync(shared));
        Assert.False(Directory.Exists(dirs.Target("versions", "26.3")));
    }

    [Fact]
    public async Task JunctionAddedAfterPreflightCannotCreateOutsideDirectories()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");
        var outside = Directory.CreateDirectory(Path.Combine(
            Path.GetDirectoryName(dirs.TargetRoot)!, "outside")).FullName;
        var link = dirs.Target("assets");
        var committer = new InstallCommitter(afterPreflight: () =>
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c mklink /J \"{link}\" \"{outside}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            Assert.NotNull(process);
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        });

        try
        {
            await Assert.ThrowsAsync<InstallException>(() =>
                committer.CommitAsync(staged, dirs.TargetRoot, CancellationToken.None));
            Assert.False(Directory.Exists(Path.Combine(outside, "objects")));
            Assert.False(Directory.Exists(dirs.Target("versions", "26.3")));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
        }
    }

    [Fact]
    public async Task CancelAfterSharedMergeLeavesNoVisibleVersion()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");
        using var cancellation = new CancellationTokenSource();
        var committer = new InstallCommitter(afterSharedFile: _ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            committer.CommitAsync(staged, dirs.TargetRoot, cancellation.Token));

        Assert.True(File.Exists(dirs.Target("assets", "objects", "ab", "hash")));
        Assert.False(Directory.Exists(dirs.Target("versions", "26.3")));
    }

    [Fact]
    public async Task RejectsInsufficientSpaceBeforeAnyWrite()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");

        await Assert.ThrowsAsync<InstallException>(() =>
            new InstallCommitter(availableBytes: _ => 0)
                .CommitAsync(staged, dirs.TargetRoot, CancellationToken.None));

        Assert.False(Directory.Exists(dirs.Target("assets")));
    }

    [Fact]
    public async Task RejectsExistingVersionBeforeSharedWrites()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");
        Directory.CreateDirectory(dirs.Target("versions", "26.3"));

        await Assert.ThrowsAsync<InstallException>(() =>
            new InstallCommitter().CommitAsync(staged, dirs.TargetRoot, CancellationToken.None));

        Assert.False(Directory.Exists(dirs.Target("assets")));
    }

    [Fact]
    public async Task ReportsBlockedFilePathWithoutCommittingVersion()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");
        Directory.CreateDirectory(dirs.Target("assets", "objects", "ab", "hash"));

        await Assert.ThrowsAsync<InstallException>(() =>
            new InstallCommitter().CommitAsync(staged, dirs.TargetRoot, CancellationToken.None));

        Assert.False(Directory.Exists(dirs.Target("versions", "26.3")));
    }

    [Fact]
    public async Task RejectsUnverifiedJunctionInsideStagedVersion()
    {
        using var dirs = new InstallDirectories();
        var staged = dirs.Stage("abc");
        var outside = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(staged.Root)!, "outside")).FullName;
        var link = Path.Combine(staged.Root, "versions", "26.3", "unexpected-link");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{link}\" \"{outside}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
        Assert.NotNull(process);
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        try
        {
            await Assert.ThrowsAsync<InstallException>(() =>
                new InstallCommitter().CommitAsync(staged, dirs.TargetRoot, CancellationToken.None));
            Assert.False(Directory.Exists(dirs.Target("versions", "26.3")));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            var movedLink = dirs.Target("versions", "26.3", "unexpected-link");
            if (Directory.Exists(movedLink)) Directory.Delete(movedLink);
        }
    }

    private sealed class InstallDirectories : IDisposable
    {
        private readonly string _base = Path.Combine(Path.GetTempPath(),
            "pcl-bridge-commit-test-" + Guid.NewGuid().ToString("N"));
        public string TargetRoot { get; }
        private string StagingRoot { get; }

        public InstallDirectories()
        {
            TargetRoot = Directory.CreateDirectory(Path.Combine(_base, "target", ".minecraft")).FullName;
            StagingRoot = Directory.CreateDirectory(Path.Combine(_base, "stage", ".minecraft")).FullName;
        }

        public string Target(params string[] segments) => Path.Combine([TargetRoot, .. segments]);

        public StagedVersion Stage(string contents)
        {
            var shared = Path.Combine(StagingRoot, "assets", "objects", "ab", "hash");
            var version = Path.Combine(StagingRoot, "versions", "26.3", "26.3.json");
            Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
            Directory.CreateDirectory(Path.GetDirectoryName(version)!);
            File.WriteAllText(shared, contents);
            File.WriteAllText(version, contents);
            var sha = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(contents))).ToLowerInvariant();
            return new StagedVersion(
                new VersionChoice("26.3", new Uri("https://piston-meta.mojang.com/version"), sha),
                StagingRoot,
                [new StagedFile(Path.Combine("assets", "objects", "ab", "hash"), sha, contents.Length),
                 new StagedFile(Path.Combine("versions", "26.3", "26.3.json"), sha, contents.Length)]);
        }

        public void Dispose()
        {
            var full = Path.GetFullPath(_base);
            var temp = Path.GetFullPath(Path.GetTempPath());
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("pcl-bridge-commit-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test cleanup path");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
}
