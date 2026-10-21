namespace PclVersionBridge.Core.Paths;

public static class GamePathValidator
{
    public static string ValidateRoot(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            throw new InstallException("请选择 .minecraft 文件夹。");

        string root;
        try { root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InstallException("游戏目录路径无效。", ex);
        }

        if (!string.Equals(Path.GetFileName(root), ".minecraft", StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(@"\\", StringComparison.Ordinal)
            || !Directory.Exists(root))
            throw new InstallException("请选择已有的本地 .minecraft 文件夹。");

        RejectReparsePoints(root);
        return root;
    }

    public static string ValidateRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new InstallException("下载清单包含无效的相对路径。");

        var segments = relative.Split(['/', '\\']);
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.Contains(':') || IsDeviceName(segment))
                throw new InstallException("下载清单包含不安全的路径。");
        }

        return Path.Combine(segments);
    }

    public static string ValidateVersionId(string versionId)
    {
        var normalized = ValidateRelative(versionId);
        if (normalized.Contains(Path.DirectorySeparatorChar) ||
            normalized.Contains(Path.AltDirectorySeparatorChar))
            throw new InstallException("版本号包含路径分隔符。");
        return normalized;
    }

    public static void RejectReparsePoints(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InstallException("目标路径包含链接或重解析点，无法安全写入。");
            current = current.Parent;
        }
    }

    private static bool IsDeviceName(string segment)
    {
        var stem = segment.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 &&
             (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             stem[3] is >= '1' and <= '9');
    }
}
