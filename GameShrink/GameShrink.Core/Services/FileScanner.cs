using System.Runtime.InteropServices;
using GameShrink.Core.Abstractions;
using GameShrink.Core.Models;
using Serilog;

namespace GameShrink.Core.Services;

public sealed class FileScanner : IFileScanner
{
    private readonly ICompressibilityEstimator _estimator;
    private readonly IVolumeInfoService _volumeInfo;
    private readonly ILogger _log;

    public FileScanner(ICompressibilityEstimator estimator, IVolumeInfoService volumeInfo, ILogger log)
    {
        _estimator = estimator;
        _volumeInfo = volumeInfo;
        _log = log;
    }

    public async Task<FolderAnalysisResult> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Root path is required.", nameof(rootPath));
        rootPath = Path.GetFullPath(rootPath);

        var volume = _volumeInfo.GetCompressionInfo(rootPath);

        var result = new FolderAnalysisResult
        {
            Path = rootPath,
            IsNtfs = volume.IsNtfs,
            SupportsCompression = volume.SupportsPerFileCompression,
            WarningMessage = volume.Warning
        };

        if (!Directory.Exists(rootPath))
        {
            result.WarningMessage = "Directory does not exist.";
            return result;
        }

        var files = new List<FileAnalysisInfo>();

        // Pre-enumerate safely
        var allFiles = new List<string>();
        await Task.Run(() =>
        {
            EnumerateFilesSafe(rootPath, options, allFiles, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);

        // Estimate/measure sizes. TotalBytes is logical size; TotalDiskBytes is "size on disk".
        long totalBytes = 0;
        long totalDiskBytes = 0;

        // Cluster size helps give a deterministic fallback when Win32 allocation queries fail.
        _ = DiskSize.TryGetClusterSizeBytes(rootPath, out var clusterSizeBytes);

        foreach (var f in allFiles)
        {
            try
            {
                var fi = new FileInfo(f);
                totalBytes += fi.Length;

                var onDisk = DiskSize.GetFileSizeOnDisk(f);
                if (onDisk <= 0)
                {
                    onDisk = DiskSize.RoundUpToCluster(fi.Length, clusterSizeBytes);
                }

                totalDiskBytes += onDisk;
            }
            catch { /* ignore */ }
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long processedBytes = 0;
        int processedFiles = 0;

        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo fi;
            try
            {
                fi = new FileInfo(file);
                if (!fi.Exists) continue;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to stat file {File}", file);
                continue;
            }

            // Best-effort on-disk size (allocated bytes).
            long sizeOnDisk;
            try
            {
                sizeOnDisk = DiskSize.GetFileSizeOnDisk(file);
                if (sizeOnDisk <= 0)
                {
                    sizeOnDisk = DiskSize.RoundUpToCluster(fi.Length, clusterSizeBytes);
                }
            }
            catch
            {
                sizeOnDisk = DiskSize.RoundUpToCluster(fi.Length, clusterSizeBytes);
            }

            processedFiles++;
            processedBytes += fi.Length;

            progress?.Report(new CompressionProgress
            {
                CurrentFile = file,
                ProcessedBytes = processedBytes,
                TotalBytes = totalBytes,
                ProcessedFiles = processedFiles,
                TotalFiles = allFiles.Count,
                SpeedMBps = sw.Elapsed.TotalSeconds > 0 ? (processedBytes / (1024d * 1024d)) / sw.Elapsed.TotalSeconds : 0,
                StatusMessage = "Analyzing…"
            });

            var rel = Path.GetRelativePath(rootPath, file);
            var ext = Path.GetExtension(file);

            if (options.ExcludedExtensions.Contains(ext))
            {
                files.Add(new FileAnalysisInfo
                {
                    Path = file,
                    RelativePath = rel,
                    Size = fi.Length,
                    SizeOnDisk = sizeOnDisk,
                    LastModified = fi.LastWriteTimeUtc,
                    IsCompressible = false,
                    SkipReason = $"Excluded extension: {ext}",
                    Category = Categorize(file)
                });
                continue;
            }

            // Try to detect existing compression via attributes.
            var attrs = File.GetAttributes(file);
            var isCompressed = (attrs & FileAttributes.Compressed) != 0;

            double estRatio = 1.0;
            bool compressible = true;
            string? skipReason = null;

            try
            {
                estRatio = await _estimator.EstimateCompressionRatioAsync(
                    file,
                    fi.Length,
                    options.SampleBlockCount,
                    options.SampleBlockSizeBytes,
                    cancellationToken).ConfigureAwait(false);

                var savingsRatio = 1.0 - estRatio;
                if (savingsRatio < options.MinSavingsRatioToConsider)
                {
                    compressible = false;
                    skipReason = $"Low estimated savings ({savingsRatio:P0})";
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warning(ex, "Estimator failed for {File}", file);
                compressible = false;
                skipReason = "Estimator error";
            }

            var estimatedSavings = (long)Math.Max(0, fi.Length - (fi.Length * estRatio));

            files.Add(new FileAnalysisInfo
            {
                Path = file,
                RelativePath = rel,
                Size = fi.Length,
                SizeOnDisk = sizeOnDisk,
                LastModified = fi.LastWriteTimeUtc,
                IsCompressed = isCompressed,
                EstimatedCompressionRatio = estRatio,
                EstimatedSavings = estimatedSavings,
                IsCompressible = compressible,
                SkipReason = skipReason,
                Category = Categorize(file)
            });
        }

        result.Files = files;
        result.FileCount = files.Count;
        result.TotalSize = files.Sum(f => f.Size);
        result.TotalSizeOnDisk = totalDiskBytes > 0 ? totalDiskBytes : files.Sum(f => f.SizeOnDisk);
        result.CompressedFileCount = files.Count(f => f.IsCompressed);

        // NOTE: EstimatedSavings is best-effort and is based on logical sizes.
        // UI will also surface "size on disk" which matches Explorer/actual allocation.
        result.EstimatedSavings = files.Where(f => f.IsCompressible).Sum(f => f.EstimatedSavings);
        result.AverageCompressionRatio = files.Count > 0 ? files.Average(f => f.EstimatedCompressionRatio <= 0 ? 1.0 : f.EstimatedCompressionRatio) : 1.0;

        return result;
    }

    private void EnumerateFilesSafe(string rootPath, ScanOptions options, List<string> sink, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            if (ShouldExcludeFolder(dir, options))
            {
                _log.Information("Skipping excluded folder {Dir}", dir);
                continue;
            }

            DirectoryInfo di;
            try
            {
                di = new DirectoryInfo(dir);
                if (!di.Exists) continue;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to open directory {Dir}", dir);
                continue;
            }

            // Reparse point protection
            if (options.DoNotFollowReparsePoints && (di.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                _log.Warning("Skipping reparse point directory (symlink/junction) {Dir}", dir);
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    sink.Add(file);
                }

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var sdi = new DirectoryInfo(sub);
                        if (options.DoNotFollowReparsePoints && (sdi.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            _log.Warning("Skipping reparse point directory (symlink/junction) {Dir}", sub);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Failed to stat directory {Dir}", sub);
                        continue;
                    }

                    pending.Push(sub);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.Warning(ex, "No access to directory {Dir}", dir);
            }
            catch (IOException ex)
            {
                _log.Warning(ex, "IO error enumerating directory {Dir}", dir);
            }
        }
    }

    private static bool ShouldExcludeFolder(string fullPath, ScanOptions options)
    {
        // IMPORTANT: apply folder exclusions to folder *names* (segments), not the entire absolute path.
        // Otherwise common system paths like "C:\\Users\\...\\Temp" could accidentally match "temp" and skip everything.
        try
        {
            var di = new DirectoryInfo(fullPath);
            var nameLower = di.Name.ToLowerInvariant();

            foreach (var fragment in options.ExcludedFolderNameFragments)
            {
                var frag = fragment.Trim();
                if (frag.Length == 0) continue;

                if (nameLower.Contains(frag.ToLowerInvariant()))
                    return true;
            }
        }
        catch
        {
            // If something goes wrong, do not exclude.
        }

        return false;
    }

    private static FileTypeCategory Categorize(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".exe" or ".dll" => FileTypeCategory.Executable,
            ".pak" or ".bundle" or ".zip" or ".7z" or ".rar" => FileTypeCategory.Archive,
            ".dds" or ".png" or ".jpg" or ".jpeg" or ".tga" => FileTypeCategory.Texture,
            ".wav" or ".ogg" or ".mp3" or ".flac" => FileTypeCategory.Audio,
            ".mp4" or ".mkv" or ".webm" => FileTypeCategory.Video,
            ".ini" or ".cfg" or ".json" or ".xml" => FileTypeCategory.Config,
            ".bin" => FileTypeCategory.Unknown,
            _ => FileTypeCategory.Unknown
        };
    }
}
