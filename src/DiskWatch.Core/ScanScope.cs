namespace DiskWatch.Core;

/// <summary>本机一个可选的磁盘（由界面层枚举后传入，便于测试）。</summary>
public sealed record DriveEntry(string Root, string Label, string FileSystem, long TotalBytes, long FreeBytes, bool Removable)
{
    public string Describe()
    {
        var name = string.IsNullOrWhiteSpace(Label) ? (Removable ? "可移动磁盘" : "本地磁盘") : Label.Trim();
        return $"{name} · {FileSystem} · 共 {Labels.Size(TotalBytes)} · 剩余 {Labels.Size(FreeBytes)}" + (Removable ? " · 可移动" : "");
    }
}

/// <summary>扫描范围列表中的一项：磁盘或自定义文件夹。</summary>
public sealed class ScopeItem
{
    public required string Path { get; init; }
    public bool IsDrive { get; init; }
    public bool Exists { get; init; }
    public bool Checked { get; set; }
    public string Description { get; init; } = "";
    /// <summary>检测到的磁盘只能勾选/取消；文件夹和“未找到”的项可以移除。</summary>
    public bool Removable => !IsDrive || !Exists;
}

/// <summary>纯函数：扫描范围勾选列表与 settings.json 中 Roots（绝对路径列表）的互转。不改变字段含义。</summary>
public static class ScanScope
{
    public const string EmptySelection = "请至少选择一个扫描范围（磁盘或文件夹）。";

    /// <summary>规范化：盘符统一为 “C:\”，文件夹去掉末尾分隔符。</summary>
    public static string Normalize(string path)
    {
        var p = path.Trim();
        if (p.Length == 2 && p[1] == ':') p += "\\";
        p = System.IO.Path.GetFullPath(p);
        var root = System.IO.Path.GetPathRoot(p);
        return string.Equals(root, p, StringComparison.OrdinalIgnoreCase) ? p.ToUpperInvariant() : p.TrimEnd('\\');
    }

    public static bool IsDriveRoot(string path)
    {
        var p = Normalize(path); return string.Equals(System.IO.Path.GetPathRoot(p), p, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 合并本机磁盘与已保存的范围：已保存的项保持勾选；检测到但未保存的固定盘、可移动盘显示为未勾选；
    /// 已保存但当前不存在的盘或文件夹标记为“未找到”并保留勾选，不静默丢弃。顺序：磁盘按盘符，其后是文件夹（按保存顺序）。
    /// </summary>
    public static List<ScopeItem> Merge(IEnumerable<DriveEntry> drives, IEnumerable<string> savedRoots, Func<string, bool> folderExists)
    {
        var saved = savedRoots.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var items = new List<ScopeItem>();
        var detected = drives.GroupBy(d => Normalize(d.Root), StringComparer.OrdinalIgnoreCase).Select(g => g.First() with { Root = g.Key }).ToList();
        foreach (var d in detected)
            items.Add(new ScopeItem { Path = d.Root, IsDrive = true, Exists = true, Checked = saved.Contains(d.Root, StringComparer.OrdinalIgnoreCase), Description = d.Describe() });
        foreach (var root in saved.Where(r => IsDriveRoot(r) && !detected.Any(d => d.Root.Equals(r, StringComparison.OrdinalIgnoreCase))))
            items.Add(new ScopeItem { Path = root, IsDrive = true, Exists = false, Checked = true, Description = "未找到（当前没有这个盘或未就绪）" });
        items.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        foreach (var folder in saved.Where(r => !IsDriveRoot(r)))
        {
            bool exists = folderExists(folder);
            items.Add(new ScopeItem { Path = folder, IsDrive = false, Exists = exists, Checked = true, Description = exists ? "文件夹" : "未找到（文件夹不存在或无法访问）" });
        }
        return items;
    }

    /// <summary>添加文件夹（或盘）到列表；已存在则只勾选。返回是否新增。</summary>
    public static bool Add(List<ScopeItem> items, string path, Func<string, bool> folderExists)
    {
        var p = Normalize(path);
        var hit = items.FirstOrDefault(i => i.Path.Equals(p, StringComparison.OrdinalIgnoreCase));
        if (hit != null) { hit.Checked = true; return false; }
        bool drive = IsDriveRoot(p), exists = folderExists(p);
        items.Add(new ScopeItem { Path = p, IsDrive = drive, Exists = exists, Checked = true, Description = exists ? (drive ? "磁盘" : "文件夹") : "未找到" });
        return true;
    }

    /// <summary>勾选的项按列表顺序转成 Roots（去重）。</summary>
    public static List<string> ToRoots(IEnumerable<ScopeItem> items) =>
        items.Where(i => i.Checked).Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>为空时返回提示文字；可以保存时返回 null。</summary>
    public static string? Validate(IReadOnlyCollection<string> roots) => roots.Count == 0 ? EmptySelection : null;

    /// <summary>快捷操作：全选现有磁盘（不含可移动盘和未找到的盘）；文件夹勾选状态不变。</summary>
    public static void SelectAllFixed(IEnumerable<ScopeItem> items, IEnumerable<DriveEntry> drives)
    {
        var fixedRoots = drives.Where(d => !d.Removable).Select(d => Normalize(d.Root)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items.Where(i => i.IsDrive && i.Exists)) i.Checked = fixedRoots.Contains(i.Path);
    }

    /// <summary>快捷操作：只扫 C 盘——只勾 C:\，其他磁盘和文件夹全部取消。</summary>
    public static void OnlySystemDrive(IEnumerable<ScopeItem> items, string systemDrive = "C:\\")
    {
        var c = Normalize(systemDrive);
        foreach (var i in items) i.Checked = i.Path.Equals(c, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>排除目录列表的规范化（去重、保留顺序）。</summary>
    public static List<string> NormalizeList(IEnumerable<string> paths) => paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}