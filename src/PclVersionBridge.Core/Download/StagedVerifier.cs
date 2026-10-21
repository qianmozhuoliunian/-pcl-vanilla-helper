using System.Security.Cryptography;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Download;

public static class StagedVerifier
{
    public static async Task VerifyAsync(StagedVersion staged, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(staged.Root);
        foreach (var file in staged.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = GamePathValidator.ValidateRelative(file.RelativePath);
            if (file.Sha1.Length != 40 || !file.Sha1.All(Uri.IsHexDigit))
                throw new InstallException("文件缺少可用的 SHA-1 校验值。");
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InstallException("文件路径超出了临时安装目录。");
            if (!File.Exists(full)) throw new InstallException("临时安装文件缺失。");
            GamePathValidator.RejectReparsePoints(Path.GetDirectoryName(full)!);
            var info = new FileInfo(full);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InstallException("临时安装文件是链接，无法验证。");
            if (file.Size >= 0 && info.Length != file.Size)
                throw new InstallException("下载文件大小不匹配。");
            await using var stream = File.OpenRead(full);
            var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken));
            if (!actual.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase))
                throw new InstallException("下载文件校验失败。");
        }
    }
}
