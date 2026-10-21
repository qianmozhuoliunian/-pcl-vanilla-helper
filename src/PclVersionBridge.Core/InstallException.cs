namespace PclVersionBridge.Core;

public sealed class InstallException : Exception
{
    public InstallException(string message) : base(message) { }
    public InstallException(string message, Exception innerException) : base(message, innerException) { }
}
