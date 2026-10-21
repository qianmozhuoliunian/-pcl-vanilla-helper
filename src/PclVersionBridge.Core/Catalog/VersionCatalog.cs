using System.Text.Json;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Catalog;

public sealed class VersionCatalog(HttpClient client) : IVersionCatalog
{
    public static readonly Uri ManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    public async Task<IReadOnlyList<VersionChoice>> ListReleasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(ManifestUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var versions = document.RootElement.GetProperty("versions");
            if (versions.ValueKind != JsonValueKind.Array)
                throw new InstallException("官方版本清单格式异常。");

            var result = new List<VersionChoice>();
            foreach (var item in versions.EnumerateArray())
            {
                if (item.GetProperty("type").GetString() != "release") continue;
                var id = GamePathValidator.ValidateVersionId(item.GetProperty("id").GetString() ?? "");
                var urlText = item.GetProperty("url").GetString();
                if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url))
                    throw new InstallException("官方版本清单包含无效的下载地址。");
                OfficialHosts.ValidateUri(url);
                var sha1 = item.GetProperty("sha1").GetString() ?? "";
                if (sha1.Length != 40 || !sha1.All(Uri.IsHexDigit))
                    throw new InstallException("官方版本清单缺少有效的校验值。");
                result.Add(new VersionChoice(id, url, sha1.ToLowerInvariant()));
            }
            if (result.Count == 0) throw new InstallException("官方版本清单没有可安装的正式版。");
            return result;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or HttpRequestException)
        {
            throw new InstallException("获取官方版本清单失败，请检查网络后重试。", ex);
        }
    }
}
