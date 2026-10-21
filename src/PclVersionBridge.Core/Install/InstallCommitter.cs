using System.Security.Cryptography;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Install;

public sealed class InstallCommitter(
    Func<string, long>? availableBytes = null,
    Action? afterPreflight = null,
    Action<string>? afterSharedFile = null)
{
    public async Task CommitAsync(StagedVersion staged, string targetRoot,
        CancellationToken cancellationToken)
    {
        var root = GamePathValidator.ValidateRoot(targetRoot);
        var id = GamePathValidator.ValidateVersionId(staged.Version.Id);
        await StagedVerifier.VerifyAsync(staged, cancellationToken);
        var versionRelative = Path.Combine("versions", id);
        var sourceVersion = Path.Combine(staged.Root, versionRelative);
        var targetVersion = Path.Combine(root, versionRelative);
        if (!Directory.Exists(sourceVersion) || Directory.Exists(targetVersion) || File.Exists(targetVersion))
            throw new InstallException("目标版本已存在或临时版本不完整。");

        var versionPrefix = versionRelative + Path.DirectorySeparatorChar;
        var assetsPrefix = "assets" + Path.DirectorySeparatorChar;
        var librariesPrefix = "libraries" + Path.DirectorySeparatorChar;
        var files = staged.Files.Select(file =>
            (File: file, Relative: GamePathValidator.ValidateRelative(file.RelativePath))).ToArray();
        if (files.GroupBy(item => item.Relative, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            throw new InstallException("清单包含重复的目标路径。");
        foreach (var item in files)
        {
            if (!item.Relative.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase) &&
                !item.Relative.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase) &&
                !item.Relative.StartsWith(librariesPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InstallException("清单试图写入游戏目录以外的区域。");
        }

        var expectedVersionFiles = files.Where(item => item.Relative.StartsWith(versionPrefix,
            StringComparison.OrdinalIgnoreCase)).Select(item => Path.GetFullPath(Path.Combine(staged.Root,
            item.Relative))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expectedVersionFiles.Count == 0)
            throw new InstallException("临时版本目录包含未经校验的文件。");
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(sourceVersion);
        while (pendingDirectories.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pendingDirectories.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InstallException("临时版本目录包含链接，已拒绝提交。");
                if ((attributes & FileAttributes.Directory) != 0)
                    pendingDirectories.Push(entry);
                else if (!expectedVersionFiles.Contains(Path.GetFullPath(entry)))
                    throw new InstallException("临时版本目录包含未经校验的文件。");
            }
        }

        long missingBytes = 0;
        var shared = files.Where(item => !item.Relative.StartsWith(versionPrefix,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var item in shared)
        {
            var destination = Path.Combine(root, item.Relative);
            RejectDestinationLink(destination);
            if (Directory.Exists(destination))
                throw new InstallException("目标路径被文件夹占用，无法安装。");
            if (File.Exists(destination))
                await AssertExistingMatchesAsync(destination, item.File.Sha1, cancellationToken);
            else
                missingBytes = checked(missingBytes + item.File.Size);
        }

        var free = availableBytes?.Invoke(root) ??
            new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
        if (free < missingBytes)
            throw new InstallException("游戏目录所在磁盘空间不足。");
        afterPreflight?.Invoke();

        var journal = new InstallJournal(Path.Combine(Path.GetDirectoryName(staged.Root)!, "journal.txt"));
        foreach (var item in shared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(staged.Root, item.Relative);
            var destination = Path.Combine(root, item.Relative);
            RejectDestinationLink(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            RejectDestinationLink(destination);
            if (Directory.Exists(destination))
                throw new InstallException("目标路径被文件夹占用，无法安装。");
            if (File.Exists(destination))
            {
                await AssertExistingMatchesAsync(destination, item.File.Sha1, cancellationToken);
                continue;
            }
            try { File.Move(source, destination, overwrite: false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstallException("写入游戏目录失败；已有文件未被覆盖。", ex);
            }
            journal.Record(item.Relative);
            afterSharedFile?.Invoke(item.Relative);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(targetVersion) || File.Exists(targetVersion))
            throw new InstallException("安装期间目标版本被其他程序创建，已停止提交。");
        GamePathValidator.RejectReparsePoints(Path.GetDirectoryName(targetVersion)!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetVersion)!);
        try { Directory.Move(sourceVersion, targetVersion); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallException("提交版本目录失败；已有版本未被覆盖。", ex);
        }
        journal.Record(versionRelative);
    }

    private static void RejectDestinationLink(string destination)
    {
        GamePathValidator.RejectReparsePoints(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new InstallException("目标文件是链接，无法安全写入。");
    }

    private static async Task AssertExistingMatchesAsync(string path, string expected,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InstallException("现有游戏文件与官方校验值冲突，已停止安装，未覆盖该文件。");
    }
}
