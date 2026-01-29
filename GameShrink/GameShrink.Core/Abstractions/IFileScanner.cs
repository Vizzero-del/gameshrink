using GameShrink.Core.Models;

namespace GameShrink.Core.Abstractions;

public interface IFileScanner
{
    Task<FolderAnalysisResult> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class ScanOptions
{
    public bool DryRunOnly { get; set; } = true;

    /// <summary>
    /// When true, reparse points (symlinks/junctions/mount points) are NOT followed.
    /// Entries detected as reparse points are skipped and logged.
    /// </summary>
    public bool DoNotFollowReparsePoints { get; set; } = true;

    public long LargeFileThresholdBytes { get; set; } = 32L * 1024 * 1024;
    public int SampleBlockCount { get; set; } = 3;
    public int SampleBlockSizeBytes { get; set; } = 1 * 1024 * 1024;

    public double MinSavingsRatioToConsider { get; set; } = 0.03; // 3%

    public HashSet<string> ExcludedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp",
        ".log",
        ".dmp"
    };

    public List<string> ExcludedFolderNameFragments { get; set; } = new()
    {
        "shadercache",
        "shader cache",
        "cache",
        "temp",
        "crash",
        "crashes"
    };
}
