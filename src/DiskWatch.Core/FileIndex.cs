using System.IO.Compression;
using System.Text;

namespace DiskWatch.Core;

/// <summary>文件级快照中的一个文件：所在目录（目录字典下标）、文件名、逻辑大小、分配空间、修改时间（1601 起的秒数）。</summary>
public readonly record struct FileRow(int Directory, string Name, long LogicalBytes, long AllocatedBytes, long ModifiedSeconds);

public sealed record FileComparisonContext(string VolumeId, string Root, string RuleVersion, string CoverageKey, bool Complete)
{
    public static FileComparisonContext From(Snapshot s) => new(s.VolumeId, s.Root, s.RuleVersion, s.CoverageKey, ScanCoverage.Verified(s));
    public bool Matches(FileComparisonContext? other) => Complete && other is { Complete: true }
        && !string.IsNullOrWhiteSpace(VolumeId) && VolumeId == other.VolumeId
        && Classifier.Normalize(Root).Equals(Classifier.Normalize(other.Root), StringComparison.OrdinalIgnoreCase)
        && RuleVersion == other.RuleVersion && CoverageKey.StartsWith(ScanCoverage.Version + ":", StringComparison.Ordinal) && CoverageKey == other.CoverageKey;
}

/// <summary>
/// 文件级快照：用目录字典 + 文件行的紧凑结构保存一次扫描里的全部文件，序列化时目录和文件名做前缀压缩、数字用变长编码，再整体 Brotli 压缩。
/// 只用于“变化明细”对比；原有的目录桶统计不受影响。
/// </summary>
public sealed class FileIndex
{
    private const string Magic = "DWFI1";
    public const int MaxRows = 2_000_000, MaxDirectories = 250_000, MaxCompressedBytes = 64 * 1024 * 1024;
    private const long MaxExpandedBytes = 160L * 1024 * 1024;
    private Dictionary<string, int>? lookup;
    public string Root { get; init; } = "";
    // 由存储层独立的覆盖记录提供；旧 DWFI1 BLOB 本身不构成完整覆盖证明。
    public FileComparisonContext? Context { get; set; }
    public List<string> Directories { get; } = [];
    public List<FileRow> Files { get; } = [];

    public int DirectoryIndex(string directory)
    {
        if (lookup == null) { lookup = new(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < Directories.Count; i++) lookup[Directories[i]] = i; }
        if (!lookup.TryGetValue(directory, out var index)) { index = Directories.Count; Directories.Add(directory); lookup[directory] = index; }
        return index;
    }

    public void Add(string directory, string name, long logicalBytes, long allocatedBytes, DateTime modifiedUtc) =>
        Files.Add(new FileRow(DirectoryIndex(directory), name, logicalBytes, allocatedBytes, ToSeconds(modifiedUtc)));

    /// <summary>FILETIME（100ns，自 1601 起）版本，供 MFT / Win32 直接传入。</summary>
    public void AddFileTime(string directory, string name, long logicalBytes, long allocatedBytes, long fileTime) =>
        Files.Add(new FileRow(DirectoryIndex(directory), name, logicalBytes, allocatedBytes, Math.Max(0, fileTime) / 10_000_000));

    public static long ToSeconds(DateTime utc) => utc <= DateTime.FromFileTimeUtc(0) ? 0 : utc.ToFileTimeUtc() / 10_000_000;
    public static string Join(string directory, string name) => directory.EndsWith('\\') ? directory + name : directory + "\\" + name;
    public string PathOf(FileRow row) => Join(Directories[row.Directory], row.Name);
    public long AllocatedBytes => Files.Sum(f => f.AllocatedBytes);

    private static int Common(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        if (i > 0 && char.IsHighSurrogate(a[i - 1])) i--;
        return i;
    }
    private static long RoundUp(long logical) => checked((logical + 4095) / 4096 * 4096);
    private static long ZigZag(long v) => (v << 1) ^ (v >> 63);
    private static long UnZigZag(long v) => (long)((ulong)v >> 1) ^ -(v & 1);

