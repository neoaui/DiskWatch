namespace DiskWatch.Core;

public enum Category { Windows = 1, SpecialFiles, Vendor, Applications, AppData, Games, Temporary, Personal, Development, Unexplained, Other }

public static class Labels
{
    /// <summary>界面显示的权限状态（扫描覆盖、设置页）。</summary>
    public static string ElevationStatus(bool elevated, bool fastScanEnabled) => elevated
        ? (fastScanEnabled ? "已获得管理员权限，可使用 NTFS 快速扫描" : "已获得管理员权限，快速扫描已关闭，将使用普通扫描")
        : "未获得管理员权限，将使用普通扫描。下次启动时在权限确认窗口点击「是」，即可使用快速扫描";
    // UI wording deliberately centralized pending product copy approval.
    public static string CategoryName(Category c) => c switch
    {
        Category.Windows => "Windows 系统本体", Category.SpecialFiles => "系统特殊文件",
        Category.Vendor => "厂商预装 / 维护软件", Category.Applications => "普通应用本体",
        Category.AppData => "应用缓存与数据", Category.Games => "游戏本体与存档 / 配置",
        Category.Temporary => "临时文件", Category.Personal => "用户个人文件",
        Category.Development => "开发环境", Category.Unexplained => "隐性 / 未解释差额",
        _ => "其他已扫描"
    };
    public static string Size(long bytes) => $"{bytes / 1073741824d:N2} GiB";
    /// <summary>自动单位（B / KiB / MiB / GiB），用于文件级大小。</summary>
    public static string SmartSize(long bytes)
    {
        double a = Math.Abs((double)bytes);
        return a >= 1L << 30 ? $"{bytes / 1073741824d:N2} GiB" : a >= 1L << 20 ? $"{bytes / 1048576d:N1} MiB" : a >= 1024 ? $"{bytes / 1024d:N0} KiB" : $"{bytes} B";
    }
    public static string SignedSize(long bytes) => (bytes > 0 ? "+" : bytes < 0 ? "−" : "") + SmartSize(Math.Abs(bytes));
}

public sealed record VolumeSample(string VolumeId, string Root, DateTimeOffset Time, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;
}
public sealed record Classification(Category Category, int Confidence, string Evidence, string? Application = null);
public sealed record PathRule(string Path, Category Category, bool IncludeChildren = true, string Note = "用户纠正");
public sealed record InstalledApp(string Name, string Directory);
public sealed class ClassificationContext
{
    public string WindowsDirectory { get; set; } = @"C:\Windows";
    public List<string> PersonalRoots { get; set; } = [];
    public List<string> GameRoots { get; set; } = [];
    public List<InstalledApp> Applications { get; set; } = [];
    public List<PathRule> Overrides { get; set; } = [];
}
public sealed record Bucket(string Path, Category Category, long AllocatedBytes, long LogicalBytes, long Files);
public sealed record LargeFile(string Path, long LogicalBytes, long AllocatedBytes, Category Category, int Confidence, string Evidence);
public sealed record ScanIssue(string Path, string Reason);
public sealed class Snapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Root { get; set; } = "";
    public string VolumeId { get; set; } = "";
    public DateTimeOffset Started { get; set; }
    public DateTimeOffset Finished { get; set; }
    public string Status { get; set; } = "Running";
    public string RuleVersion { get; set; } = "1";
    public string CoverageKey { get; set; } = "";
    /// <summary>本次使用的扫描方式（目录遍历 / MFT 快速扫描及回退原因）；旧快照为空，按目录遍历显示。</summary>
    public string ScanMethod { get; set; } = "";
    public bool IsWholeVolume { get; set; }
    public VolumeSample? Before { get; set; }
    public VolumeSample? After { get; set; }
    public long Files { get; set; }
    public long HardLinksSkipped { get; set; }
    public long ReparsePointsSkipped { get; set; }
    public long Errors { get; set; }
    public List<Bucket> Buckets { get; set; } = [];
    public List<LargeFile> LargestFiles { get; set; } = [];
    public List<ScanIssue> Issues { get; set; } = [];
    /// <summary>本次扫描收集的文件级明细（不写入快照 JSON，单独压缩存储）。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public FileIndex? FileDetails { get; set; }
    /// <summary>本次扫描各应用 / 来源的占用（应用排行用；不写入快照 JSON，单独保存在 app_usage 表）。旧快照为空。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<AppUsage>? Apps { get; set; }
    /// <summary>应用归属规则表版本（内置表 + 用户规则）；只对比同一版本下的两次结果。</summary>
    public string AppRulesKey { get; set; } = "";
    public long AllocatedBytes => Buckets.Sum(x => x.AllocatedBytes);
    public long LogicalBytes => Buckets.Sum(x => x.LogicalBytes);
    public long? Residual => IsWholeVolume && After != null ? After.UsedBytes - AllocatedBytes : null;
    public bool IsComparable => Status == "Complete" && Errors == 0;
    public string Coverage => $"{Status switch { "Complete" => Errors > 0 ? "扫描结束（部分区域未覆盖）" : "扫描结束", "Partial" => "扫描结束（部分区域未覆盖）", "Cancelled" => "已取消（部分结果）", "ResourceLimit" => "达到资源上限（部分结果）", "Failed" => "扫描失败", _ => "扫描中" }}；问题 {Errors}；跳过重解析点 {ReparsePointsSkipped}；硬链接去重 {HardLinksSkipped}";
}

