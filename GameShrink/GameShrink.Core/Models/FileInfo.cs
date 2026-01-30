namespace GameShrink.Core.Models;

public class FileAnalysisInfo
{
    public string Path { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Logical size (uncompressed bytes).</summary>
    public long Size { get; set; }

    /// <summary>
    /// On-disk allocated bytes ("size on disk").
    /// This is what actually matters for free space.
    /// </summary>
    public long SizeOnDisk { get; set; }

    public DateTime LastModified { get; set; }
    public bool IsCompressed { get; set; }
    public long CompressedSize { get; set; }
    public double CurrentCompressionRatio { get; set; }
    public double EstimatedCompressionRatio { get; set; }
    public long EstimatedSavings { get; set; }
    public bool IsCompressible { get; set; }
    public string? SkipReason { get; set; }
    public FileTypeCategory Category { get; set; }
}

public enum FileTypeCategory
{
    Unknown,
    Executable,
    Texture,
    Audio,
    Video,
    Archive,
    Shader,
    Config,
    SaveData,
    Cache
}

public class FolderAnalysisResult
{
    public string Path { get; set; } = string.Empty;

    /// <summary>Logical bytes (sum of file lengths).</summary>
    public long TotalSize { get; set; }

    /// <summary>Allocated bytes ("size on disk").</summary>
    public long TotalSizeOnDisk { get; set; }

    public long TotalCompressedSize { get; set; }
    public int FileCount { get; set; }
    public int CompressedFileCount { get; set; }
    public long EstimatedSavings { get; set; }
    public double AverageCompressionRatio { get; set; }
    public List<FileAnalysisInfo> Files { get; set; } = new();
    public List<FolderAnalysisResult> SubFolders { get; set; } = new();
    public bool IsNtfs { get; set; }
    public bool SupportsCompression { get; set; }
    public string? WarningMessage { get; set; }
}

public class CompressionProgress
{
    public string CurrentFile { get; set; } = string.Empty;
    public long ProcessedBytes { get; set; }
    public long TotalBytes { get; set; }
    public int ProcessedFiles { get; set; }
    public int TotalFiles { get; set; }
    public double Percentage => TotalBytes > 0 ? (double)ProcessedBytes / TotalBytes * 100 : 0;
    public double SpeedMBps { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public string? StatusMessage { get; set; }
}