    public byte[] Serialize()
    {
        if (Files.Count > MaxRows || Directories.Count > MaxDirectories) throw new InvalidDataException("文件明细达到资源上限。");
        long budget = Files.Count * 40L;
        foreach (var d in Directories) Charge(ref budget, d);
        foreach (var f in Files)
        {
            Charge(ref budget, f.Name);
            if ((uint)f.Directory >= Directories.Count || f.LogicalBytes < 0 || f.AllocatedBytes < 0 || f.ModifiedSeconds < 0) throw new InvalidDataException("文件明细字段无效。");
        }
        var order = Enumerable.Range(0, Directories.Count).OrderBy(i => Directories[i], StringComparer.Ordinal).ToArray();
        var remap = new int[Directories.Count];
        for (int k = 0; k < order.Length; k++) remap[order[k]] = k;
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new BinaryWriter(brotli, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic); w.Write(Root);
            w.Write7BitEncodedInt(Directories.Count);
            var previous = "";
            foreach (var i in order) { var d = Directories[i]; int c = Common(previous, d); w.Write7BitEncodedInt(c); w.Write(d[c..]); previous = d; }
            var rows = Files.OrderBy(r => remap[r.Directory]).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
            w.Write7BitEncodedInt(rows.Count);
            int previousDir = 0; var previousName = "";
            foreach (var r in rows)
            {
                int d = remap[r.Directory];
                w.Write7BitEncodedInt(d - previousDir); if (d != previousDir) previousName = ""; previousDir = d;
                int c = Common(previousName, r.Name); w.Write7BitEncodedInt(c); w.Write(r.Name[c..]); previousName = r.Name;
                w.Write7BitEncodedInt64(Math.Max(0, r.LogicalBytes));
                w.Write7BitEncodedInt64(ZigZag(r.AllocatedBytes - RoundUp(Math.Max(0, r.LogicalBytes))));
                w.Write7BitEncodedInt64(Math.Max(0, r.ModifiedSeconds));
            }
        }
        if (output.Length > MaxCompressedBytes) throw new InvalidDataException("压缩明细超过大小上限。");
        return output.ToArray();
    }

    private static void Charge(ref long budget, string text)
    {
        budget = checked(budget + 24L + text.Length * 2L);
        if (text.Length > 32768 || budget > MaxExpandedBytes) throw new InvalidDataException("文件明细展开大小超过上限。");
    }
    private static string ReadText(BinaryReader r)
    {
        int bytes = r.Read7BitEncodedInt();
        if (bytes < 0 || bytes > 131072) throw new InvalidDataException("明细字符串长度无效。");
        var data = r.ReadBytes(bytes);
        if (data.Length != bytes) throw new InvalidDataException("明细字符串截断。");
        return new UTF8Encoding(false, true).GetString(data);
    }
    private static string Expand(BinaryReader r, string previous, ref long budget)
    {
        int prefix = r.Read7BitEncodedInt();
        if ((uint)prefix > previous.Length) throw new InvalidDataException("明细公共前缀越界。");
        var value = previous[..prefix] + ReadText(r); Charge(ref budget, value); return value;
    }

    public static FileIndex Deserialize(byte[] data)
    {
        if (data.Length > MaxCompressedBytes) throw new InvalidDataException("压缩明细超过大小上限。");
        using var input = new BrotliStream(new MemoryStream(data), CompressionMode.Decompress);
        using var r = new BinaryReader(input, Encoding.UTF8);
        if (ReadText(r) != Magic) throw new InvalidDataException("文件级快照格式不正确。");
        var index = new FileIndex { Root = ReadText(r) };
        long budget = 0;
        int dirs = r.Read7BitEncodedInt(); if (dirs < 0 || dirs > MaxDirectories) throw new InvalidDataException("文件级快照目录数异常。");
        var previous = "";
        for (int i = 0; i < dirs; i++) { var d = Expand(r, previous, ref budget); index.Directories.Add(d); previous = d; }
        int count = r.Read7BitEncodedInt(); if (count < 0 || count > MaxRows) throw new InvalidDataException("文件级快照文件数异常。");
        budget += count * 40L;
        if (budget > MaxExpandedBytes) throw new InvalidDataException("文件明细展开大小超过上限。");
        index.Files.Capacity = count;
        int dir = 0; var previousName = "";
        for (int i = 0; i < count; i++)
        {
            int step = r.Read7BitEncodedInt(); if (step < 0 || step > dirs - dir) throw new InvalidDataException("目录增量无效。");
            if (step != 0) previousName = ""; dir += step;
            if ((uint)dir >= (uint)dirs) throw new InvalidDataException("文件级快照目录下标越界。");
            var name = Expand(r, previousName, ref budget); previousName = name;
            long logical = r.Read7BitEncodedInt64();
            if (logical < 0 || logical > long.MaxValue - 4095) throw new InvalidDataException("逻辑大小无效。");
            long allocated;
            try { allocated = checked(UnZigZag(r.Read7BitEncodedInt64()) + RoundUp(logical)); }
            catch (OverflowException ex) { throw new InvalidDataException("分配大小溢出。", ex); }
            long modified = r.Read7BitEncodedInt64();
            if (allocated < 0 || modified < 0) throw new InvalidDataException("明细数值为负。");
            index.Files.Add(new FileRow(dir, name, logical, allocated, modified));
        }
        if (input.ReadByte() != -1) throw new InvalidDataException("明细包含额外数据。");
        return index;
    }
}
