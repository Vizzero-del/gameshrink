using GameShrink.Core.Abstractions;
using GameShrink.Core.Models;
using Serilog;

namespace GameShrink.Core.Services;

public sealed class GameShrinkEngine
{
    private readonly IFileScanner _scanner;
    private readonly ICompactRunner _compact;
    private readonly IOperationJournal _journal;
    private readonly ILogger _log;

    public GameShrinkEngine(IFileScanner scanner, ICompactRunner compact, IOperationJournal journal, ILogger log)
    {
        _scanner = scanner;
        _compact = compact;
        _journal = journal;
        _log = log;
    }

    public Task<FolderAnalysisResult> AnalyzeAsync(
        string folder,
        ScanOptions scanOptions,
        IProgress<CompressionProgress>? progress,
        CancellationToken ct)
        => _scanner.ScanAsync(folder, scanOptions, progress, ct);

    public async Task<OperationRecord> CompressAsync(
        string folder,
        CompressionMode mode,
        CompressionAlgorithm algorithm,
        CompactRunOptions options,
        long beforeBytes,
        IProgress<CompressionProgress>? progress,
        CancellationToken ct)
    {
        var op = new OperationRecord
        {
            Path = Path.GetFullPath(folder),
            Mode = mode,
            Algorithm = algorithm,
            StartedAt = DateTime.UtcNow,
            Status = OperationStatus.InProgress,
            BeforeBytes = beforeBytes,
            AfterBytes = 0,
            IsRollback = false
        };

        await _journal.AddAsync(op, ct).ConfigureAwait(false);

        try
        {
            var rr = await _compact.CompressAsync(folder, algorithm, options, progress, ct).ConfigureAwait(false);
            if (rr.WasCancelled)
            {
                op.Status = OperationStatus.Cancelled;
            }
            else if (rr.ExitCode == 0)
            {
                op.Status = OperationStatus.Completed;
            }
            else
            {
                op.Status = OperationStatus.Failed;
                op.ErrorMessage = string.Join(Environment.NewLine, rr.Errors.Take(10));
            }

            op.FinishedAt = DateTime.UtcNow;
            op.AfterBytes = TryGetDirectorySizeOnDisk(folder);
            await _journal.UpdateAsync(op, ct).ConfigureAwait(false);
            return op;
        }
        catch (OperationCanceledException)
        {
            op.Status = OperationStatus.Cancelled;
            op.FinishedAt = DateTime.UtcNow;
            op.AfterBytes = TryGetDirectorySizeOnDisk(folder);
            await _journal.UpdateAsync(op, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Compression failed");
            op.Status = OperationStatus.Failed;
            op.ErrorMessage = ex.Message;
            op.FinishedAt = DateTime.UtcNow;
            op.AfterBytes = TryGetDirectorySizeOnDisk(folder);
            await _journal.UpdateAsync(op, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<OperationRecord> RollbackAsync(
        string folder,
        Guid? originalOperationId,
        CompactRunOptions options,
        long beforeBytes,
        IProgress<CompressionProgress>? progress,
        CancellationToken ct)
    {
        var op = new OperationRecord
        {
            Path = Path.GetFullPath(folder),
            Mode = CompressionMode.Safe,
            Algorithm = CompressionAlgorithm.None,
            StartedAt = DateTime.UtcNow,
            Status = OperationStatus.InProgress,
            BeforeBytes = beforeBytes,
            AfterBytes = 0,
            IsRollback = true,
            OriginalOperationId = originalOperationId
        };

        await _journal.AddAsync(op, ct).ConfigureAwait(false);

        try
        {
            var rr = await _compact.UncompressAsync(folder, options, progress, ct).ConfigureAwait(false);
            if (rr.WasCancelled)
            {
                op.Status = OperationStatus.Cancelled;
            }
            else if (rr.ExitCode == 0)
            {
                op.Status = OperationStatus.RolledBack;
            }
            else
            {
                op.Status = OperationStatus.Failed;
                op.ErrorMessage = string.Join(Environment.NewLine, rr.Errors.Take(10));
            }

            op.FinishedAt = DateTime.UtcNow;
            op.AfterBytes = TryGetDirectorySizeOnDisk(folder);
            await _journal.UpdateAsync(op, ct).ConfigureAwait(false);
            return op;
        }
        catch (OperationCanceledException)
        {
            op.Status = OperationStatus.Cancelled;
            op.FinishedAt = DateTime.UtcNow;
            op.AfterBytes = TryGetDirectorySizeOnDisk(folder);
            await _journal.UpdateAsync(op, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Rollback failed");
            op.Status = OperationStatus.Failed;
            op.ErrorMessage = ex.Message;
            op.FinishedAt = DateTime.UtcNow;
            op.AfterBytes = TryGetDirectorySizeOnDisk(folder);
            await _journal.UpdateAsync(op, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static long TryGetDirectorySizeOnDisk(string folder)
    {
        try
        {
            long total = 0;

            _ = DiskSize.TryGetClusterSizeBytes(folder, out var clusterSizeBytes);

            foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var onDisk = DiskSize.GetFileSizeOnDisk(f);
                    if (onDisk <= 0)
                    {
                        long logical = 0;
                        try { logical = new FileInfo(f).Length; } catch { /* ignore */ }
                        onDisk = DiskSize.RoundUpToCluster(logical, clusterSizeBytes);
                    }

                    total += onDisk;
                }
                catch
                {
                    // ignore
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }
}
