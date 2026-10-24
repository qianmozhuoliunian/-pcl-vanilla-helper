using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Core.Modpacks;

public sealed record ModpackProject(string Id, string Slug, string Title, string Author, string Description)
{
    public Uri? IconUri { get; init; }
    public long? Downloads { get; init; }
    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "项目未提供简介。" : Description;
    public string DownloadsText => Downloads is long count ? $"下载 {count:N0}" : "下载量未提供";
    public Uri ProjectUrl => new("https://modrinth.com/modpack/" + Uri.EscapeDataString(Id));
}
public sealed record ModpackSearchRequest(string Query, string? GameVersion, int Offset = 0, string Sort = "auto");
public sealed record ModpackDetails(string Id, string Title, string Description, Uri? IconUri,
    long? Downloads, IReadOnlyList<string> GameVersions, IReadOnlyList<string> Categories)
{
    public Uri? RawIconUri { get; init; }
    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "项目未提供简介。" : Description;
    public Uri ProjectUrl => new("https://modrinth.com/modpack/" + Uri.EscapeDataString(Id));
}
public sealed record ModpackSearchPage(IReadOnlyList<ModpackProject> Hits, int Offset, int Total);
public sealed record ModpackRelease(string Id, string ProjectId, string Name, string VersionNumber,
    IReadOnlyList<string> GameVersions, string ReleaseType, Uri FileUrl, long FileSize, string Sha1, string Sha512)
{
    public string DisplayName => $"{Name} · MC {string.Join(", ", GameVersions)} · {ReleaseType}";
}
public sealed record PackFile(string Path, string Sha1, string Sha512, long Size,
    IReadOnlyList<Uri> Downloads, bool Optional);
public sealed record PackOverride(string EntryName, string Path, int Layer, long Size);
public sealed record MrpackPlan(string Name, string VersionId, string MinecraftVersion,
    string FabricVersion, IReadOnlyList<PackFile> Files, IReadOnlyList<PackOverride> Overrides);
public sealed record PackPreview(string ArchivePath, string ArchiveSha512, MrpackPlan Plan)
{
    public string? SourceProjectId { get; init; }
    public string? SourceVersionId { get; init; }
}

public interface IModrinthCatalog
{
    Task<ModpackSearchPage> SearchAsync(string query, string? gameVersion, int offset, CancellationToken cancellationToken);
    Task<ModpackSearchPage> SearchAsync(ModpackSearchRequest request, CancellationToken token) =>
        SearchAsync(request.Query, request.GameVersion, request.Offset, token);
    Task<IReadOnlyList<ModpackRelease>> GetVersionsAsync(string projectId, bool includePrerelease, CancellationToken cancellationToken);
    async Task<IReadOnlyList<ModpackRelease>> GetVersionsAsync(string projectId, bool includePrerelease, string? gameVersion, CancellationToken token)
    {
        var versions = await GetVersionsAsync(projectId, includePrerelease, token);
        return string.IsNullOrWhiteSpace(gameVersion) ? versions : versions.Where(v => v.GameVersions.Contains(gameVersion)).ToArray();
    }
    Task<ModpackDetails> GetProjectAsync(string projectId, CancellationToken token) =>
        Task.FromException<ModpackDetails>(new InstallException("此来源暂不支持项目详情。"));
    Task<ModpackRelease> GetPinnedReleaseAsync(string projectId, string versionId, string archiveSha512, CancellationToken token) =>
        Task.FromException<ModpackRelease>(new InstallException("此来源暂不支持指定版本邀请。"));
}
public interface IModpackInstallService
{
    Task InstallAsync(PackPreview preview, string instanceId, string targetRoot,
        IReadOnlySet<string> optionalPaths, IProgress<InstallProgress> progress, CancellationToken cancellationToken);
}
