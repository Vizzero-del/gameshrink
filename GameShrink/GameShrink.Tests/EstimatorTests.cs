using GameShrink.Core.Services;
using Xunit;

namespace GameShrink.Tests;

public class EstimatorTests
{
    [Fact]
    public async Task EstimateCompressionRatioAsync_TextLikeData_ShouldBeCompressible()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "GameShrinkTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var file = Path.Combine(tmp, "text.bin");

        // Highly compressible
        var data = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("AAAAABBBBBCCCCCDDDDDEEEEE\n", 20000)));
        await File.WriteAllBytesAsync(file, data);

        var est = new BrotliCompressibilityEstimator();
        var ratio = await est.EstimateCompressionRatioAsync(file, data.Length, 3, 1024 * 1024, CancellationToken.None);

        Assert.True(ratio < 0.5, $"Expected ratio < 0.5, got {ratio}");
    }

    [Fact]
    public async Task EstimateCompressionRatioAsync_RandomData_ShouldBeNearOne()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "GameShrinkTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var file = Path.Combine(tmp, "rand.bin");

        var data = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(file, data);

        var est = new BrotliCompressibilityEstimator();
        var ratio = await est.EstimateCompressionRatioAsync(file, data.Length, 3, 1024 * 1024, CancellationToken.None);

        Assert.True(ratio > 0.85, $"Expected ratio > 0.85, got {ratio}");
    }
}
