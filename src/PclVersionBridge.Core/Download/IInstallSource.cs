using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Core.Download;

public interface IInstallSource
{
    Task<StagedVersion> StageAsync(VersionChoice version, string stagingRoot,
        IProgress<InstallProgress> progress, CancellationToken cancellationToken);
}
