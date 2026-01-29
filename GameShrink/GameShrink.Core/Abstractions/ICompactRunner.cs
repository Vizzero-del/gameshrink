using GameShrink.Core.Models;

namespace GameShrink.Core.Abstractions;

public interface ICompactRunner
{
    Task<CompactRunResult> CompressAsync(
        string directory,
        CompressionAlgorithm algorithm,
        CompactRunOptions options,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken);

    Task<CompactRunResult> UncompressAsync(
        string directory,
        CompactRunOptions options,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken);

    Task<CompactQueryResult> QueryAsync(
        string directory,
        CancellationToken cancellationToken);
}

public sealed class CompactRunOptions
{
    /// <summary>
    /// Mirrors /I (continue on errors).
    /// </summary>
    public bool ContinueOnErrors { get; set; } = true;

    /// <summary>
    /// Mirrors /F (force on all files, even already compressed).
    /// </summary>
    public bool Force { get; set; } = true;

    /// <summary>
    /// Mirrors /Q (quiet). If false, compact prints file-by-file lines; we can parse them for progress.
    /// </summary>
    public bool Quiet { get; set; } = false;

    /// <summary>
    /// Mirrors /S:"dir" behavior. This library always uses /S for recursive processing.
    /// </summary>
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// If true, attempts "Pause" by cancelling current compact.exe run (kill process),
    /// then later resuming by re-running compact on the directory.
    /// </summary>
    public bool PauseByCancellingProcess { get; set; } = true;
}

public sealed class CompactRunResult
{
    public int ExitCode { get; set; }
    public bool Started { get; set; }
    public bool WasCancelled { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

public sealed class CompactQueryResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;

    public int? CompressedFiles { get; set; }
    public int? UncompressedFiles { get; set; }
}
