using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using DiskWatch.Core;

namespace DiskWatch.Windows;

/// <summary>ModifiedFileTime：最后修改时间（FILETIME，100ns 自 1601 起，UTC）；0 表示未知。</summary>
public sealed record FileMetadata(string Identity, long LogicalBytes, long AllocatedBytes, uint Links, long ModifiedFileTime = 0, ulong FileReference = 0);
public static class NativeMetadata
{
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct HandleInfo
    {
        public uint Attributes; public FileTime Creation, Access, Write; public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StandardInfo
    {
        public long AllocationSize, EndOfFile; public uint NumberOfLinks;
        [MarshalAs(UnmanagedType.U1)] public bool DeletePending;
        [MarshalAs(UnmanagedType.U1)] public bool Directory;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CompressionInfo
    {
        public long CompressedFileSize;
        public ushort Format;
        public byte UnitShift, ChunkShift, ClusterShift, Reserved1, Reserved2, Reserved3;
    }
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCompressionInfo(SafeFileHandle handle, int infoClass, out CompressionInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out StandardInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string path, out uint high);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string path, out ulong available, out ulong total, out ulong free);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string path, StringBuilder name, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string path, StringBuilder root, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceW(string path, out uint sectors, out uint bytes, out uint freeClusters, out uint totalClusters);

    private static string LongPath(string path) => path.StartsWith(@"\\?\") ? path : path.StartsWith(@"\\") ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    public static FileMetadata Read(string path, FileAttributes attributes)
    {
        // FILE_READ_ATTRIBUTES, share read/write/delete, OPEN_EXISTING, OPEN_REPARSE_POINT.
        using var handle = CreateFileW(LongPath(path), 0x80, 7, 0, 3, 0x00200000, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new IOException("文件在扫描期间变为重解析点，未跟随。");
        if (!GetFileInformationByHandleEx(handle, 1, out var standard, (uint)Marshal.SizeOf<StandardInfo>())) throw new Win32Exception(Marshal.GetLastWin32Error());
        long allocated = standard.AllocationSize;
        if (standard.Directory || standard.DeletePending) throw new IOException("文件已成为目录或正在删除。");
        if ((info.Attributes & (uint)(FileAttributes.Compressed | FileAttributes.SparseFile)) != 0)
        {
            // 与身份读取共用同一句柄，避免路径在两次查询之间被替换。
            if (!GetCompressionInfo(handle, 8, out var compressed, (uint)Marshal.SizeOf<CompressionInfo>())) throw new Win32Exception(Marshal.GetLastWin32Error());
            allocated = compressed.CompressedFileSize;
        }
        if (allocated < 0 || standard.EndOfFile < 0) throw new InvalidDataException("文件大小无效。");
        return new($"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}", standard.EndOfFile, allocated, info.Links, ((long)info.Write.High << 32) | info.Write.Low, ((ulong)info.IndexHigh << 32) | info.IndexLow);
    }
    /// <summary>读取目录（如卷根）的完整 64 位文件引用（含序列号）；只读，不跟随重解析点。</summary>
    public static ulong ReadDirectoryReference(string path)
    {
        // FILE_READ_ATTRIBUTES, share read/write/delete, OPEN_EXISTING, BACKUP_SEMANTICS | OPEN_REPARSE_POINT.
        using var handle = CreateFileW(LongPath(path), 0x80, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((info.Attributes & (uint)FileAttributes.Directory) == 0) throw new IOException("不是目录：" + path);
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new IOException("目录为重解析点，未跟随：" + path);
        return ((ulong)info.IndexHigh << 32) | info.IndexLow;
    }
    public static VolumeSample Sample(string path)
    {
        var root = new StringBuilder(1024);
        if (!GetVolumePathNameW(Path.GetFullPath(path), root, (uint)root.Capacity)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetDiskFreeSpaceExW(root.ToString(), out _, out var total, out var free)) throw new Win32Exception(Marshal.GetLastWin32Error());
        // Whole-volume capacity, rather than quota-limited caller capacity where available.
        if (GetDiskFreeSpaceW(root.ToString(), out var sectors, out var bytes, out _, out var clusters)) total = (ulong)sectors * bytes * clusters;
        var id = new StringBuilder(1024);
        if (!GetVolumeNameForVolumeMountPointW(root.ToString(), id, (uint)id.Capacity)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(id.ToString(), root.ToString(), DateTimeOffset.UtcNow, checked((long)total), checked((long)free));
    }
}
