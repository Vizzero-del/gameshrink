namespace GameShrink.Core.Abstractions;

public interface ICompressibilityEstimator
{
    /// <summary>
    /// Returns estimated compression ratio: compressedBytes / originalBytes.
    /// Lower is better. 1.0 means no gain.
    /// </summary>
    Task<double> EstimateCompressionRatioAsync(
        string filePath,
        long sizeBytes,
        int sampleBlockCount,
        int sampleBlockSizeBytes,
        CancellationToken cancellationToken);
}
