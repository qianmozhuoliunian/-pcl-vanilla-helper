using PclVersionBridge.Core;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Tests;

public sealed class InstallSourceTests
{
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";

    [Fact]
    public async Task VerifiesRealStagedBytes()
    {
        var root = NewTempRoot();
        try
        {
            var file = Path.Combine(root, "assets", "objects", "ab", "hash");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "abc");
            var staged = OneFile(root, "assets/objects/ab/hash", AbcSha1, 3);

            await StagedVerifier.VerifyAsync(staged, CancellationToken.None);
            await File.WriteAllTextAsync(file, "abd");
            await Assert.ThrowsAsync<InstallException>(() =>
                StagedVerifier.VerifyAsync(staged, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsMissingHashBeforeCommit()
    {
        var root = NewTempRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "file"), "abc");
            await Assert.ThrowsAsync<InstallException>(() =>
                StagedVerifier.VerifyAsync(OneFile(root, "file", "", 3), CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsTraversalEvenWhenHashIsValid()
    {
        var root = NewTempRoot();
        try
        {
            await Assert.ThrowsAsync<InstallException>(() =>
                StagedVerifier.VerifyAsync(OneFile(root, "../outside", AbcSha1, 3), CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CancelledVerificationStops()
    {
        var root = NewTempRoot();
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                StagedVerifier.VerifyAsync(OneFile(root, "file", AbcSha1, 3), cancellation.Token));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static StagedVersion OneFile(string root, string relative, string hash, long size) =>
        new(new VersionChoice("26.3", new Uri("https://piston-meta.mojang.com/26.3.json"), AbcSha1),
            root, [new StagedFile(relative, hash, size)]);

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcl-bridge-source-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
