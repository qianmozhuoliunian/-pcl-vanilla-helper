using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Download;

public static class OfficialRequirements
{
    public static void Validate(StagedVersion staged)
    {
        var id = GamePathValidator.ValidateVersionId(staged.Version.Id);
        var versionPath = Path.Combine(staged.Root, "versions", id, id + ".json");
        var files = new Dictionary<string, StagedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in staged.Files)
        {
            var relative = GamePathValidator.ValidateRelative(file.RelativePath);
            if (!files.TryAdd(relative, file))
                throw new InstallException("官方清单包含重复文件路径。");
        }

        try
        {
            using var version = JsonDocument.Parse(File.ReadAllText(versionPath));
            var client = version.RootElement.GetProperty("downloads").GetProperty("client");
            Require(files, Path.Combine("versions", id, id + ".jar"),
                client.GetProperty("sha1").GetString(), client.GetProperty("size").GetInt64());

            var indexId = GamePathValidator.ValidateVersionId(version.RootElement
                .GetProperty("assetIndex").GetProperty("id").GetString() ?? "");
            var indexPath = Path.Combine(staged.Root, "assets", "indexes", indexId + ".json");
            using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
            foreach (var asset in index.RootElement.GetProperty("objects").EnumerateObject())
            {
                var hash = asset.Value.GetProperty("hash").GetString();
                var size = asset.Value.GetProperty("size").GetInt64();
                if (hash is null || hash.Length != 40 || !hash.All(Uri.IsHexDigit))
                    throw new InstallException("官方资源清单包含无效校验值。");
                Require(files, Path.Combine("assets", "objects", hash[..2], hash), hash, size);
            }

            foreach (var library in version.RootElement.GetProperty("libraries").EnumerateArray())
            {
                if (!AppliesToWindows(library)) continue;
                var downloads = library.GetProperty("downloads");
                if (downloads.TryGetProperty("artifact", out var artifact))
                    RequireLibrary(files, artifact);
                if (library.TryGetProperty("natives", out var natives) &&
                    natives.TryGetProperty("windows", out var nativeClassifier))
                {
                    var name = nativeClassifier.GetString()?.Replace("${arch}",
                        Environment.Is64BitProcess ? "64" : "32", StringComparison.Ordinal);
                    if (string.IsNullOrWhiteSpace(name) ||
                        !downloads.TryGetProperty("classifiers", out var classifiers) ||
                        !classifiers.TryGetProperty(name, out var nativeDownload))
                        throw new InstallException("官方版本缺少 Windows 原生库清单。");
                    RequireLibrary(files, nativeDownload);
                }
            }

            if (version.RootElement.TryGetProperty("logging", out var logging) &&
                logging.TryGetProperty("client", out var loggingClient))
            {
                var file = loggingClient.GetProperty("file");
                var name = GamePathValidator.ValidateVersionId(file.GetProperty("id").GetString() ?? "");
                Require(files, Path.Combine("assets", "log_configs", name),
                    file.GetProperty("sha1").GetString(), file.GetProperty("size").GetInt64());
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or
            InvalidOperationException or IOException)
        {
            throw new InstallException("官方版本或资源清单不完整，已停止安装。", ex);
        }
    }

    private static void RequireLibrary(IReadOnlyDictionary<string, StagedFile> files,
        JsonElement download)
    {
        var path = GamePathValidator.ValidateRelative(download.GetProperty("path").GetString() ?? "");
        Require(files, Path.Combine("libraries", path), download.GetProperty("sha1").GetString(),
            download.GetProperty("size").GetInt64());
    }

    private static bool AppliesToWindows(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules)) return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var applies = true;
            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name) &&
                    !string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase))
                    applies = false;
                if (os.TryGetProperty("arch", out var arch) &&
                    !MatchesArchitecture(arch.GetString()))
                    applies = false;
                if (os.TryGetProperty("version", out var version))
                {
                    try
                    {
                        if (!Regex.IsMatch(Environment.OSVersion.Version.ToString(),
                            version.GetString() ?? "", RegexOptions.None, TimeSpan.FromSeconds(1)))
                            applies = false;
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InstallException("官方库规则包含无效的系统版本条件。", ex);
                    }
                }
            }
            if (rule.TryGetProperty("features", out var features))
            {
                foreach (var feature in features.EnumerateObject())
                    if (feature.Value.GetBoolean()) applies = false;
            }
            if (!applies) continue;
            allowed = rule.GetProperty("action").GetString() switch
            {
                "allow" => true,
                "disallow" => false,
                _ => throw new InstallException("官方库规则包含未知操作。")
            };
        }
        return allowed;
    }

    private static bool MatchesArchitecture(string? name) => name?.ToLowerInvariant() switch
    {
        "x86" => RuntimeInformation.OSArchitecture == Architecture.X86,
        "x64" or "amd64" => RuntimeInformation.OSArchitecture == Architecture.X64,
        "arm64" => RuntimeInformation.OSArchitecture == Architecture.Arm64,
        _ => false
    };

    private static void Require(IReadOnlyDictionary<string, StagedFile> files,
        string relative, string? sha1, long size)
    {
        if (sha1 is null || sha1.Length != 40 || !sha1.All(Uri.IsHexDigit) || size < 0 ||
            !files.TryGetValue(relative, out var file) ||
            !file.Sha1.Equals(sha1, StringComparison.OrdinalIgnoreCase) || file.Size != size)
            throw new InstallException("安装清单缺少官方要求的游戏文件或校验值。");
    }
}
