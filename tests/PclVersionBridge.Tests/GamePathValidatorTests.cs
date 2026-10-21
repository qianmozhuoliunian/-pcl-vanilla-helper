using PclVersionBridge.Core;
using PclVersionBridge.Core.Paths;
using System.Diagnostics;

namespace PclVersionBridge.Tests;

public sealed class GamePathValidatorTests
{
    [Fact]
    public void AcceptsExistingMinecraftDirectory()
    {
        using var temp = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(temp.Path, ".minecraft")).FullName;

        Assert.Equal(root, GamePathValidator.ValidateRoot(root));
    }

    [Fact]
    public void RejectsAnotherDirectoryName()
    {
        using var temp = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(temp.Path, "other")).FullName;

        Assert.Throws<InstallException>(() => GamePathValidator.ValidateRoot(root));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("versions/../../outside")]
    [InlineData("C:\\outside")]
    [InlineData("\\\\server\\share")]
    [InlineData("assets/objects/a:b")]
    [InlineData("assets/./object")]
    public void RejectsUnsafeRelativePaths(string relative)
    {
        Assert.Throws<InstallException>(() => GamePathValidator.ValidateRelative(relative));
    }

    [Fact]
    public void AcceptsOrdinaryRelativePath()
    {
        Assert.Equal(Path.Combine("assets", "objects", "ab", "hash"),
            GamePathValidator.ValidateRelative("assets/objects/ab/hash"));
    }

    [Fact]
    public void RejectsDirectorySymlink()
    {
        using var temp = new TemporaryDirectory();
        var target = Directory.CreateDirectory(Path.Combine(temp.Path, "real", ".minecraft")).FullName;
        var link = Path.Combine(temp.Path, ".minecraft");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
        Assert.NotNull(process);
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        Assert.Throws<InstallException>(() => GamePathValidator.ValidateRoot(link));
        Directory.Delete(link);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pcl-bridge-tests-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(Path);
            var temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(full).StartsWith("pcl-bridge-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test cleanup path");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
}
