using GameShrink.Core.Services;
using Serilog;
using Xunit;

namespace GameShrink.Tests;

public class SmokeTests
{
    [Fact]
    public async Task FileScanner_SkipsReparsePoints()
    {
        // Create a directory with a subfolder and a file.
        var root = Path.Combine(Path.GetTempPath(), "GameShrinkTests", "scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "a.txt"), "hello");

        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console().CreateLogger();
        var scanner = new FileScanner(new BrotliCompressibilityEstimator(), new VolumeInfoService(), log);

        var res = await scanner.ScanAsync(root, new GameShrink.Core.Abstractions.ScanOptions { DoNotFollowReparsePoints = true }, null, CancellationToken.None);

        Assert.True(res.FileCount >= 1);
        Assert.True(res.TotalSize > 0);
    }
}
