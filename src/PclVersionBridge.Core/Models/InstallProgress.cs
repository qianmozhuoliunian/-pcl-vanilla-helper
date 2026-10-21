namespace PclVersionBridge.Core.Models;

public sealed record InstallProgress(string Stage, long Completed, long Total, string? CurrentFile);
