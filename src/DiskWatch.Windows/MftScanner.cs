using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using DiskWatch.Core;

namespace DiskWatch.Windows;

/// <summary>从一条 MFT 文件记录中解析出的主数据流大小。</summary>
/// <summary>ModifiedFileTime 取自 $STANDARD_INFORMATION 的“修改时间”（FILETIME）；没有时为 0。</summary>
public sealed record MftFileSizes(bool InUse, bool IsDirectory, bool IsBaseRecord, int Links, long LogicalBytes, long AllocatedBytes, bool NeedsFallback, long ModifiedFileTime = 0, ushort SequenceNumber = 0);

/// <summary>纯函数：解析 NTFS 文件记录（FILE 记录）。不访问磁盘，便于用合成数据测试。</summary>
public static class MftRecordParser
{
    public const uint StandardInformation = 0x10, AttributeList = 0x20, DataAttribute = 0x80, EndMarker = 0xFFFFFFFF;
    private static int U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o, 2));
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
    private static long I64(byte[] b, int o) => BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(o, 8));

    /// <summary>
    /// 还原更新序列（fixup）：每个扇区最后两个字节在磁盘上被替换为更新序列号，原值保存在更新序列数组里。
    /// 仅接受原始带 USA 的记录，固定 512 字节 stride。先完整验证再修改；不可重复调用或猜测已经还原的输入。
    /// </summary>
    public static void ApplyFixups(byte[] record, int length, int sectorSize = 512)
    {
        if (length < 512 || length > record.Length || length % 512 != 0 || sectorSize != 512) throw new InvalidDataException("无效的原始记录 / USA stride。");
        int usaOffset = U16(record, 4), usaCount = U16(record, 6);
        if (usaCount != length / 512 + 1 || usaOffset < 0x30 || usaOffset + usaCount * 2 > Math.Min(510, U16(record, 0x14))) throw new InvalidDataException("无效的 USA 范围。");
        var usn = U16(record, usaOffset);
        for (int i = 1; i < usaCount; i++)
        {
            int end = i * sectorSize - 2;
            if (U16(record, end) != usn) throw new InvalidDataException("USA 校验不一致：损坏或非原始记录。");
        }
        for (int i = 1; i < usaCount; i++) record.AsSpan(usaOffset + i * 2, 2).CopyTo(record.AsSpan(i * 512 - 2, 2));
    }

    /// <summary>
    /// 读取未命名 $DATA 属性：常驻数据的逻辑大小为值长度、分配空间按 0 计（数据存放在 MFT 记录内，资源管理器“占用空间”也显示 0）；
    /// 非常驻数据取 DataSize 和 AllocatedSize，压缩或稀疏文件取 CompressedSize（与 GetCompressedFileSize 一致）。
    /// 命名数据流（备用数据流）不计，与目录遍历只统计主数据流一致。找不到主数据流（例如在属性列表的扩展记录里）时标记 NeedsFallback。
    /// </summary>
    public static MftFileSizes Parse(byte[] record, int length)
    {
        if (length < 48 || length > record.Length || record[0] != (byte)'F' || record[1] != (byte)'I' || record[2] != (byte)'L' || record[3] != (byte)'E')
            throw new InvalidDataException("不是有效的 MFT 文件记录。");
        int flags = U16(record, 0x16);
        bool inUse = (flags & 1) != 0, directory = (flags & 2) != 0;
        int links = U16(record, 0x12);
        bool isBase = BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(0x20, 8)) == 0;
        uint usedLength = U32(record, 0x18);
        if (usedLength > length || usedLength < 48) throw new InvalidDataException("MFT 已用长度越界。");
        int used = (int)usedLength;
        int offset = U16(record, 0x14);
        if (offset < 48 || offset % 8 != 0 || offset > used - 4) throw new InvalidDataException("MFT 属性起点无效。");
        long? logical = null, allocated = null; bool fallback = false; long modified = 0;
        bool ended = false;
        while (offset <= used - 4)
        {
            uint type = U32(record, offset);
            if (type == EndMarker) { ended = true; break; }
            if (offset > used - 16) throw new InvalidDataException("MFT 属性头截断。");
            uint rawLength = U32(record, offset + 4);
            if (rawLength < 24 || rawLength > used - offset || rawLength % 8 != 0) throw new InvalidDataException("MFT 属性长度无效。");
            int len = (int)rawLength;
            byte form = record[offset + 8];
            if (form > 1 || (form == 1 && len < 0x40)) throw new InvalidDataException("MFT 属性形式无效。");
            bool unnamed = record[offset + 9] == 0;
            int nameOffset = U16(record, offset + 10), nameBytes = record[offset + 9] * 2;
            if (!unnamed && (nameOffset < (form == 0 ? 24 : 64) || nameBytes > len - nameOffset)) throw new InvalidDataException("属性名称越界。");
            if (form == 0)
            {
                int valueOffset = U16(record, offset + 0x14); uint valueLength = U32(record, offset + 0x10);
                if (valueOffset < 24 || valueOffset > len || valueLength > len - valueOffset || (!unnamed && valueOffset < nameOffset + nameBytes)) throw new InvalidDataException("常驻属性值越界。");
                if (type == StandardInformation && valueLength >= 16) modified = Math.Max(0, I64(record, offset + valueOffset + 8));
            }
            if (type == AttributeList) fallback = true; // 不猜测扩展记录布局。
            if (type == DataAttribute && unnamed && logical == null)
            {
                if (record[offset + 8] == 0) { logical = U32(record, offset + 0x10); allocated = 0; }
                else if (len < 0x40 || I64(record, offset + 0x10) != 0) fallback = true;
                else
                {
                    int attributeFlags = U16(record, offset + 0x0C);
                    logical = I64(record, offset + 0x30);
                    bool special = (attributeFlags & 0x80FF) != 0;
                    if ((attributeFlags & 0xFF) > 1 || (special && len < 0x48)) fallback = true;
                    allocated = special && len >= 0x48 ? I64(record, offset + 0x40) : I64(record, offset + 0x28);
                    if (logical < 0 || allocated < 0) throw new InvalidDataException("MFT 大小为负数。");
                }
            }
            offset += len;
        }
        if (!ended) throw new InvalidDataException("MFT 缺少属性结束标记。");
        if (!directory && logical == null) fallback = true;
        return new(inUse, directory, isBase, links, logical ?? 0, allocated ?? 0, fallback, modified, (ushort)U16(record, 0x10));
    }
}

