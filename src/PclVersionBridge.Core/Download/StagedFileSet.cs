using PclVersionBridge.Core.Paths;
namespace PclVersionBridge.Core.Download;

public static class StagedFileSet
{
    public static IReadOnlyList<StagedFile> Merge(IEnumerable<StagedFile> files)
    {
        var result = new Dictionary<string,StagedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var key = GamePathValidator.ValidateRelative(file.RelativePath);
            if (result.TryGetValue(key,out var previous))
            {
                if (previous.Size != file.Size || !previous.Sha1.Equals(file.Sha1,StringComparison.OrdinalIgnoreCase))
                    throw new InstallException("官方清单对同一文件提供了冲突的校验值。");
            }
            else result.Add(key,file with {RelativePath=key});
        }
        return result.Values.ToArray();
    }
}
