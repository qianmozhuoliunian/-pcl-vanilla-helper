using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Modpacks;

internal static class ModpackRules
{
    public const long ArchiveLimit = 512L * 1024 * 1024;
    public const int IndexLimit = 8 * 1024 * 1024;
    public const long FileLimit = 1024L * 1024 * 1024;
    public const long ExpandedLimit = 4L * 1024 * 1024 * 1024;
    public const long DownloadLimit = 8L * 1024 * 1024 * 1024;
    public static string ContentPath(string path)
    {
        var result = GamePathValidator.ValidateRelative(path).Replace('\\', '/');
        var first = result.Split('/')[0];
        if (new[] { "versions", "assets", "libraries", "PCL", ".vanillareach", "launcher_accounts.json",
            "launcher_profiles.json", "accounts.json", "instance.cfg", "mmc-pack.json" }
            .Contains(first, StringComparer.OrdinalIgnoreCase))
            throw new InstallException("整合包包含启动器保留目录或账号配置，已停止安装。");
        return result;
    }
    public static Uri ContentUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "cdn.modrinth.com" || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            uri.Fragment.Length != 0)
            throw new InstallException("首版只支持 Modrinth CDN 文件来源，此包暂不能安装。");
        return uri;
    }
    public static string Hash(string value, int length)
    {
        if (value.Length != length || !value.All(Uri.IsHexDigit))
            throw new InstallException("整合包缺少有效的文件校验值。");
        return value.ToUpperInvariant();
    }
}