public sealed record UsnEntry(ulong FileReference, ulong ParentReference, FileAttributes Attributes, string Name);

/// <summary>纯函数：解析 FSCTL_ENUM_USN_DATA 输出缓冲区（USN_RECORD_V2）。</summary>
public static class UsnRecordParser
{
    /// <summary>把记录追加到 output，返回下一次枚举的起始文件引用号（缓冲区前 8 字节）。</summary>
    public static ulong Parse(byte[] buffer, int length, List<UsnEntry> output)
    {
        if (length < 8 || length > buffer.Length) throw new InvalidDataException("USN 缓冲区截断。");
        ulong next = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(0, 8));
        int offset = 8;
        while (offset < length)
        {
            if (length - offset < 60) throw new InvalidDataException("USN 记录头截断。");
            int recordLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
            if (recordLength < 60 || recordLength > length - offset || recordLength % 8 != 0) throw new InvalidDataException("USN 记录长度无效。");
            if (BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 4, 2)) != 2) throw new InvalidDataException("不支持的 USN 记录版本。");
            if (BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 4, 2)) == 2)
            {
                var frn = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 8, 8));
                var parent = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 16, 8));
                var attributes = (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 52, 4));
                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 56, 2));
                int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 58, 2));
                if (nameOffset < 60 || nameLength == 0 || (nameLength & 1) != 0 || nameLength > recordLength - nameOffset) throw new InvalidDataException("USN 名称范围无效。");
                var name = Encoding.Unicode.GetString(buffer, offset + nameOffset, nameLength);
                bool rootName = name == "." && (frn & MftAggregator.IndexMask) == MftAggregator.RootIndex && (attributes & FileAttributes.Directory) != 0;
                if (name.IndexOfAny(['\\', '/', '\0', ':']) >= 0 || name == ".." || (name == "." && !rootName)) throw new InvalidDataException("USN 文件名无效。");
                output.Add(new(frn, parent, attributes, name));
            }
            offset += recordLength;
        }
        return next;
    }
}

