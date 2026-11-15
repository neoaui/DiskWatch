namespace DiskWatch.Core;

/// <summary>一次扫描的应用归属结果（与快照分开保存，便于按天 / 周 / 月找对比基线）。</summary>
public sealed record AppUsageRecord(string SnapshotId, string Root, string VolumeId, DateTimeOffset Finished, string Status, long Errors,
    string RuleVersion, string CoverageKey, string RulesKey, string ScanMethod, List<AppUsage> Apps)
{
    public long Total => Apps.Sum(a => a.AllocatedBytes);
    /// <summary>扫描正常结束（包括有读取问题的“部分覆盖”）才用于排行；取消、失败、达到资源上限的不用。</summary>
    public bool Usable => Status is "Complete" or "Partial";
    public static AppUsageRecord From(Snapshot s) => new(s.Id, s.Root, s.VolumeId, s.Finished, s.Status, s.Errors, s.RuleVersion, s.CoverageKey, s.AppRulesKey, s.ScanMethod, s.Apps ?? []);
}

public sealed record AppRankRow(string App, bool Known, long BeforeBytes, long CurrentBytes, long? GrowthBytes, double Share);
public sealed record AppRankingResult(AppUsageRecord Current, AppUsageRecord? Baseline, List<AppRankRow> Rows, long GrowthTotal, long NetBytes, string Note)
{
    public bool Compared => Baseline != null;
}

/// <summary>“谁在吃 C 盘”应用排行：比较同一扫描范围、同一卷、同一扫描方式和规则的两次扫描里各应用的占用。</summary>
public static class AppRanking
{
    public static readonly string[] Periods = ["最近两次扫描", "近 1 天", "近 7 天", "近 30 天"];
    public static TimeSpan? PeriodSpan(string period) => period switch { "近 1 天" => TimeSpan.FromDays(1), "近 7 天" => TimeSpan.FromDays(7), "近 30 天" => TimeSpan.FromDays(30), _ => null };

    /// <summary>不能对比的原因；null 表示可以对比。</summary>
    public static string? Incomparable(AppUsageRecord current, AppUsageRecord other)
    {
        if (!current.Usable || !other.Usable) return "有一次扫描没有正常结束";
        if (!Classifier.Normalize(current.Root).Equals(Classifier.Normalize(other.Root), StringComparison.OrdinalIgnoreCase)) return "扫描范围不同";
        if (string.IsNullOrWhiteSpace(current.VolumeId) || current.VolumeId != other.VolumeId) return "卷身份不同";
        if (current.RuleVersion != other.RuleVersion) return "扫描方式或分类规则不同";
        if (current.CoverageKey != other.CoverageKey) return "排除目录不同";
        if (current.RulesKey != other.RulesKey) return "应用归属规则不同";
        return null;
    }

    /// <summary>
    /// 选对比基线。history 为同一范围的记录（任意顺序）。period 为空：上一次可对比的扫描；
    /// 否则取“距当前至少 period 之前”的最近一次；没有那么早的，就取窗口内最早的一次（结果说明里写实际跨度）。
    /// </summary>
    public static AppUsageRecord? Baseline(IEnumerable<AppUsageRecord> history, AppUsageRecord current, TimeSpan? period)
    {
        var older = history.Where(h => h.SnapshotId != current.SnapshotId && h.Finished < current.Finished && Incomparable(current, h) == null).OrderByDescending(h => h.Finished).ToList();
        if (older.Count == 0) return null;
        if (period is not TimeSpan p) return older[0];
        var cutoff = current.Finished - p;
        return older.FirstOrDefault(h => h.Finished <= cutoff) ?? older[^1];
    }

    public static AppRankingResult Build(AppUsageRecord current, AppUsageRecord? baseline, string? period = null)
    {
        var now = current.Apps.GroupBy(a => (a.App, a.Known)).ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedBytes));
        if (baseline == null)
        {
            long total = Math.Max(1, now.Values.Sum());
            var rows = now.Select(x => new AppRankRow(x.Key.App, x.Key.Known, 0, x.Value, null, x.Value / (double)total)).OrderByDescending(r => r.CurrentBytes).ToList();
            return new(current, null, rows, 0, 0, current.Apps.Count == 0
                ? "这次扫描没有应用占用记录。完成一次新的扫描后可查看当前占用，再扫描一次后可查看增长。"
                : "还没有可对比的上一次扫描，先按当前占用排序。");
        }
        var old = baseline.Apps.GroupBy(a => (a.App, a.Known)).ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedBytes));
        var all = now.Keys.Union(old.Keys).Select(k => (k.App, k.Known, Before: old.GetValueOrDefault(k), After: now.GetValueOrDefault(k))).ToList();
        long growthTotal = all.Sum(x => Math.Max(0, x.After - x.Before));
        var list = all.Select(x => new AppRankRow(x.App, x.Known, x.Before, x.After, x.After - x.Before, x.After > x.Before && growthTotal > 0 ? (x.After - x.Before) / (double)growthTotal : 0))
            .OrderByDescending(r => r.GrowthBytes).ThenByDescending(r => r.CurrentBytes).ToList();
        var span = current.Finished - baseline.Finished;
        var note = $"对比 {baseline.Finished.ToLocalTime():MM-dd HH:mm} 至 {current.Finished.ToLocalTime():MM-dd HH:mm}（间隔 {Span(span)}）的两次扫描。";
        if (period is { } p && PeriodSpan(p) is TimeSpan want && span < want - TimeSpan.FromHours(1)) note += $" 没有更早的可对比扫描，实际只覆盖 {Span(span)}。";
        if (current.Errors > 0 || baseline.Errors > 0) note += $" 两次分别有 {baseline.Errors:N0} / {current.Errors:N0} 项读取失败，未读到的文件不计入。";
        note += " 按文件所在位置归类，不一定是该程序写入。";
        return new(current, baseline, list, growthTotal, now.Values.Sum() - old.Values.Sum(), note);
    }

    public static string Span(TimeSpan t) => t.TotalDays >= 1 ? $"{t.TotalDays:0.#} 天" : t.TotalHours >= 1 ? $"{t.TotalHours:0.#} 小时" : $"{Math.Max(1, t.TotalMinutes):0} 分钟";

    /// <summary>排行文字（周报、骤降抓现场用）：增长最多的前 top 项。</summary>
    public static string TopGrowth(AppRankingResult r, int top = 3) =>
        string.Join("、", r.Rows.Where(x => x.GrowthBytes > 0).Take(top).Select(x => $"{x.App} {Labels.SignedSize(x.GrowthBytes!.Value)}"));
}
