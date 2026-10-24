using System.Security.Cryptography;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.FileExtractors;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Download;

public sealed class CmlInstallSource(HttpClient client) : IInstallSource
{
    public async Task<StagedVersion> StageAsync(VersionChoice version, string stagingRoot,
        IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        GamePathValidator.ValidateVersionId(version.Id);
        OfficialHosts.ValidateUri(version.ManifestUrl);
        var root = Path.GetFullPath(stagingRoot);
        Directory.CreateDirectory(root);
        GamePathValidator.RejectReparsePoints(root);

        var parameters = MinecraftLauncherParameters.CreateDefault(new MinecraftPath(root), client);
        var extractors = DefaultFileExtractors.CreateDefault(client,
            parameters.RulesEvaluator!, parameters.JavaPathResolver!);
        extractors.Java = null;
        parameters.FileExtractors = extractors.ToExtractorCollection();
        var launcher = new MinecraftLauncher(parameters);
        launcher.FileProgressChanged += (_, args) => progress.Report(new InstallProgress(
            "下载", args.ProgressedTasks, args.TotalTasks, args.Name));

        progress.Report(new InstallProgress("获取清单", 0, 0, null));
        var parsedVersion = await launcher.GetVersionAsync(version.Id, cancellationToken);
        var versionRelative = Path.Combine("versions", version.Id, version.Id + ".json");
        var versionPath = Path.Combine(root, versionRelative);
        await VerifyFileHashAsync(versionPath, version.ManifestSha1, cancellationToken);

        var indexFile = ReadAssetIndex(versionPath);
        var extracted = (await launcher.ExtractFiles(parsedVersion, cancellationToken)).ToArray();
        await VerifyFileHashAsync(Path.Combine(root, indexFile.RelativePath), indexFile.Sha1,
            cancellationToken);

        var files = new List<StagedFile>
        {
            new(versionRelative, version.ManifestSha1, new FileInfo(versionPath).Length),
            indexFile
        };
        foreach (var file in extracted)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.Hash) ||
                !Uri.TryCreate(file.Url, UriKind.Absolute, out var uri))
                throw new InstallException("官方文件清单缺少路径、下载地址或校验值。");
            OfficialHosts.ValidateUri(uri);
            var relative = GamePathValidator.ValidateRelative(Path.GetRelativePath(root, file.Path));
            files.Add(new StagedFile(relative, file.Hash, file.Size));
        }
        var staged = new StagedVersion(version, root, StagedFileSet.Merge(files));
        OfficialRequirements.Validate(staged);

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await launcher.InstallAsync(parsedVersion, cancellationToken);
                break;
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                progress.Report(new InstallProgress("网络中断，重试", attempt, 3, null));
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new InstallProgress("校验", 0, files.Count, null));
        await StagedVerifier.VerifyAsync(staged, cancellationToken);
        return staged;
    }

    private static StagedFile ReadAssetIndex(string versionPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(versionPath));
            var index = document.RootElement.GetProperty("assetIndex");
            var id = GamePathValidator.ValidateVersionId(index.GetProperty("id").GetString() ?? "");
            var sha1 = index.GetProperty("sha1").GetString() ?? "";
            var size = index.GetProperty("size").GetInt64();
            var url = new Uri(index.GetProperty("url").GetString() ?? "", UriKind.Absolute);
            OfficialHosts.ValidateUri(url);
            return new StagedFile(Path.Combine("assets", "indexes", id + ".json"), sha1, size);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or
            InvalidOperationException or UriFormatException)
        {
            throw new InstallException("版本元数据中的资源索引无效。", ex);
        }
    }

    private static async Task VerifyFileHashAsync(string path, string expected,
        CancellationToken cancellationToken)
    {
        if (expected.Length != 40 || !expected.All(Uri.IsHexDigit) || !File.Exists(path))
            throw new InstallException("版本元数据缺失或没有有效校验值。");
        await using var file = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA1.HashDataAsync(file, cancellationToken));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InstallException("版本元数据校验失败。");
    }
}
