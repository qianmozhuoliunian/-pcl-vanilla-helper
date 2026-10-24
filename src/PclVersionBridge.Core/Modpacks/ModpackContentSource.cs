using System.IO.Compression;
using System.Security.Cryptography;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Modpacks;

public interface IModpackContentSource
{
    Task<PackPreview> ReadAsync(string path, CancellationToken token);
    Task<PackPreview> DownloadArchiveAsync(ModpackRelease release, string destination, CancellationToken token);
    Task<IReadOnlyList<StagedFile>> StageAsync(PackPreview preview, string instanceDirectory,
        IReadOnlySet<string> optionalPaths, IProgress<InstallProgress> progress, CancellationToken token);
}

public sealed class ModpackContentSource(ModpackNetwork network) : IModpackContentSource
{
    public Task<PackPreview> ReadAsync(string path, CancellationToken token) => new MrpackReader().ReadAsync(path, token);
    public async Task<PackPreview> DownloadArchiveAsync(ModpackRelease release, string destination, CancellationToken token)
    {
        ModpackRules.ContentUri(release.FileUrl.AbsoluteUri);
        if (release.FileSize > ModpackRules.ArchiveLimit) throw new InstallException("整合包文件超过 512 MiB 上限。");
        await network.DownloadAsync(release.FileUrl, destination, release.FileSize, release.Sha1, release.Sha512, token);
        var preview = await ReadAsync(destination, token);
        if (!release.GameVersions.Contains(preview.Plan.MinecraftVersion))
            throw new InstallException("包索引的 Minecraft 版本与来源信息不一致。");
        return preview with { SourceProjectId = release.ProjectId, SourceVersionId = release.Id };
    }
    public async Task<IReadOnlyList<StagedFile>> StageAsync(PackPreview preview, string instanceDirectory,
        IReadOnlySet<string> optionalPaths, IProgress<InstallProgress> progress, CancellationToken token)
    {
        var current = await ReadAsync(preview.ArchivePath, token);
        if (!current.ArchiveSha512.Equals(preview.ArchiveSha512, StringComparison.OrdinalIgnoreCase))
            throw new InstallException("整合包文件在预览后发生了变化，请重新检查。");
        // Keep the verified archive locked throughout extraction on Windows.
        await using var input = new FileStream(current.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!Convert.ToHexString(await SHA512.HashDataAsync(input, token)).Equals(preview.ArchiveSha512, StringComparison.OrdinalIgnoreCase))
            throw new InstallException("整合包文件发生了变化，请重新检查。");
        input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var plan = current.Plan;
        var optional = plan.Files.Where(f => f.Optional).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        if (optionalPaths.Any(path => !optional.Contains(path))) throw new InstallException("可选文件选择已失效，请重新检查。");
        var root = Path.GetFullPath(instanceDirectory);
        GamePathValidator.RejectReparsePoints(root);
        Directory.CreateDirectory(root);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = plan.Files.Where(f => !f.Optional || optionalPaths.Contains(f.Path)).ToArray();
        var completed = 0;
        foreach (var file in selected)
        {
            token.ThrowIfCancellationRequested();
            var destination = Destination(root, file.Path);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new InstallException("整合包试图替换安装器生成的文件。");
            progress.Report(new InstallProgress("下载模组", completed, selected.Length, file.Path));
            await network.DownloadAsync(file.Downloads[0], destination, file.Size, file.Sha1, file.Sha512, token);
            owned.Add(file.Path);
            completed++;
        }
        foreach (var overlay in plan.Overrides)
        {
            token.ThrowIfCancellationRequested();
            var destination = Destination(root, overlay.Path);
            if ((File.Exists(destination) && !owned.Contains(overlay.Path)) || Directory.Exists(destination))
                throw new InstallException("整合包试图替换安装器生成的文件。");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            GamePathValidator.RejectReparsePoints(Path.GetDirectoryName(destination)!);
            var entry = zip.GetEntry(overlay.EntryName) ?? throw new InstallException("整合包覆盖文件缺失。");
            await using (var source = entry.Open())
            await using (var output = new FileStream(destination,
                owned.Contains(overlay.Path) ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await MrpackReader.CopyBoundedAsync(source, output, overlay.Size, token);
            if (new FileInfo(destination).Length != overlay.Size) throw new InstallException("整合包覆盖文件大小不一致。");
            owned.Add(overlay.Path);
        }
        var result = new List<StagedFile>();
        foreach (var path in owned)
        {
            var full = Destination(root, path);
            await using var stream = File.OpenRead(full);
            result.Add(new StagedFile(path, Convert.ToHexString(await SHA1.HashDataAsync(stream, token)), stream.Length));
        }
        return result;
    }
    private static string Destination(string root, string relative)
    {
        var path = Path.Combine(root, ModpackRules.ContentPath(relative));
        GamePathValidator.RejectReparsePoints(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InstallException("整合包目标文件是链接。");
        return path;
    }
}
