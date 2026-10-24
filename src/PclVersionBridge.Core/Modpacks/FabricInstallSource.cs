using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PclVersionBridge.Core.Catalog;
using PclVersionBridge.Core.Download;
using PclVersionBridge.Core.Models;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Modpacks;

public sealed record FabricStaging(StagedVersion Parent, StagedVersion Instance);
public interface IFabricInstallSource
{
    Task<FabricStaging> StageAsync(MrpackPlan plan, string instanceId, string stagingRoot,
        IProgress<InstallProgress> progress, CancellationToken token);
}

public sealed class FabricInstallSource(IVersionCatalog catalog, IInstallSource vanilla, ModpackNetwork network) : IFabricInstallSource
{
    public async Task<FabricStaging> StageAsync(MrpackPlan plan, string instanceId, string stagingRoot,
        IProgress<InstallProgress> progress, CancellationToken token)
    {
        var id = GamePathValidator.ValidateVersionId(instanceId);
        var mc = GamePathValidator.ValidateVersionId(plan.MinecraftVersion);
        var loader = GamePathValidator.ValidateVersionId(plan.FabricVersion);
        if (id.Equals(mc, StringComparison.OrdinalIgnoreCase)) throw new InstallException("整合包实例名不能与原版版本号相同。");
        var version = (await catalog.ListReleasesAsync(token)).SingleOrDefault(v => v.Id == mc)
            ?? throw new InstallException("首版仅支持 Minecraft 原版正式版，该包依赖的版本暂不支持。");
        progress.Report(new InstallProgress("获取 Fabric", 0, 0, null));
        using var document = await network.GetJsonAsync(new Uri($"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(mc)}/{Uri.EscapeDataString(loader)}/profile/json"), 2 * 1024 * 1024, token);
        var profile = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
        if (profile["inheritsFrom"]?.GetValue<string>() != mc ||
            profile["mainClass"]?.GetValue<string>() != "net.fabricmc.loader.impl.launch.knot.KnotClient" ||
            profile["libraries"] is not JsonArray libraries || libraries.Count is 0 or > 100)
            throw new InstallException("Fabric 配置与指定游戏版本不一致。");
        profile["id"] = id;
        // The vanilla source supplies the complete official parent and its verified assets.
        var parent = await vanilla.StageAsync(version, stagingRoot, progress, token);
        var result = new List<StagedFile>();
        foreach (var library in libraries)
        {
            token.ThrowIfCancellationRequested();
            var name = library?["name"]?.GetValue<string>() ?? "";
            var parts = name.Split(':');
            if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_' and not '+')) ||
                library?["url"]?.GetValue<string>() != "https://maven.fabricmc.net/")
                throw new InstallException("Fabric 依赖库来源或名称无效。");
            var relative = GamePathValidator.ValidateRelative($"libraries/{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}.jar");
            var uri = new Uri("https://maven.fabricmc.net/" + relative.Replace('\\', '/')[10..]);
            var sha1 = library?["sha1"]?.GetValue<string>();
            if (sha1 is null) sha1 = (await network.GetTextAsync(new Uri(uri.AbsoluteUri + ".sha1"), 256, token)).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            sha1 = ModpackRules.Hash(sha1, 40);
            var size = library?["size"]?.GetValue<long>() ?? await network.GetLengthAsync(uri, token);
            var destination = Path.Combine(stagingRoot, relative);
            progress.Report(new InstallProgress("下载 Fabric", result.Count, libraries.Count, name));
            if (File.Exists(destination)) await ModpackNetwork.VerifyAsync(destination, size, sha1, null, token);
            else await network.DownloadAsync(uri, destination, size, sha1, null, token);
            // A shared parent artifact is committed by the parent exactly once.
            if (!parent.Files.Any(f => GamePathValidator.ValidateRelative(f.RelativePath).Equals(relative, StringComparison.OrdinalIgnoreCase)))
                result.Add(new StagedFile(relative, sha1, size));
        }
        var jsonRelative = Path.Combine("versions", id, id + ".json");
        var jsonPath = Path.Combine(stagingRoot, jsonRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllTextAsync(jsonPath, profile.ToJsonString(), token);
        await using var stream = File.OpenRead(jsonPath);
        result.Add(new StagedFile(jsonRelative, Convert.ToHexString(await SHA1.HashDataAsync(stream, token)), stream.Length));
        return new FabricStaging(parent, new StagedVersion(version with { Id = id }, stagingRoot, result));
    }
}
