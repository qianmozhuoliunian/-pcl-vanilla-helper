using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PclVersionBridge.Core.Paths;

namespace PclVersionBridge.Core.Modpacks;

public sealed class MrpackReader
{
    public async Task<PackPreview> ReadAsync(string archivePath, CancellationToken cancellationToken)
    {
        try
        {
            var full = Path.GetFullPath(archivePath);
            GamePathValidator.RejectReparsePoints(Path.GetDirectoryName(full)!);
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InstallException("整合包文件不能是链接。");
            await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > ModpackRules.ArchiveLimit)
                throw new InstallException("整合包文件超过首版支持的 512 MiB 上限。");
            var hash = Convert.ToHexString(await SHA512.HashDataAsync(stream, cancellationToken));
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > 20_000) throw new InstallException("整合包条目过多。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var overrides = new List<PackOverride>();
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = entry.FullName.EndsWith('/');
                var name = GamePathValidator.ValidateRelative(entry.FullName.TrimEnd('/')).Replace('\\', '/');
                if (!names.Add(name) || entry.IsEncrypted || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                    (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InstallException("整合包包含重复路径、加密文件或链接。");
                if (entry.Length < 0 || entry.Length > ModpackRules.FileLimit ||
                    (expanded = checked(expanded + entry.Length)) > ModpackRules.ExpandedLimit)
                    throw new InstallException("整合包展开后超过安全大小上限。");
                if (directory) continue;
                var layer = name.StartsWith("overrides/", StringComparison.Ordinal) ? 0 :
                    name.StartsWith("client-overrides/", StringComparison.Ordinal) ? 1 : -1;
                if (layer >= 0)
                {
                    var relative = ModpackRules.ContentPath(name[(name.IndexOf('/') + 1)..]);
                    overrides.Add(new PackOverride(entry.FullName, relative, layer, entry.Length));
                }
            }
            var index = zip.GetEntry("modrinth.index.json") ??
                throw new InstallException("没有找到 modrinth.index.json，请选择有效的 .mrpack。");
            if (index.Length > ModpackRules.IndexLimit) throw new InstallException("整合包索引超过 8 MiB 上限。");
            await using var indexStream = index.Open();
            using var memory = new MemoryStream();
            await CopyBoundedAsync(indexStream, memory, ModpackRules.IndexLimit, cancellationToken);
            using var document = JsonDocument.Parse(memory.ToArray());
            UniqueProperties(document.RootElement);
            var root = document.RootElement;
            if (root.GetProperty("formatVersion").GetInt32() != 1 || Text(root, "game") != "minecraft")
                throw new InstallException("首版仅支持 Minecraft 的 mrpack v1 格式。");
            var dependencies = root.GetProperty("dependencies");
            if (dependencies.EnumerateObject().Count() != 2 ||
                !dependencies.TryGetProperty("minecraft", out var minecraft) ||
                !dependencies.TryGetProperty("fabric-loader", out var fabric))
                throw new InstallException("首版仅支持 Fabric 整合包，此包包含不支持的加载器或依赖。");
            var mc = GamePathValidator.ValidateVersionId(minecraft.GetString() ?? "");
            var loader = GamePathValidator.ValidateVersionId(fabric.GetString() ?? "");
            var files = new List<PackFile>();
            long downloadBytes = 0;
            foreach (var file in root.GetProperty("files").EnumerateArray())
            {
                if (files.Count >= 20_000) throw new InstallException("整合包下载文件过多。");
                var path = ModpackRules.ContentPath(Text(file, "path"));
                var environment = "required";
                if (file.TryGetProperty("env", out var env) && env.ValueKind != JsonValueKind.Null)
                {
                    if (env.TryGetProperty("client", out var client)) environment = client.GetString() ?? "";
                    foreach (var side in env.EnumerateObject())
                        if (side.Value.GetString() is not ("required" or "optional" or "unsupported"))
                            throw new InstallException("整合包客户端环境声明无效。");
                }
                if (environment == "unsupported") continue;
                var size = file.GetProperty("fileSize").GetInt64();
                if (size < 0 || size > ModpackRules.FileLimit ||
                    (downloadBytes = checked(downloadBytes + size)) > ModpackRules.DownloadLimit)
                    throw new InstallException("整合包下载量超过首版安全上限。");
                var hashes = file.GetProperty("hashes");
                var sha1 = ModpackRules.Hash(Text(hashes, "sha1"), 40);
                var sha512 = ModpackRules.Hash(Text(hashes, "sha512"), 128);
                var urls = file.GetProperty("downloads").EnumerateArray()
                    .Select(u => u.GetString() ?? "").Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
                        uri.Host == "cdn.modrinth.com").Select(ModpackRules.ContentUri).ToArray();
                if (urls.Length == 0)
                    throw new InstallException("此包需要首版尚不支持的外部下载来源，已停止安装。");
                files.Add(new PackFile(path, sha1, sha512, size, urls, environment == "optional"));
            }
            RejectFileConflicts(files.Select(f => f.Path));
            foreach (var layer in overrides.GroupBy(o => o.Layer)) RejectFileConflicts(layer.Select(o => o.Path));
            RejectFileConflicts(files.Select(f => f.Path).Concat(overrides.Select(o => o.Path)), allowSame: true);
            return new PackPreview(full, hash, new MrpackPlan(Text(root, "name"), Text(root, "versionId"),
                mc, loader, files.AsReadOnly(), overrides.OrderBy(o => o.Layer).ToArray()));
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException or OverflowException)
        {
            throw new InstallException("整合包格式或文件清单无效，未写入游戏目录。", ex);
        }
    }

    internal static async Task CopyBoundedAsync(Stream source, Stream target, long max, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            total = checked(total + count);
            if (total > max) throw new InstallException("文件实际大小超过允许上限。");
            await target.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }
    private static string Text(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
            throw new InstallException("整合包文本字段无效。");
        return value;
    }
    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InstallException("整合包索引包含重复字段。");
                UniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) UniqueProperties(item);
    }
    private static void RejectFileConflicts(IEnumerable<string> paths, bool allowSame = false)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            if (!set.Add(path) && !allowSame) throw new InstallException("整合包文件路径存在大小写冲突或重复。");
        foreach (var path in set)
        {
            var parent = path;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (set.Contains(parent)) throw new InstallException("整合包文件与文件夹路径冲突。");
            }
        }
    }
}
