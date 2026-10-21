using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Core.Download;

public sealed record StagedFile(string RelativePath, string Sha1, long Size);

public sealed record StagedVersion(VersionChoice Version, string Root, IReadOnlyList<StagedFile> Files);
