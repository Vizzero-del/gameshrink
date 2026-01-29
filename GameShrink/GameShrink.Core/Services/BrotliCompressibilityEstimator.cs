using System.IO.Compression;
using GameShrink.Core.Abstractions;

namespace GameShrink.Core.Services;

public sealed class BrotliCompressibilityEstimator : ICompressibilityEstimator
{
    /// <summary>
    /// Fast-ish, in-memory estimate using Brotli at Fastest.
    /// This is only an approximation of NTFS/Compact savings.
    /// </summary>
    public async Task<double> EstimateCompressionRatioAsync(
        string filePath,
        long sizeBytes,
        int sampleBlockCount,
        int sampleBlockSizeBytes,
        CancellationToken cancellationToken)
    {
        // Guard
        if (sizeBytes <= 0) return 1.0;

        // For small files, read the whole thing up to a cap.
        var maxWholeRead = 2 * 1024 * 1024;

        await using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (sizeBytes <= maxWholeRead)
        {
            var buffer = new byte[sizeBytes];
            var read = await ReadExactlyOrUntilEofAsync(fs, buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0) return 1.0;
            return CompressRatio(buffer.AsSpan(0, read));
        }

        // Sample 3 blocks: start/middle/end
        var blocks = Math.Max(1, sampleBlockCount);
        var blockSize = Math.Max(64 * 1024, sampleBlockSizeBytes);

        var positions = new List<long>();
        positions.Add(0);
        if (blocks >= 2)
        {
            positions.Add(Math.Max(0, (sizeBytes / 2) - (blockSize / 2)));
        }
        if (blocks >= 3)
        {
            positions.Add(Math.Max(0, sizeBytes - blockSize));
        }

        long totalRead = 0;
        long totalCompressed = 0;
        var buffer2 = new byte[blockSize];

        foreach (var pos in positions.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            fs.Position = pos;
            var read = await fs.ReadAsync(buffer2, 0, blockSize, cancellationToken).ConfigureAwait(false);
            if (read <= 0) continue;

            totalRead += read;
            totalCompressed += CompressSize(buffer2.AsSpan(0, read));
        }

        if (totalRead <= 0) return 1.0;
        return (double)totalCompressed / totalRead;
    }

    private static double CompressRatio(ReadOnlySpan<byte> data)
    {
        var c = CompressSize(data);
        return (double)c / data.Length;
    }

    private static int CompressSize(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(data);
        }
        return (int)ms.Length;
    }

    private static async Task<int> ReadExactlyOrUntilEofAsync(FileStream fs, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            ct.ThrowIfCancellationRequested();
            var read = await fs.ReadAsync(buffer, total, buffer.Length - total, ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
