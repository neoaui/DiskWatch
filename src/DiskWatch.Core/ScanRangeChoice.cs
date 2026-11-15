namespace DiskWatch.Core;

/// <summary>
/// 工具栏“开始扫描”旁的范围下拉：默认“全部磁盘”（按设置里已勾选的 Roots），或只扫当前在线的某一个盘。
/// 选择只影响这一次扫描，不写 settings.json。
/// </summary>
public static class ScanRangeChoice
{
    public const string AllKey = "*all*";
    public const string AllLabel = "全部磁盘";

    public sealed record Option(string Key, string Label, string Description)
    {
        public override string ToString() => Label;
    }

    public static string DefaultKey => AllKey;

    public static IReadOnlyList<Option> Options(IEnumerable<DriveEntry> drives)
    {
        var list = new List<Option> { new(AllKey, AllLabel, "按设置里已勾选的扫描范围") };
        foreach (var d in drives
            .GroupBy(x => ScanScope.Normalize(x.Root), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => ScanScope.Normalize(d.Root), StringComparer.OrdinalIgnoreCase))
        {
            var root = ScanScope.Normalize(d.Root);
            list.Add(new Option(root, root, d.Describe()));
        }
        return list;
    }

    /// <summary>把下拉选中项解析成这次要扫的根路径。全部磁盘 / 空 / 未知键 → 已保存的 Roots；在线单盘 → 只扫该盘（即使未勾进设置）。</summary>
    public static List<string> Resolve(string? selectedKey, IEnumerable<string> savedRoots, IEnumerable<DriveEntry> presentDrives)
    {
        var saved = savedRoots.Select(ScanScope.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (string.IsNullOrWhiteSpace(selectedKey) || selectedKey == AllKey) return saved;
        var key = ScanScope.Normalize(selectedKey);
        var present = presentDrives.Select(d => ScanScope.Normalize(d.Root)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return present.Contains(key) ? [key] : saved;
    }

    /// <summary>扫描进行中不可改范围；改了也不会影响已经开始的这次扫描。</summary>
    public static bool CanChange(bool scanning) => !scanning;

    /// <summary>刷新选项后保留原来的选择；该项已不在列表里则回到全部磁盘。</summary>
    public static string Retain(string? currentKey, IEnumerable<Option> options)
    {
        var key = string.IsNullOrWhiteSpace(currentKey) ? AllKey : currentKey;
        var hit = options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return hit?.Key ?? AllKey;
    }
}