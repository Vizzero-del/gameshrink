namespace GameShrink.Core.Models;

public enum CompressionMode
{
    Safe,
    StrongerLzx
}

public enum CompressionAlgorithm
{
    None,
    NTFS,
    Xpress4K,
    Xpress8K,
    Xpress16K,
    Lzx
}

public static class CompressionAlgorithmExtensions
{
    public static string ToCompactArgument(this CompressionAlgorithm algorithm)
    {
        return algorithm switch
        {
            CompressionAlgorithm.NTFS => "",
            CompressionAlgorithm.Xpress4K => "/EXE:XPRESS4K",
            CompressionAlgorithm.Xpress8K => "/EXE:XPRESS8K",
            CompressionAlgorithm.Xpress16K => "/EXE:XPRESS16K",
            CompressionAlgorithm.Lzx => "/EXE:LZX",
            _ => ""
        };
    }

    public static string GetDisplayName(this CompressionAlgorithm algorithm)
    {
        return algorithm switch
        {
            CompressionAlgorithm.NTFS => "NTFS (Standard)",
            CompressionAlgorithm.Xpress4K => "XPRESS4K (Fast)",
            CompressionAlgorithm.Xpress8K => "XPRESS8K (Balanced)",
            CompressionAlgorithm.Xpress16K => "XPRESS16K (Better)",
            CompressionAlgorithm.Lzx => "LZX (Maximum)",
            _ => "None"
        };
    }
}
