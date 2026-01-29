using System.Runtime.InteropServices;
using GameShrink.Core.Abstractions;

namespace GameShrink.Core.Services;

public sealed class VolumeInfoService : IVolumeInfoService
{
    // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationw
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        System.Text.StringBuilder? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        System.Text.StringBuilder? lpFileSystemNameBuffer,
        int nFileSystemNameSize);

    private const uint FILE_FILE_COMPRESSION = 0x00000010;

    public VolumeCompressionInfo GetCompressionInfo(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return new VolumeCompressionInfo(false, false, string.Empty, string.Empty, "Cannot determine drive root.");
        }

        var fsName = new System.Text.StringBuilder(64);
        var ok = GetVolumeInformation(
            root,
            null,
            0,
            out _,
            out _,
            out var flags,
            fsName,
            fsName.Capacity);

        if (!ok)
        {
            return new VolumeCompressionInfo(false, false, root, string.Empty, $"GetVolumeInformation failed: {Marshal.GetLastWin32Error()}.");
        }

        var fileSystem = fsName.ToString();
        var isNtfs = string.Equals(fileSystem, "NTFS", StringComparison.OrdinalIgnoreCase);
        var supports = (flags & FILE_FILE_COMPRESSION) != 0;

        string? warning = null;
        if (!isNtfs)
        {
            warning = $"Volume is {fileSystem}. GameShrink requires NTFS for per-file compression.";
        }
        else if (!supports)
        {
            warning = "NTFS compression flag FILE_FILE_COMPRESSION is not reported. Compression may not be supported on this volume.";
        }

        return new VolumeCompressionInfo(isNtfs, supports, root, fileSystem, warning);
    }
}