/// <summary>
/// 把 MFT 枚举得到的文件条目汇总成与目录遍历相同口径的快照：4 层目录统计桶、≥50 MiB 大文件、硬链接只计一次（每个 MFT 记录一条）、
/// 不跟随重解析点（重解析目录下的内容不计）、跳过 NTFS 元数据文件、排除目录、离线文件记为读取问题。
/// </summary>
public static class MftAggregator
{
    public const ulong IndexMask = 0x0000FFFFFFFFFFFF, RootIndex = 5, FirstUserIndex = 24;
    private static string Join(string parent, string name) => parent.EndsWith('\\') ? parent + name : parent + "\\" + name;

    public static void Aggregate(Snapshot snapshot, string volumeRoot, string root, IReadOnlyList<UsnEntry> entries, Func<UsnEntry, string, FileMetadata?> sizes,
        Classifier classifier, Settings settings, IProgress<ScanProgress>? progress, CancellationToken token, FileIndex? details = null, ulong? rootReference = null)
    {
        var map = new Dictionary<ulong, UsnEntry>(entries.Count);
        foreach (var e in entries)
            if (!map.TryAdd(e.FileReference, e)) throw new InvalidDataException("MFT 枚举出现重复文件引用。");
        var dirs = new Dictionary<ulong, string?>();
        string? DirectoryPath(ulong index)
        {
            var chain = new List<ulong>(); string? basePath; var current = index;
            while (true)
            {
                if (dirs.TryGetValue(current, out var hit)) { basePath = hit; break; }
                // FSCTL_ENUM_USN_DATA 通常不返回根目录（记录 5）本身：根目录的完整引用由调用方用句柄读取后传入，仍按含序列号的完整值精确匹配。
                if ((current & IndexMask) == RootIndex && ((rootReference.HasValue && current == rootReference.Value) || (map.TryGetValue(current, out var rootEntry) && (rootEntry.Attributes & FileAttributes.Directory) != 0))) { basePath = volumeRoot; dirs[current] = volumeRoot; break; }
                if ((current & IndexMask) < FirstUserIndex || !map.TryGetValue(current, out var entry) || chain.Count >= 256 || chain.Contains(current)) { basePath = null; dirs[current] = null; break; }
                chain.Add(current); current = entry.ParentReference;
            }
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var entry = map[chain[i]];
                string? path = basePath == null || (entry.Attributes & FileAttributes.Directory) == 0 || (entry.Attributes & FileAttributes.ReparsePoint) != 0 ? null : Join(basePath, entry.Name);
                dirs[chain[i]] = path; basePath = path;
            }
            return basePath;
        }
        var wholeVolume = Classifier.Normalize(root).Equals(Classifier.Normalize(volumeRoot), StringComparison.OrdinalIgnoreCase);
        var buckets = new Dictionary<string, (string Path, Category Category, long Allocated, long Logical, long Files)>(StringComparer.OrdinalIgnoreCase);
        var largest = new PriorityQueue<LargeFile, long>();
        long allocated = 0, seen = 0;
        var apps = new AppUsageCollector(settings.AppRules);
        void Issue(string path, string reason) { snapshot.Errors++; if (snapshot.Issues.Count < 100) snapshot.Issues.Add(new(path, reason)); }
        try { foreach (var e in entries)
        {
            token.ThrowIfCancellationRequested();
            if ((++seen & 4095) == 0) progress?.Report(new(root, "MFT 汇总", snapshot.Files, snapshot.Errors, allocated));
            if ((e.FileReference & IndexMask) < FirstUserIndex) continue;
            var parent = DirectoryPath(e.ParentReference);
            if (parent == null) { Issue(root, $"无法确认父目录 / 被跳过的路径：{e.FileReference:X16}"); continue; }
            var full = Join(parent, e.Name);
            if (!wholeVolume && !Classifier.Under(full, root)) continue;
            if (Classifier.Normalize(full).Equals(Classifier.Normalize(root), StringComparison.OrdinalIgnoreCase)) continue;
            var excluded = settings.ExcludedPaths.FirstOrDefault(x => Classifier.Under(full, x));
            if (excluded != null) { if (Classifier.Normalize(full).Equals(Classifier.Normalize(excluded), StringComparison.OrdinalIgnoreCase)) Issue(full, "用户排除范围"); continue; }
            if ((e.Attributes & FileAttributes.ReparsePoint) != 0) { snapshot.ReparsePointsSkipped++; continue; }
            if ((e.Attributes & FileAttributes.Directory) != 0) continue;
            if ((e.Attributes & FileAttributes.Offline) != 0) { Issue(full, "离线文件未读取，避免触发下载"); continue; }
            FileMetadata? meta;
            try { meta = sizes(e, full); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidDataException) { Issue(full, ex.Message); continue; }
            if (meta == null) { Issue(full, "未取得文件元数据"); continue; }
            if (meta.Links > 1) snapshot.HardLinksSkipped += meta.Links - 1;
            var decision = classifier.Classify(full);
            var bucketPath = Scanner.BucketPath(root, parent);
            var key = $"{(int)decision.Category}|{bucketPath}";
            buckets.TryGetValue(key, out var b);
            buckets[key] = (bucketPath, decision.Category, b.Allocated + meta.AllocatedBytes, b.Logical + meta.LogicalBytes, b.Files + 1);
            snapshot.Files++; allocated += meta.AllocatedBytes;
            details?.AddFileTime(parent, e.Name, meta.LogicalBytes, meta.AllocatedBytes, meta.ModifiedFileTime);
            apps.Add(parent, e.Name, meta.AllocatedBytes, decision.Application);
            if (meta.LogicalBytes >= 50L * 1024 * 1024)
            {
                largest.Enqueue(new(full, meta.LogicalBytes, meta.AllocatedBytes, decision.Category, decision.Confidence, decision.Evidence), meta.AllocatedBytes);
                if (largest.Count > 100) largest.Dequeue();
            }
        }
        } finally {
        snapshot.Buckets = buckets.Values.Select(x => new Bucket(x.Path, x.Category, x.Allocated, x.Logical, x.Files)).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Category).ToList();
        snapshot.LargestFiles = largest.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.AllocatedBytes).ToList();
        snapshot.Apps = apps.ToList(); snapshot.AppRulesKey = AppAttribution.RulesKey(settings);
        }
    }
}

