namespace DiskWatch.Core;

/// <summary>
/// 骤降抓现场（可选功能，默认关闭）：骤降提醒触发时自动扫一次那个盘，再告诉用户是哪些应用 / 文件夹长大了。
/// 这里只放纯判断逻辑：开关、冷却、扫描锁、要扫哪些范围。
/// </summary>
public static class AutoCapture
{
    public const string StateKeyPrefix = "autocapture:last:";

    /// <summary>返回 null 表示应该开始扫描；否则返回不扫的原因。</summary>
    public static string? Decide(Settings settings, Alert alert, DateTimeOffset now, DateTimeOffset? lastRun, bool scanning, bool paused)
    {
        if (!settings.AutoCaptureOnSuddenDrop) return "未开启骤降抓现场";
        if (alert.Kind != "SuddenDrop") return "不是骤降提醒";
        if (paused) return "监测已暂停";
        if (scanning) return "已有扫描在进行（本次骤降由正在进行的扫描记录）";
        if (lastRun is DateTimeOffset last && now >= last && now - last < TimeSpan.FromMinutes(settings.AutoCaptureCooldownMinutes))
            return $"距上次自动扫描不到 {settings.AutoCaptureCooldownMinutes} 分钟";
        return null;
    }

    /// <summary>扫描范围里落在报警卷上的项（通常就是 C:\ 整盘），保证和平时的扫描口径相同、可以对比。</summary>
    public static List<string> RootsFor(string alertRoot, IEnumerable<string> configuredRoots)
    {
        var volume = Classifier.Normalize(alertRoot);
        return configuredRoots.Where(r => Path.IsPathFullyQualified(r) && Classifier.Normalize(Path.GetPathRoot(r) ?? "").Equals(volume, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>抓现场结果：增长最多的应用和文件夹（目录统计桶，四层以内）。没有可比的上一次扫描时如实说明。</summary>
    public static string Report(string alertMessage, Snapshot current, Snapshot? previous, AppRankingResult? apps, int top = 3)
    {
        var head = $"{current.Root} 骤降抓现场（{current.Finished.ToLocalTime():MM-dd HH:mm} 自动扫描）";
        if (current.Status is not ("Complete" or "Partial")) return $"{head}：扫描没有正常结束（{current.Coverage}），没有生成对比。触发原因：{alertMessage}";
        var parts = new List<string>();
        if (apps is { Compared: true } && AppRanking.TopGrowth(apps, top) is { Length: > 0 } a) parts.Add("增长最多的来源：" + a);
        var folders = FolderGrowth(current, previous, top);
        if (folders.Count > 0) parts.Add("增长最多的文件夹：" + string.Join("、", folders.Select(f => $"{f.Path} {Labels.SignedSize(f.Delta)}")));
        if (parts.Count == 0)
            return previous == null || !FoldersComparable(current, previous)
                ? $"{head}：没有可对比的上一次扫描，本次结果已保存为基线。触发原因：{alertMessage}"
                : $"{head}：与 {previous.Finished.ToLocalTime():MM-dd HH:mm} 的扫描相比，扫描到的文件没有明显增长。空间变化可能来自未能读取的区域、还原点或已删除的临时文件。触发原因：{alertMessage}";
        var since = apps is { Compared: true } ? apps.Baseline!.Finished : previous!.Finished;
        return $"{head}：{string.Join("；", parts)}。对比 {since.ToLocalTime():MM-dd HH:mm} 的同方式扫描。触发原因：{alertMessage}";
    }

    public static bool FoldersComparable(Snapshot a, Snapshot b) => a.Status is "Complete" or "Partial" && b.Status is "Complete" or "Partial" && a.Id != b.Id
        && a.VolumeId == b.VolumeId && !string.IsNullOrEmpty(a.VolumeId) && Classifier.Normalize(a.Root).Equals(Classifier.Normalize(b.Root), StringComparison.OrdinalIgnoreCase)
        && a.RuleVersion == b.RuleVersion && a.CoverageKey == b.CoverageKey;

    /// <summary>按统计桶路径（合并分类）比较两次扫描，只列增长的。统计桶互不重复，不会父子重复计数。</summary>
    public static List<(string Path, long Delta)> FolderGrowth(Snapshot current, Snapshot? previous, int top)
    {
        if (previous == null || !FoldersComparable(current, previous)) return [];
        var before = previous.Buckets.GroupBy(b => b.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(b => b.AllocatedBytes), StringComparer.OrdinalIgnoreCase);
        return current.Buckets.GroupBy(b => b.Path, StringComparer.OrdinalIgnoreCase).Select(g => (Path: g.Key, Delta: g.Sum(b => b.AllocatedBytes) - before.GetValueOrDefault(g.Key)))
            .Where(x => x.Delta > 0).OrderByDescending(x => x.Delta).Take(top).ToList();
    }
}
