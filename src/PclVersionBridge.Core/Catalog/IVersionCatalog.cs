using PclVersionBridge.Core.Models;

namespace PclVersionBridge.Core.Catalog;

public interface IVersionCatalog
{
    Task<IReadOnlyList<VersionChoice>> ListReleasesAsync(CancellationToken cancellationToken);
}
