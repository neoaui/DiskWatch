namespace DiskWatch.Core;

public enum ChangeKind { Added, Removed, Changed }

/// <summary>一个文件的变化。大小均为分配空间（与“占用”口径一致）。</summary>
public sealed record FileChange(string Directory, string Name, ChangeKind Kind, long BeforeBytes, long AfterBytes)
{
    public long Delta => AfterBytes - BeforeBytes;
    public string Path => FileIndex.Join(Directory, Name);
}

/// <summary>增长分级：幅度（与 11 类“原因分类”并存）。</summary>
public enum GrowthGrade { None, Shrink, Minor, Rapid, Significant, New, Removed }

/// <summary>分级阈值，默认参照 LightC：显著增长 &gt; 300 MiB；快速增长：变化 &gt; 50 MiB 且增长率 &gt; 10%。</summary>
public sealed record GrowthThresholds(long SignificantBytes = 300L << 20, long RapidBytes = 50L << 20, double RapidPercent = 10)
{
    public static GrowthThresholds From(Settings s) => new((long)(s.GrowthSignificantMiB * 1048576), (long)(s.GrowthRapidMiB * 1048576), s.GrowthRapidPercent);
}

public static class Growth
{
    /// <summary>
    /// 分级规则：新增文件 → 新增（蓝）；变化 &gt; 显著阈值 → 显著增长（红）；变化 &gt; 快速阈值且增长率 &gt; 百分比（原来为 0 视为无穷大）→ 快速增长（橙）；
    /// 其余增长 → 轻微增长（黄）；变小 → 减少；不变 → 无。阈值为“严格大于”。
    /// </summary>
    public static GrowthGrade Grade(long before, long after, GrowthThresholds t, bool newFile = false)
    {
        if (newFile) return GrowthGrade.New;
        long delta = after - before;
        if (delta > t.SignificantBytes) return GrowthGrade.Significant;
        if (delta > t.RapidBytes && (before <= 0 || delta * 100.0 / before > t.RapidPercent)) return GrowthGrade.Rapid;
        if (delta > 0) return GrowthGrade.Minor;
        return delta < 0 ? GrowthGrade.Shrink : GrowthGrade.None;
    }
    public static GrowthGrade Grade(FileChange c, GrowthThresholds t) => c.Kind switch
    {
        ChangeKind.Added => GrowthGrade.New,
        ChangeKind.Removed => GrowthGrade.Removed,
        _ => Grade(c.BeforeBytes, c.AfterBytes, t)
    };
    public static string Name(GrowthGrade g) => g switch
    {
        GrowthGrade.Significant => "显著增长", GrowthGrade.Rapid => "快速增长", GrowthGrade.Minor => "轻微增长",
        GrowthGrade.New => "新增", GrowthGrade.Removed => "删除", GrowthGrade.Shrink => "减少", _ => "无变化"
    };
}

public static class FileDiff
{
    private sealed class KeyComparer : IEqualityComparer<(int, string)>
    {
        public bool Equals((int, string) a, (int, string) b) => a.Item1 == b.Item1 && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((int, string) k) => HashCode.Combine(k.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item2));
    }

    /// <summary>
    /// 对比两份文件级快照：按“目录 + 文件名”（不区分大小写）匹配。只在一边出现的为新增 / 删除（改名、移动视为删除 + 新增）；
    /// 两边都有且大小或修改时间不同的为变更。
    /// </summary>
    public static List<FileChange> Compare(FileIndex before, FileIndex after)
    {
        if (before.Context?.Matches(after.Context) != true
            || !Classifier.Normalize(before.Root).Equals(Classifier.Normalize(before.Context.Root), StringComparison.OrdinalIgnoreCase)
            || !Classifier.Normalize(after.Root).Equals(Classifier.Normalize(after.Context!.Root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("两次扫描的范围或完整性不一致，不能判断文件新增或删除。");
        ValidatePaths(before); ValidatePaths(after);
        var dirMap = new Dictionary<string, int>(after.Directories.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < after.Directories.Count; i++) dirMap.TryAdd(Classifier.Normalize(after.Directories[i]), i);
        var remaining = new Dictionary<(int, string), FileRow>(after.Files.Count, new KeyComparer());
        foreach (var a in after.Files) remaining[(a.Directory, a.Name)] = a;
        var changes = new List<FileChange>();
        foreach (var b in before.Files)
        {
            var dir = before.Directories[b.Directory];
            if (dirMap.TryGetValue(Classifier.Normalize(dir), out var ad) && remaining.Remove((ad, b.Name), out var a))
            {
                if (a.AllocatedBytes != b.AllocatedBytes || a.LogicalBytes != b.LogicalBytes || a.ModifiedSeconds != b.ModifiedSeconds)
                    changes.Add(new(after.Directories[ad], a.Name, ChangeKind.Changed, b.AllocatedBytes, a.AllocatedBytes));
            }
            else changes.Add(new(dir, b.Name, ChangeKind.Removed, b.AllocatedBytes, 0));
        }
        foreach (var a in remaining.Values) changes.Add(new(after.Directories[a.Directory], a.Name, ChangeKind.Added, 0, a.AllocatedBytes));
        return changes;
    }

    private static void ValidatePaths(FileIndex index)
    {
        var paths = new HashSet<(int, string)>(new KeyComparer());
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in index.Directories)
            if (!dirs.Add(Classifier.Normalize(d))) throw new InvalidDataException("存在大小写冲突目录，无法安全比较。");
        foreach (var f in index.Files)
            if ((uint)f.Directory >= index.Directories.Count || !paths.Add((f.Directory, f.Name))) throw new InvalidDataException("存在重复或大小写冲突文件，无法安全比较。");
    }
}

/// <summary>变化明细中的一个目录（任意层级，不受 4 层统计桶限制）。</summary>
public sealed class ChangeNode
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public ChangeNode? Parent { get; init; }
    public List<ChangeNode> Children { get; } = [];
    /// <summary>直接位于此目录的文件变化。</summary>
    public List<FileChange> Files { get; } = [];
    public long OwnBefore, OwnAfter;
    public long BeforeBytes { get; internal set; }
    public long AfterBytes { get; internal set; }
    public long Delta => AfterBytes - BeforeBytes;
    public int Added { get; internal set; }
    public int Removed { get; internal set; }
    public int Changed { get; internal set; }
    public bool ExistedBefore { get; internal set; }
    public bool ExistsAfter { get; internal set; }
}

