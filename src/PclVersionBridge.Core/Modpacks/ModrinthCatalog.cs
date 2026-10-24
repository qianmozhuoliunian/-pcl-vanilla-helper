using System.Text.Json;

namespace PclVersionBridge.Core.Modpacks;

public sealed class ModrinthCatalog(ModpackNetwork network) : IModrinthCatalog
{
    public Task<ModpackSearchPage> SearchAsync(string query, string? gameVersion, int offset, CancellationToken token) =>
        SearchAsync(new ModpackSearchRequest(query, gameVersion, offset), token);
    public async Task<ModpackSearchPage> SearchAsync(ModpackSearchRequest request, CancellationToken token)
    {
        var (query, gameVersion, offset, sort) = request;
        if (query.Length > 200 || offset < 0 || offset > 10000)
            throw new InstallException("搜索条件过长或页码超出范围。");
        if (sort == "auto") sort = string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance";
        if (sort is not ("relevance" or "downloads" or "updated")) throw new InstallException("搜索排序方式无效。");
        var facets = new List<string[]> { new[] { "project_type:modpack" }, new[] { "categories:fabric" } };
        if (!string.IsNullOrWhiteSpace(gameVersion)) facets.Add(["versions:" + gameVersion.Trim()]);
        var uri = new Uri("https://api.modrinth.com/v2/search?query=" + Uri.EscapeDataString(query.Trim()) +
            "&facets=" + Uri.EscapeDataString(JsonSerializer.Serialize(facets)) + "&index=" + sort + "&limit=20&offset=" + offset);
        using var data = await network.GetJsonAsync(uri, 2 * 1024 * 1024, token);
        try
        {
            var root = data.RootElement;
            var hits = root.GetProperty("hits").EnumerateArray().Take(20).Select(p =>
                new ModpackProject(Text(p, "project_id"), Text(p, "slug"), Text(p, "title"),
                    Text(p, "author"), OptionalText(p, "description"))
                { IconUri = SafeImageUri(OptionalText(p, "icon_url")), Downloads = OptionalCount(p, "downloads") }).ToArray();
            return new ModpackSearchPage(hits, offset, root.GetProperty("total_hits").GetInt32());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InstallException("Modrinth 搜索结果格式无效，请稍后重试。", ex); }
    }
    public Task<IReadOnlyList<ModpackRelease>> GetVersionsAsync(string projectId, bool includePrerelease, CancellationToken token) =>
        GetVersionsAsync(projectId, includePrerelease, null, token);
    public async Task<IReadOnlyList<ModpackRelease>> GetVersionsAsync(string projectId, bool includePrerelease, string? gameVersion, CancellationToken token)
    {
        if (projectId.Length is < 1 or > 128 || projectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InstallException("整合包项目标识无效。");
        var filter = string.IsNullOrWhiteSpace(gameVersion) ? "" : "&game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[]{gameVersion.Trim()}));
        using var data = await network.GetJsonAsync(new Uri("https://api.modrinth.com/v2/project/" +
            Uri.EscapeDataString(projectId) + "/version?loaders=%5B%22fabric%22%5D&include_changelog=false" + filter), 8 * 1024 * 1024, token);
        try
        {
            if (data.RootElement.GetArrayLength() > 2000) throw new InstallException("包版本列表过大，请打开来源页面查看。");
            var result = new List<(ModpackRelease Version, DateTimeOffset Date)>();
            foreach (var version in data.RootElement.EnumerateArray())
            {
                var type = Text(version, "version_type");
                if (type != "release" && (!includePrerelease || type is not ("beta" or "alpha"))) continue;
                if (!version.GetProperty("loaders").EnumerateArray().Any(l => l.GetString() == "fabric")) continue;
                if (!string.IsNullOrWhiteSpace(gameVersion) && !version.GetProperty("game_versions").EnumerateArray().Any(v=>v.GetString()==gameVersion.Trim())) continue;
                var files = version.GetProperty("files").EnumerateArray()
                    .Where(f => Text(f, "filename").EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.GetProperty("primary").GetBoolean()).ToArray();
                if (files.Length == 0) continue;
                var file = files[0];
                var size = file.GetProperty("size").GetInt64();
                if (size <= 0 || size > ModpackRules.ArchiveLimit) continue;
                var hashes = file.GetProperty("hashes");
                result.Add((new ModpackRelease(Text(version, "id"), Text(version, "project_id"),
                    Text(version, "name"), Text(version, "version_number"),
                    version.GetProperty("game_versions").EnumerateArray().Select(v => v.GetString() ?? "").ToArray(),
                    type, ModpackRules.ContentUri(Text(file, "url")), size,
                    ModpackRules.Hash(Text(hashes, "sha1"), 40), ModpackRules.Hash(Text(hashes, "sha512"), 128)),
                    version.GetProperty("date_published").GetDateTimeOffset()));
            }
            return result.OrderByDescending(v => v.Date).Select(v => v.Version).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InstallException("Modrinth 包版本信息无效，请稍后重试。", ex); }
    }
    public async Task<ModpackDetails> GetProjectAsync(string projectId, CancellationToken token)
    {
        if (projectId.Length is < 1 or > 128 || projectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InstallException("整合包项目标识无效。");
        using var data=await network.GetJsonAsync(new Uri("https://api.modrinth.com/v2/project/"+Uri.EscapeDataString(projectId)),2*1024*1024,token);
        try
        {
            var p=data.RootElement;
            if(Text(p,"id")!=projectId || Text(p,"project_type")!="modpack") throw new InstallException("项目详情与所选整合包不一致。");
            string[] Strings(string name)=>p.TryGetProperty(name,out var values) && values.ValueKind==JsonValueKind.Array
                ? values.EnumerateArray().Select(v=>v.GetString()??"").Where(v=>v.Length>0).ToArray() : [];
            return new ModpackDetails(projectId,Text(p,"title"),OptionalText(p,"description"),SafeImageUri(OptionalText(p,"icon_url")),OptionalCount(p,"downloads"),Strings("game_versions"),Strings("categories"))
                { RawIconUri=SafeImageUri(OptionalText(p,"raw_icon_url")) };
        }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {throw new InstallException("Modrinth 项目资料格式无效，请稍后重试。",ex);}
    }
    public async Task<ModpackRelease> GetPinnedReleaseAsync(string projectId, string versionId, string archiveSha512, CancellationToken token)
    {
        static void ValidateId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length>128 || !value.All(char.IsAsciiLetterOrDigit))
                throw new InstallException("邀请的项目或版本标识无效。");
        }
        ValidateId(projectId); ValidateId(versionId);
        var expectedHash=ModpackRules.Hash(archiveSha512,128);
        // Project identity/type and version identity are independent source checks.
        await GetProjectAsync(projectId,token);
        using var versionData=await network.GetJsonAsync(new Uri("https://api.modrinth.com/v2/version/"+Uri.EscapeDataString(versionId)),2*1024*1024,token);
        try
        {
            var version=versionData.RootElement;
            if(Text(version,"id")!=versionId || Text(version,"project_id")!=projectId ||
                !version.GetProperty("loaders").EnumerateArray().Any(x=>x.GetString()=="fabric") ||
                Text(version,"version_type") is not ("release" or "beta" or "alpha"))
                throw new InstallException("邀请中的整合包版本已失效或暂不支持。");
            var file=version.GetProperty("files").EnumerateArray().FirstOrDefault(f=>
                Text(f,"filename").EndsWith(".mrpack",StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Text(f.GetProperty("hashes"),"sha512"),expectedHash,StringComparison.OrdinalIgnoreCase));
            if(file.ValueKind==JsonValueKind.Undefined)
                throw new InstallException("邀请指定的归档文件已不可用，未改装其他版本。");
            var size=file.GetProperty("size").GetInt64();
            if(size<=0 || size>ModpackRules.ArchiveLimit)throw new InstallException("邀请归档大小超出允许范围。");
            using var official=await network.GetJsonAsync(Catalog.VersionCatalog.ManifestUri,4*1024*1024,token);
            var formal=official.RootElement.GetProperty("versions").EnumerateArray()
                .Where(x=>Text(x,"type")=="release").Select(x=>Text(x,"id")).ToHashSet(StringComparer.Ordinal);
            var gameVersions=version.GetProperty("game_versions").EnumerateArray().Select(x=>x.GetString()??"").Where(formal.Contains).ToArray();
            if(gameVersions.Length==0)throw new InstallException("此邀请没有支持的 Minecraft 原版正式版。");
            var hashes=file.GetProperty("hashes");
            return new ModpackRelease(versionId,projectId,Text(version,"name"),Text(version,"version_number"),gameVersions,
                Text(version,"version_type"),ModpackRules.ContentUri(Text(file,"url")),size,
                ModpackRules.Hash(Text(hashes,"sha1"),40),expectedHash);
        }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {throw new InstallException("邀请对应的来源版本信息无效，请重试或联系发送者。",ex);}
    }
    private static Uri? SafeImageUri(string value)
    {
        if (!Uri.TryCreate(value,UriKind.Absolute,out var uri)) return null;
        try {return ModpackRules.ContentUri(uri.AbsoluteUri);} catch(InstallException){return null;}
    }
    private static string OptionalText(JsonElement element,string name)=>element.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString()??"" : "";
    private static long? OptionalCount(JsonElement element,string name)=>element.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetInt64(out var count) && count>=0 ? count : null;
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? "";
}
