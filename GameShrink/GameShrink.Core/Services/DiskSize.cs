using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameShrink.Core.Services;

/// <summary>
/// Utilities to measure "size on disk" (allocated bytes) for files on Windows.
/// Uses AllocationSize (preferred) and falls back to GetCompressedFileSizeW.
/// </summary>
public static class DiskSize
{
    // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getdiskfreespacew
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceW(
        string lpRootPathName,
        out uint lpSectorsPerCluster,
        out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters,
        out uint lpTotalNumberOfClusters);

    // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandleex
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        FILE_INFO_BY_HANDLE_CLASS fileInfoClass,
        out FILE_STANDARD_INFO lpFileInformation,
        uint dwBufferSize);

    private enum FILE_INFO_BY_HANDLE_CLASS
    {
        FileStandardInfo = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_STANDARD_INFO
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;

        [MarshalAs(UnmanagedType.U1)]
        public bool DeletePending;

        [MarshalAs(UnmanagedType.U1)]
        public bool Directory;
    }

    public static bool TryGetClusterSizeBytes(string anyPathOnVolume, out long clusterSizeBytes)
    {
        clusterSizeBytes = 0;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(anyPathOnVolume));
            if (string.IsNullOrWhiteSpace(root)) return false;

            if (!GetDiskFreeSpaceW(root, out var spc, out var bps, out _, out _))
            {
                return false;
            }

            clusterSizeBytes = (long)spc * bps;
            return clusterSizeBytes > 0;
        }
        catch
        {
            return false;
        }
    }

    public static long RoundUpToCluster(long bytes, long clusterSizeBytes)
    {
        if (bytes <= 0) return 0;
        if (clusterSizeBytes <= 0) return bytes;

        var rem = bytes % clusterSizeBytes;
        return rem == 0 ? bytes : (bytes + (clusterSizeBytes - rem));
    }

    public static long GetFileSizeOnDisk(string filePath)
    {
        // Prefer AllocationSize because it reflects actual allocated clusters.
        try
        {
            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                options: FileOptions.None);

            if (GetFileInformationByHandleEx(
                fs.SafeFileHandle,
                FILE_INFO_BY_HANDLE_CLASS.FileStandardInfo,
                out var info,
                (uint)Marshal.SizeOf<FILE_STANDARD_INFO>()))
            {
                return Math.Max(0, info.AllocationSize);
            }
        }
        catch
        {
            // ignore and fall back
        }

        // Fallback: GetCompressedFileSizeW.
        try
        {
            const uint INVALID_FILE_SIZE = 0xFFFFFFFF;
            var low = GetCompressedFileSizeW(filePath, out var high);
            if (low == INVALID_FILE_SIZE)
            {
                var err = Marshal.GetLastWin32Error();
                if (err != 0)
                {
                    throw new Win32Exception(err);
                }
            }

            return (long)(((ulong)high << 32) | low);
        }
        catch
        {
            // Final fallback: logical size.
            try { return new FileInfo(filePath).Length; } catch { return 0; }
        }
    }
}