public sealed record ChangeSummary(int Added, int Changed, int Removed, int SignificantDirectories, int RapidDirectories, long NetBytes)
{
    public string Text => $"新增 {Added:N0}，变更 {Changed:N0}，删除 {Removed:N0}，发现 {SignificantDirectories} 个显著增长目录，{RapidDirectories} 个快速增长目录；净变化 {Labels.SignedSize(NetBytes)}";
}

public static class ChangeTree
{
    public static ChangeNode Build(string root, FileIndex before, FileIndex after, IReadOnlyList<FileChange> changes)
    {
        if (before.Directories.Concat(after.Directories).Any(p => p.Count(c => c == '\\') > 256)) throw new InvalidDataException("目录层级超过安全展示上限。");
        var rootKey = Classifier.Normalize(root);
        var top = new ChangeNode { Path = root, Name = root };
        var nodes = new Dictionary<string, ChangeNode>(StringComparer.OrdinalIgnoreCase) { [rootKey] = top };
        ChangeNode Node(string path)
        {
            var key = Classifier.Normalize(path);
            if (nodes.TryGetValue(key, out var hit)) return hit;
            if (!Classifier.Under(key, rootKey) || key.Length <= rootKey.Length) return top;
            var parentPath = System.IO.Path.GetDirectoryName(key.Length == 2 && key[1] == ':' ? key + "\\" : key) ?? root;
            var parent = Node(parentPath);
            var node = new ChangeNode { Path = key, Name = key[(key.LastIndexOf('\\') + 1)..], Parent = parent };
            parent.Children.Add(node); nodes[key] = node; return node;
        }
        var beforeNodes = before.Directories.Select(Node).ToArray();
        foreach (var n in beforeNodes) n.ExistedBefore = true;
        var afterNodes = after.Directories.Select(Node).ToArray();
        foreach (var n in afterNodes) n.ExistsAfter = true;
        foreach (var f in before.Files) beforeNodes[f.Directory].OwnBefore += f.AllocatedBytes;
        foreach (var f in after.Files) afterNodes[f.Directory].OwnAfter += f.AllocatedBytes;
        foreach (var c in changes) Node(c.Directory).Files.Add(c);
        Finish(top);
        top.ExistedBefore = true; top.ExistsAfter = true;
        return top;
    }

    private static void Finish(ChangeNode node)
    {
        long before = node.OwnBefore, after = node.OwnAfter; int added = 0, removed = 0, changed = 0;
        foreach (var f in node.Files) { if (f.Kind == ChangeKind.Added) added++; else if (f.Kind == ChangeKind.Removed) removed++; else changed++; }
        foreach (var child in node.Children)
        {
            Finish(child);
            before += child.BeforeBytes; after += child.AfterBytes; added += child.Added; removed += child.Removed; changed += child.Changed;
            if (child.ExistedBefore) node.ExistedBefore = true;
            if (child.ExistsAfter) node.ExistsAfter = true;
        }
        node.BeforeBytes = before; node.AfterBytes = after; node.Added = added; node.Removed = removed; node.Changed = changed;
        node.Children.Sort((a, b) => b.Delta.CompareTo(a.Delta));
        node.Files.Sort((a, b) => b.Delta.CompareTo(a.Delta));
    }

    /// <summary>
    /// 汇总句。显著 / 快速增长目录只计“最深一层”：某目录达到该级别，但它的子目录都没达到，才计 1 个，避免同一处增长被各级父目录重复计数。
    /// </summary>
    public static ChangeSummary Summarize(ChangeNode root, GrowthThresholds t)
    {
        int significant = 0, rapid = 0;
        (bool Significant, bool Rapid) Walk(ChangeNode n, bool isRoot)
        {
            bool descendantSignificant = false, descendantRapid = false;
            foreach (var c in n.Children) { var result = Walk(c, false); descendantSignificant |= result.Significant; descendantRapid |= result.Rapid; }
            var g = Growth.Grade(n.BeforeBytes, n.AfterBytes, t);
            if (!isRoot && g == GrowthGrade.Significant && !descendantSignificant) significant++;
            if (!isRoot && g == GrowthGrade.Rapid && !descendantRapid && !descendantSignificant) rapid++;
            return (descendantSignificant || g == GrowthGrade.Significant, descendantRapid || g == GrowthGrade.Rapid);
        }
        Walk(root, true);
        return new(root.Added, root.Changed, root.Removed, significant, rapid, root.Delta);
    }

    /// <summary>按路径找到节点（用于从目录列表直接跳转）。</summary>
    public static ChangeNode? Find(ChangeNode root, string path)
    {
        var key = Classifier.Normalize(path);
        var node = root;
        while (node != null && !Classifier.Normalize(node.Path).Equals(key, StringComparison.OrdinalIgnoreCase))
            node = node.Children.FirstOrDefault(c => Classifier.Under(key, c.Path));
        return node;
    }
}
