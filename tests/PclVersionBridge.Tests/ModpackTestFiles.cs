using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PclVersionBridge.Tests;

internal sealed class ModpackTestFiles : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "vanillareach-pack-test-" + Guid.NewGuid().ToString("N"));
    public ModpackTestFiles() => Directory.CreateDirectory(Root);
    public static byte[] Payload => "hello"u8.ToArray();
    public static object FileEntry(string path = "mods/example.jar", string? client = null, byte[]? data = null) => new
    {
        path, hashes = new { sha1 = Convert.ToHexString(SHA1.HashData(data ?? Payload)), sha512 = Convert.ToHexString(SHA512.HashData(data ?? Payload)) },
        downloads = new[] { "https://cdn.modrinth.com/data/example.jar" },
        fileSize = (data ?? Payload).Length,
        env = client is null ? null : new { client, server = "unsupported" }
    };
    public static string Index(object[]? files = null) => JsonSerializer.Serialize(new
    {
        formatVersion = 1, game = "minecraft", name = "Example Pack", versionId = "1.0",
        dependencies = new Dictionary<string, string> { ["minecraft"] = "1.20.1", ["fabric-loader"] = "0.16.0" },
        files = files ?? new[] { FileEntry() }
    });
    public string Pack(string? index = null, params (string Path, string Text)[] overrides)
    {
        var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".mrpack");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, "modrinth.index.json", index ?? Index());
        foreach (var item in overrides) Write(zip, item.Path, item.Text);
        return path;
    }
    private static void Write(ZipArchive zip, string path, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(Root);
        if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(full).StartsWith("vanillareach-pack-test-", StringComparison.Ordinal))
            Directory.Delete(full, true);
    }
}
