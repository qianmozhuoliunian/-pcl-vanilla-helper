namespace PclVersionBridge.Core.Install;

public sealed class InstallJournal(string path)
{
    public void Record(string relativePath) =>
        File.AppendAllText(path, relativePath + Environment.NewLine);
}
