using System.Security.Cryptography;
using System.Text.Json;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Install;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Modpacks;

public sealed class ModpackInstallCoordinator(IFabricInstallSource fabric, IModpackContentSource content,
    InstallCommitter committer) : IModpackInstallService
{
    public async Task InstallAsync(PackPreview preview, string instanceId, string targetRoot,
        IReadOnlySet<string> optionalPaths, IProgress<InstallProgress> progress, CancellationToken token)
    {
        var root = GamePathValidator.ValidateRoot(targetRoot);
        var id = GamePathValidator.ValidateVersionId(instanceId);
        if (id.Equals(preview.Plan.MinecraftVersion, StringComparison.OrdinalIgnoreCase))
            throw new InstallException("请为整合包使用独立的实例名。");
        if (Path.Exists(Path.Combine(root, "versions", id))) throw new InstallException("该实例名已存在，请换一个名称。");
        var stageParent = Path.Combine(Path.GetDirectoryName(root)!, ".pcl-version-bridge-stage-" + Guid.NewGuid().ToString("N"));
        var stageRoot = Path.Combine(stageParent, ".minecraft");
        Directory.CreateDirectory(stageRoot);
        try
        {
            var current = await content.ReadAsync(preview.ArchivePath, token);
            if (current.ArchiveSha512 != preview.ArchiveSha512) throw new InstallException("整合包发生变化，请重新检查。");
            var staged = await fabric.StageAsync(current.Plan, id, stageRoot, progress, token);
            if (!Path.GetFullPath(staged.Parent.Root).Equals(stageRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(staged.Instance.Root).Equals(stageRoot, StringComparison.OrdinalIgnoreCase) ||
                staged.Parent.Version.Id != current.Plan.MinecraftVersion || staged.Instance.Version.Id != id)
                throw new InstallException("安装组件返回了意外的目录或版本。");
            var instanceRoot = Path.Combine(stageRoot, "versions", id);
            var packFiles = await content.StageAsync(preview, instanceRoot, optionalPaths, progress, token);
            var files = staged.Instance.Files.Concat(packFiles.Select(f => f with { RelativePath = Path.Combine("versions", id, f.RelativePath) })).ToList();
            async Task Generate(string relative, string text)
            {
                var full = Path.Combine(instanceRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await using (var output = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                    await output.WriteAsync(bytes, token);
                }
                await using var stream = File.OpenRead(full);
                files.Add(new StagedFile(Path.Combine("versions", id, relative), Convert.ToHexString(await SHA1.HashDataAsync(stream, token)), stream.Length));
            }
            await Generate("PCL/Setup.ini", "VersionArgumentIndie:1\r\nVersionArgumentIndieV2:True\r\n");
            await Generate(".vanillareach/source.json", JsonSerializer.Serialize(new { format = 1, preview.SourceProjectId, preview.SourceVersionId, preview.ArchiveSha512 }));
            var child = staged.Instance with { Files = files };
            await StagedVerifier.VerifyAsync(child, token);
            var parentPath = Path.Combine(root, "versions", staged.Parent.Version.Id);
            if (Path.Exists(parentPath))
            {
                try { await StagedVerifier.VerifyAsync(staged.Parent with { Root = root }, token); }
                catch (Exception ex) when (ex is InstallException or IOException or UnauthorizedAccessException)
                { throw new InstallException("已有原版父版本与官方文件不一致；为保护文件，请使用其他游戏目录。", ex); }
            }
            else await committer.CommitAsync(staged.Parent, root, token);
            progress.Report(new InstallProgress("写入整合包", 0, files.Count, null));
            await committer.CommitAsync(child, root, token);
            progress.Report(new InstallProgress("完成", 1, 1, null));
        }
        finally
        {
            var full = Path.GetFullPath(stageParent);
            if (Directory.Exists(full) && Path.GetDirectoryName(full) == Path.GetDirectoryName(root) &&
                Path.GetFileName(full).StartsWith(".pcl-version-bridge-stage-", StringComparison.Ordinal) &&
                (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(full, true);
        }
    }
}
