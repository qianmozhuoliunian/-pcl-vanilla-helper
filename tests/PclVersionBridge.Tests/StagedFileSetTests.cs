using PclVersionBridge.Core;
using PclVersionBridge.Core.Download;
namespace PclVersionBridge.Tests;
public sealed class StagedFileSetTests
{
    [Fact]
    public void SharedAssetReferencesAreCoalescedButConflictingHashesAreRejected()
    {
        var file = new StagedFile("assets/objects/ab/abcdef",new string('a',40),4);
        Assert.Single(StagedFileSet.Merge(new[] { file, file with {RelativePath="ASSETS/objects/ab/abcdef"} }));
        Assert.Throws<InstallException>(() => StagedFileSet.Merge(new[] {file,file with {Sha1=new string('b',40)}}));
        Assert.Throws<InstallException>(() => StagedFileSet.Merge(new[] {file,file with {Size=5}}));
    }
}
