namespace GameShrink.Core.Abstractions;

public interface IVolumeInfoService
{
    VolumeCompressionInfo GetCompressionInfo(string path);
}

public sealed record VolumeCompressionInfo(
    bool IsNtfs,
    bool SupportsPerFileCompression,
    string VolumeRoot,
    string FileSystemName,
    string? Warning);
