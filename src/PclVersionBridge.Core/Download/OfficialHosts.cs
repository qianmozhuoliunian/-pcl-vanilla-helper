namespace PclVersionBridge.Core.Download;

public static class OfficialHosts
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "launchermeta.mojang.com",
        "piston-meta.mojang.com",
        "piston-data.mojang.com",
        "libraries.minecraft.net",
        "resources.download.minecraft.net",
        "launcher.mojang.com"
    };

    public static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || uri.Port != 443 || !Allowed.Contains(uri.IdnHost))
            throw new InstallException("下载来源不是已核准的 Minecraft 官方地址。");
    }
}
