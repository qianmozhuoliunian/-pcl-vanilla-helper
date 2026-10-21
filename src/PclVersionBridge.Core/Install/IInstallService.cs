using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Core.Install;

public interface IInstallService
{
    Task InstallAsync(VersionChoice version, string targetRoot,
        IProgress<InstallProgress> progress, CancellationToken cancellationToken);
}
