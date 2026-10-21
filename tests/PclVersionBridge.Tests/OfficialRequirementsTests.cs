using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PclVersionBridge.Core;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Tests;

public sealed class OfficialRequirementsTests
{
    private const string Sha = "a9993e364706816aba3e25717850c26c9cd0d89d";

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void RejectsMissingClientJarOrAssetObject(bool includeClient, bool includeAsset)
    {
        using var root = new FixtureRoot();
        var files = root.RequiredFiles(includeClient, includeAsset);

        Assert.Throws<InstallException>(() =>
            OfficialRequirements.Validate(root.Stage(files)));
    }

    [Fact]
    public void AcceptsCompleteClientAndAssetSet()
    {
        using var root = new FixtureRoot();
        OfficialRequirements.Validate(root.Stage(root.RequiredFiles(true, true)));
    }

    [Fact]
    public void RejectsMissingWindowsLibraryButNotLinuxOnlyLibrary()
    {
        using var root = new FixtureRoot();
        Assert.Throws<InstallException>(() =>
            OfficialRequirements.Validate(root.Stage(root.RequiredFiles(true, true, false))));
        OfficialRequirements.Validate(root.Stage(root.RequiredFiles(true, true, true)));
    }

    private sealed class FixtureRoot : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(),
            "pcl-bridge-requirements-test-" + Guid.NewGuid().ToString("N"));
        public string Root { get; }

        public FixtureRoot()
        {
            Root = Directory.CreateDirectory(Path.Combine(_parent, ".minecraft")).FullName;
            var versionPath = Path.Combine(Root, "versions", "26.3", "26.3.json");
            var indexPath = Path.Combine(Root, "assets", "indexes", "test.json");
            Directory.CreateDirectory(Path.GetDirectoryName(versionPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            var libraries = new[]
            {
                new { name = "com.example:win:1", rules = new[] { new { action = "allow", os = new { name = "windows" } } },
                    downloads = new { artifact = new { path = "com/example/win/1/win-1.jar", sha1 = Sha, size = 3 } } },
                new { name = "com.example:linux:1", rules = new[] { new { action = "allow", os = new { name = "linux" } } },
                    downloads = new { artifact = new { path = "com/example/linux/1/linux-1.jar", sha1 = Sha, size = 3 } } }
            };
            File.WriteAllText(versionPath, JsonSerializer.Serialize(new
            {
                assetIndex = new { id = "test", sha1 = Sha, size = 3 },
                downloads = new { client = new { sha1 = Sha, size = 3 } },
                libraries
            }));
            File.WriteAllText(indexPath,
                "{\"objects\":{\"test\":{\"hash\":\"" + Sha + "\",\"size\":3}}}");
        }

        public IReadOnlyList<StagedFile> RequiredFiles(bool client, bool asset, bool library = true)
        {
            var files = new List<StagedFile>
            {
                new(Path.Combine("versions", "26.3", "26.3.json"), Sha, 3),
                new(Path.Combine("assets", "indexes", "test.json"), Sha, 3)
            };
            if (client) files.Add(new StagedFile(Path.Combine("versions", "26.3", "26.3.jar"), Sha, 3));
            if (asset) files.Add(new StagedFile(Path.Combine("assets", "objects", Sha[..2], Sha), Sha, 3));
            if (library) files.Add(new StagedFile(Path.Combine("libraries", "com", "example", "win", "1", "win-1.jar"), Sha, 3));
            return files;
        }

        public StagedVersion Stage(IReadOnlyList<StagedFile> files) => new(
            new VersionChoice("26.3", new Uri("https://piston-meta.mojang.com/26.3.json"), Sha),
            Root, files);

        public void Dispose()
        {
            var full = Path.GetFullPath(_parent);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("pcl-bridge-requirements-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected cleanup path");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }
    }
}
