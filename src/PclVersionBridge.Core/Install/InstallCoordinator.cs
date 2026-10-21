using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Install;

public sealed class InstallCoordinator(IInstallSource source, InstallCommitter committer) : IInstallService
{
    public async Task InstallAsync(VersionChoice version, string targetRoot,
        IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        var root = GamePathValidator.ValidateRoot(targetRoot);
        var id = GamePathValidator.ValidateVersionId(version.Id);
        var targetVersion = Path.Combine(root, "versions", id);
        if (Directory.Exists(targetVersion) || File.Exists(targetVersion))
            throw new InstallException("该版本已存在。为保护已有文件，请先在 PCL 中检查它。");

        var parent = Path.GetDirectoryName(root)!;
        var stageParent = Path.Combine(parent,
            ".pcl-version-bridge-stage-" + Guid.NewGuid().ToString("N"));
        var stagingRoot = Path.Combine(stageParent, ".minecraft");
        Directory.CreateDirectory(stagingRoot);
        try
        {
            progress.Report(new InstallProgress("获取清单", 0, 0, null));
            var staged = await source.StageAsync(version, stagingRoot, progress, cancellationToken);
            if (!Path.GetFullPath(staged.Root).Equals(Path.GetFullPath(stagingRoot),
                    StringComparison.OrdinalIgnoreCase))
                throw new InstallException("下载组件返回了意外的临时目录。");
            progress.Report(new InstallProgress("写入", 0, staged.Files.Count, null));
            await committer.CommitAsync(staged, root, cancellationToken);
            progress.Report(new InstallProgress("完成", 1, 1, null));
        }
        finally
        {
            if (Directory.Exists(stageParent))
            {
                var full = Path.GetFullPath(stageParent);
                if (Path.GetDirectoryName(full)!.Equals(parent, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(full).StartsWith(".pcl-version-bridge-stage-", StringComparison.Ordinal) &&
                    (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0)
                    Directory.Delete(full, recursive: true);
            }
        }
    }
}