public sealed class Settings
{
    public List<string> Roots { get; set; } = [@"C:\", @"D:\", @"E:\", @"F:\"];
    public List<string> ExcludedPaths { get; set; } = [];
    public List<PathRule> Overrides { get; set; } = [];
    public bool ReadInstallationMetadata { get; set; } = true;
    /// <summary>
    /// 实验功能：MFT 快速扫描。null = 用户没手动设置过：以管理员身份运行时默认开启，否则不使用；
    /// true / false = 用户在设置里明确开启或关闭。没有管理员权限时总是自动使用目录遍历。
    /// </summary>
    public bool? FastScan { get; set; }
    /// <summary>本次是否尝试 MFT 快速扫描。</summary>
    public bool FastScanEnabled(bool elevated) => FastScan ?? elevated;
    public bool ScheduledScans { get; set; } = true;
    public int ScanHour { get; set; } = 13;
    public int ScanMinute { get; set; } = 0;
    public int SampleSeconds { get; set; } = 60;
    public List<double> ThresholdsGiB { get; set; } = [20, 11, 3];
    public double HysteresisGiB { get; set; } = 1;
    public double SuddenDropGiB { get; set; } = 2;
    public int SuddenWindowMinutes { get; set; } = 10;
    public int SuddenCooldownMinutes { get; set; } = 30;
    public int SnapshotRetention { get; set; } = 60;
    public int HistoryDays { get; set; } = 400;
    public int DatabaseLimitMiB { get; set; } = 512;
    public int ScanDelayEveryFiles { get; set; } = 256;
    public int ScanDelayMs { get; set; } = 10;
    public int MaxTrackedFileIds { get; set; } = 2_000_000;
    /// <summary>每个扫描范围保留几份文件级明细（用于变化明细）；0 = 不记录。更早的快照仍保留目录级统计。</summary>
    public int FileDetailRetention { get; set; } = 2;
    public double GrowthSignificantMiB { get; set; } = 300;
    public double GrowthRapidMiB { get; set; } = 50;
    public double GrowthRapidPercent { get; set; } = 10;
    /// <summary>应用排行的用户规则（追加到内置规则表之前，优先使用）。格式同 AppRule：路径片段 → 应用名。</summary>
    public List<AppRule> AppRules { get; set; } = [];
    /// <summary>可选功能「骤降抓现场」：骤降提醒触发时自动扫描该盘一次。默认关闭。</summary>
    public bool AutoCaptureOnSuddenDrop { get; set; }
    /// <summary>骤降抓现场两次自动扫描之间至少间隔多少分钟（同一个盘）。</summary>
    public int AutoCaptureCooldownMinutes { get; set; } = 30;
    public void Validate()
    {
        if (Roots.Count == 0 || Roots.Any(x => !Path.IsPathFullyQualified(x))) throw new ArgumentException("扫描目录必须为绝对路径，且至少有一项。");
        if (ExcludedPaths.Any(x => !Path.IsPathFullyQualified(x))) throw new ArgumentException("排除目录必须为绝对路径。");
        if (SampleSeconds is < 10 or > 3600 || ScanHour is < 0 or > 23 || ScanMinute is < 0 or > 59) throw new ArgumentException("采样周期或扫描时间无效。");
        if (ThresholdsGiB.Count == 0 || ThresholdsGiB.Any(x => !double.IsFinite(x) || x <= 0) || HysteresisGiB < 0 || !double.IsFinite(HysteresisGiB)) throw new ArgumentException("提醒阈值无效。");
        if (!double.IsFinite(SuddenDropGiB) || SuddenDropGiB <= 0 || SuddenWindowMinutes is < 1 or > 1440 || SuddenCooldownMinutes < 1) throw new ArgumentException("骤降设置无效。");
        if (SnapshotRetention is < 2 or > 500 || HistoryDays is < 7 or > 3650 || DatabaseLimitMiB is < 32 or > 4096) throw new ArgumentException("保留策略超出允许范围。");
        if (ScanDelayEveryFiles < 1 || ScanDelayMs is < 0 or > 1000 || MaxTrackedFileIds is < 100 or > 5_000_000) throw new ArgumentException("扫描资源限制无效。");
        if (FileDetailRetention is < 0 or > 20) throw new ArgumentException("文件级明细保留份数应在 0–20 之间。");
        if (!double.IsFinite(GrowthSignificantMiB) || !double.IsFinite(GrowthRapidMiB) || !double.IsFinite(GrowthRapidPercent) || GrowthRapidMiB <= 0 || GrowthSignificantMiB <= GrowthRapidMiB || GrowthRapidPercent is < 0 or > 10000)
            throw new ArgumentException("增长分级阈值无效：显著增长阈值应大于快速增长阈值，且都大于 0。");
        if (AutoCaptureCooldownMinutes is < 10 or > 1440) throw new ArgumentException("骤降抓现场的间隔应在 10–1440 分钟之间。");
        if (AppRules.Count > 500 || AppRules.Any(r => r == null || string.IsNullOrWhiteSpace(r.Pattern) || string.IsNullOrWhiteSpace(r.App) || r.Pattern.Length > 260 || r.App.Length > 80 || r.Pattern.Contains("..")))
            throw new ArgumentException("应用归属规则无效：每条需要路径片段和应用名（最多 500 条）。");
        foreach (var rule in Overrides)
            if (!Path.IsPathFullyQualified(rule.Path) || rule.Category == Category.Unexplained || !Enum.IsDefined(rule.Category)) throw new ArgumentException("纠正规则必须指定绝对路径及可扫描分类。");
    }
}