/// <summary>
/// MFT 快速扫描：用 FSCTL_ENUM_USN_DATA 一次性枚举卷上所有文件记录（名称、父目录、属性），再用带身份校验的句柄元数据接口取得大小。
/// 原始记录解析器仅用于合成格式验证，不接入生产扫描。需要管理员权限打开卷句柄；程序绝不自行提权。任何前提不满足或出错都返回 null，由调用方改用目录遍历。
/// </summary>
public static class MftScanner
{
    private const uint FsctlEnumUsnData = 0x000900B3;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, nint overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? name, int nameSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder fileSystem, int fileSystemSize);

    public static void ValidateIdentity(ulong expected, ulong actual)
    {
        if (expected != actual) throw new InvalidDataException("完整文件引用不一致：扫描期间文件被替换或 MFT 槽位复用。");
    }

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>检查能否使用 MFT 快速扫描；不能时返回原因（中文）。</summary>
    public static string? Unavailable(string root)
    {
        if (!IsElevated()) return "当前未以管理员身份运行（程序不会自行提权）";
        var volumeRoot = Path.GetPathRoot(root);
        if (string.IsNullOrEmpty(volumeRoot) || volumeRoot.StartsWith(@"\\")) return "扫描范围不是本地盘符路径";
        try { if (!NativeMetadata.Sample(root).Root.Equals(volumeRoot, StringComparison.OrdinalIgnoreCase)) return "扫描范围位于挂载点内"; }
        catch (Exception ex) { return "无法确认卷：" + ex.Message; }
        var fs = new StringBuilder(32);
        if (!GetVolumeInformationW(volumeRoot, null, 0, out _, out _, out _, fs, fs.Capacity)) return "无法读取文件系统类型";
        return fs.ToString().Equals("NTFS", StringComparison.OrdinalIgnoreCase) ? null : $"文件系统为 {fs}，不是 NTFS";
    }

    public static Snapshot? TryScan(string root, Classifier classifier, Settings settings, string ruleVersion, IProgress<ScanProgress>? progress, CancellationToken token, out string? note)
    {
        root = Path.GetFullPath(root);
        note = Unavailable(root);
        if (note != null) return null;
        var volumeRoot = Path.GetPathRoot(root)!;
        if (!Classifier.Normalize(root).Equals(Classifier.Normalize(volumeRoot), StringComparison.OrdinalIgnoreCase) || settings.ExcludedPaths.Count != 0)
        { note = "MFT 单路径枚举不能保证子目录 / 排除范围的硬链接覆盖，使用目录遍历"; return null; }
        var snapshot = new Snapshot { Root = root, Started = DateTimeOffset.UtcNow, RuleVersion = ruleVersion + "+safe2+mft", CoverageKey = ScanCoverage.Key(settings), ScanMethod = "MFT 快速扫描" };
        try
        {
            snapshot.Before = NativeMetadata.Sample(root); snapshot.VolumeId = snapshot.Before.VolumeId;
            snapshot.IsWholeVolume = Classifier.Normalize(root).Equals(Classifier.Normalize(snapshot.Before.Root), StringComparison.OrdinalIgnoreCase);
            for (var ancestor = new DirectoryInfo(root); ancestor != null; ancestor = ancestor.Parent)
                if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("扫描根目录或其父目录为重解析点，请选择真实目标目录。");
            // GENERIC_READ 打开卷，只读；非管理员会在这里被拒绝。
            using var volume = CreateFileW(@"\\.\" + volumeRoot.TrimEnd('\\'), 0x80000000, 3, 0, 3, 0, 0);
            if (volume.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var entries = new List<UsnEntry>(1 << 16);
            var input = new byte[24]; var output = new byte[1 << 20];
            BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(16, 8), long.MaxValue);
            ulong start = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(0, 8), start);
                if (!DeviceIoControl(volume, FsctlEnumUsnData, input, input.Length, output, output.Length, out var returned, 0))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 38) break; // ERROR_HANDLE_EOF：枚举结束
                    throw new Win32Exception(error);
                }
                var next = UsnRecordParser.Parse(output, returned, entries);
                if (returned <= 8 || next <= start) throw new InvalidDataException("USN 枚举游标未前进。");
                if (entries.Count > settings.MaxTrackedFileIds) throw new InvalidDataException("MFT 枚举达到资源上限，改用目录遍历。");
                start = next;
                progress?.Report(new(root, "正在读取文件目录", entries.Count, 0, 0));
            }
            FileMetadata? Sizes(UsnEntry entry, string path)
            {
                // FSCTL 原始记录的 USA 还原状态未验证：生产路径不猜测、不解析原始 DATA。
                // 句柄元数据读取保留完整 FRN（含序列号），替换/复用时拒绝旧路径归因。
                var meta = NativeMetadata.Read(path, entry.Attributes);
                ValidateIdentity(entry.FileReference, meta.FileReference);
                return meta;
            }
            var details = settings.FileDetailRetention > 0 ? new FileIndex { Root = root } : null;
            var rootReference = NativeMetadata.ReadDirectoryReference(volumeRoot);
            if ((rootReference & MftAggregator.IndexMask) != MftAggregator.RootIndex) throw new InvalidDataException("卷根目录的文件引用不是 NTFS 根记录。");
            MftAggregator.Aggregate(snapshot, volumeRoot, root, entries, Sizes, classifier, settings, progress, token, details, rootReference);
            snapshot.FileDetails = details;
            snapshot.Status = "Complete";
        }
        catch (OperationCanceledException) { snapshot.Status = "Cancelled"; }
        catch (Exception ex) { note = "读取 MFT 失败：" + ex.Message; return null; }
        try { snapshot.After = NativeMetadata.Sample(root); }
        catch (Exception ex) { snapshot.Errors++; snapshot.Issues.Add(new(root, "结束容量采样失败：" + ex.Message)); }
        if (snapshot.After != null && snapshot.Before != null && snapshot.After.VolumeId != snapshot.Before.VolumeId) { snapshot.Status = "Failed"; snapshot.Errors++; snapshot.Issues.Add(new(root, "扫描期间卷标识发生变化")); }
        ScanCoverage.Finish(snapshot);
        if (!snapshot.IsComparable) snapshot.FileDetails = null;
        snapshot.Finished = DateTimeOffset.UtcNow;
        return snapshot;
    }
}
