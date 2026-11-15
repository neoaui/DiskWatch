using System.Text;

namespace DiskWatch.Core;

/// <summary>周报里一个盘的原始输入：本周采样、本周应用排行（可空）、塞满预测（可空）。</summary>
public sealed record WeeklyDrive(string Root, IReadOnlyList<VolumeSample> Samples, AppRankingResult? Apps, FillForecast[]? Forecast);
public sealed record WeeklySummaryInput(DateTime WeekStart, DateTimeOffset Generated, IReadOnlyList<WeeklyDrive> Drives, IReadOnlyList<Alert> Alerts, TimeZoneInfo Zone);
/// <summary>缓存的最新一份周报。Report 可能为空（旧缓存只有文本）。</summary>
public sealed record WeeklyCache(string WeekStart, DateTimeOffset Generated, string Text, WeeklyReport? Report = null);

/// <summary>一周里某一天的占用。没有采样时 UsedBytes / FreeBytes / NetBytes 为 null，不用 0 充数。</summary>
public sealed record WeeklyDay(DateTime Date, string Weekday, int Samples, long? UsedBytes, long? FreeBytes, long? NetBytes);
public sealed record WeeklyDriveReport(string Root, PeriodReport? Period, long? FreeBytes, IReadOnlyList<WeeklyDay> Days, AppRankingResult? Apps, FillForecast[]? Forecast);
public sealed record WeeklyKpi(string Id, string Label, string Value, string Hint, string Tone);
public sealed record WeeklyAppRow(string Root, string App, bool Known, long GrowthBytes, long CurrentBytes, double Share);
public sealed record WeeklyReport(
    DateTime WeekStart,
    DateTime WeekEnd,
    DateTimeOffset Generated,
    bool Partial,
    IReadOnlyList<WeeklyKpi> Kpis,
    IReadOnlyList<WeeklyDriveReport> Drives,
    IReadOnlyList<WeeklyAppRow> TopApps,
    IReadOnlyList<Alert> Alerts,
    string Text);

/// <summary>周报摘要：结构化数据（界面卡片 / 柱状图）和纯文本导出。</summary>
public static class WeeklySummary
{
    static readonly string[] Weekdays = ["一", "二", "三", "四", "五", "六", "日"];

    /// <summary>所在周的周一（本地日期）。</summary>
    public static DateTime WeekStart(DateTimeOffset time, TimeZoneInfo zone)
    {
        var d = TimeZoneInfo.ConvertTime(time, zone).Date;
        return d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    }
    public static DateTimeOffset StartOf(DateTime localDate, TimeZoneInfo zone) => new(localDate, zone.GetUtcOffset(localDate));

    public static string KindName(string kind) => kind switch { "LowSpace" => "低空间", "SuddenDrop" => "空间骤降", "SuddenDropCapture" => "骤降抓现场", _ => kind };

    /// <summary>上周没有任何容量采样时，默认看本周（到现在）；上周有采样则默认上周。</summary>
    public static DateTime PreferredWeekStart(DateTimeOffset now, TimeZoneInfo zone, IEnumerable<VolumeSample> samples)
    {
        var thisWeek = WeekStart(now, zone);
        var lastWeek = thisWeek.AddDays(-7);
        var from = StartOf(lastWeek, zone);
        var to = StartOf(thisWeek, zone);
        return samples.Any(s => s.Time >= from && s.Time < to) ? lastWeek : thisWeek;
    }

    public static bool HasCapacityData(WeeklyReport report) => report.Drives.Any(d => d.Days.Any(day => day.Samples > 0));

    public static string SignedGiB(double gib)
    {
        if (Math.Abs(gib) < 0.005) return "0.00 GiB";
        return (gib > 0 ? "+" : "−") + $"{Math.Abs(gib):N2} GiB";
    }

    public static WeeklyReport Compose(WeeklySummaryInput input)
    {
        var zone = input.Zone; var start = input.WeekStart.Date; var end = start.AddDays(6);
        var from = StartOf(start, zone); var to = StartOf(start.AddDays(7), zone);
        bool partial = input.Generated < to;
        var drives = input.Drives.Select(d => ComposeDrive(d, start, from, to, zone, input.Generated)).ToList();
        var alerts = input.Alerts.Where(a => a.Time >= from && a.Time < to).OrderBy(a => a.Time).ToList();
        var topApps = drives
            .Where(d => d.Apps is { Compared: true })
            .SelectMany(d => d.Apps!.Rows.Where(r => r.GrowthBytes > 0).Select(r => new WeeklyAppRow(d.Root, r.App, r.Known, r.GrowthBytes!.Value, r.CurrentBytes, r.Share)))
            .OrderByDescending(r => r.GrowthBytes)
            .ToList();
        var kpis = BuildKpis(drives, alerts, partial);
        var text = Format(start, end, input.Generated, partial, zone, drives, topApps, alerts);
        return new WeeklyReport(start, end, input.Generated, partial, kpis, drives, topApps, alerts, text);
    }

    public static string Build(WeeklySummaryInput input) => Compose(input).Text;

    static WeeklyDriveReport ComposeDrive(WeeklyDrive d, DateTime start, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone, DateTimeOffset generated)
    {
        var week = d.Samples.Where(s => s.Time >= from && s.Time < to).OrderBy(s => s.Time).ToList();
        var row = Reports.Build(week, "周", zone).FirstOrDefault(r => r.Period == start.ToString("yyyy-MM-dd"));
        long? free = week.Count > 0 ? week[^1].FreeBytes : null;
        var days = new List<WeeklyDay>();
        for (int i = 0; i < 7; i++)
        {
            var day = start.AddDays(i);
            var dayFrom = StartOf(day, zone);
            var dayTo = StartOf(day.AddDays(1), zone);
            if (generated < dayFrom)
            {
                days.Add(new WeeklyDay(day, Weekdays[i], 0, null, null, null));
                continue;
            }
            var points = week.Where(s => s.Time >= dayFrom && s.Time < dayTo).OrderBy(s => s.Time).ToList();
            if (points.Count == 0) { days.Add(new WeeklyDay(day, Weekdays[i], 0, null, null, null)); continue; }
            var first = points[0]; var last = points[^1];
            days.Add(new WeeklyDay(day, Weekdays[i], points.Count, last.UsedBytes, last.FreeBytes, last.UsedBytes - first.UsedBytes));
        }
        return new WeeklyDriveReport(d.Root, row, free, days, d.Apps, d.Forecast);
    }

    static List<WeeklyKpi> BuildKpis(IReadOnlyList<WeeklyDriveReport> drives, IReadOnlyList<Alert> alerts, bool partial)
    {
        var measured = drives.Where(d => d.Period != null).ToList();
        string hint = measured.Count == 0 ? "这一周还没有容量记录" : (partial ? $"到生成时，{measured.Count} 盘有采样" : $"{measured.Count} 盘有采样");
        string Net() => measured.Count == 0 ? "—" : SignedGiB(measured.Sum(d => d.Period!.NetGiB));
        string Sum(Func<PeriodReport, double> pick) => measured.Count == 0 ? "—" : $"{measured.Sum(d => pick(d.Period!)):N2} GiB";
        string netTone = measured.Count == 0 ? "empty" : measured.Sum(d => d.Period!.NetGiB) > 0.005 ? "up" : measured.Sum(d => d.Period!.NetGiB) < -0.005 ? "down" : "neutral";
        var soonest = drives
            .Select(d => (d.Root, F: d.Forecast?.FirstOrDefault(x => x.Kind == ForecastKind.Growing)))
            .Where(x => x.F?.DaysToFull != null)
            .OrderBy(x => x.F!.DaysToFull)
            .FirstOrDefault();
        string fillValue, fillHint, fillTone;
        if (soonest.F is { } f)
        {
            fillValue = f.DaysToFull < 1 ? "不到 1 天" : $"约 {Math.Floor(f.DaysToFull!.Value):N0} 天";
            fillHint = $"{soonest.Root} " + f.Short;
            fillTone = f.DaysToFull < 30 ? "warn" : "neutral";
        }
        else if (drives.All(d => d.Forecast == null || d.Forecast.All(x => x.Kind == ForecastKind.Insufficient)))
        {
            fillValue = "—";
            fillHint = "记录还不够，暂不预测";
            fillTone = "empty";
        }
        else
        {
            fillValue = "没有持续增长";
            fillHint = "近期剩余空间没有持续减少";
            fillTone = "neutral";
        }
        return
        [
            new("net", "已用净变化", Net(), hint, netTone),
            new("growth", "累计增长", Sum(p => p.ObservedGrowthGiB), hint, measured.Count == 0 ? "empty" : "neutral"),
            new("release", "累计释放", Sum(p => p.ObservedReleaseGiB), hint, measured.Count == 0 ? "empty" : "neutral"),
            new("alerts", "提醒", alerts.Count.ToString("N0") + " 条", alerts.Count == 0 ? "这一周没有触发提醒" : string.Join("，", alerts.GroupBy(a => a.Kind).Select(g => $"{KindName(g.Key)} {g.Count()} 条")), alerts.Count == 0 ? "empty" : "warn"),
            new("fill", "最先可能塞满", fillValue, fillHint, fillTone)
        ];
    }

    static string Format(DateTime start, DateTime end, DateTimeOffset generated, bool partial, TimeZoneInfo zone, IReadOnlyList<WeeklyDriveReport> drives, IReadOnlyList<WeeklyAppRow> topApps, IReadOnlyList<Alert> alerts)
    {
        var t = new StringBuilder();
        t.AppendLine($"# DiskWatch 周报摘要 · {start:yyyy-MM-dd}（周一）至 {end:yyyy-MM-dd}（周日）");
        t.AppendLine($"生成时间：{TimeZoneInfo.ConvertTime(generated, zone):yyyy-MM-dd HH:mm}" + (partial ? "（本周还没过完，统计到生成时）" : ""));
        t.AppendLine("数据来源：容量记录、目录扫描和提醒记录。");
        t.AppendLine();
        t.AppendLine("## 各盘本周变化");
        foreach (var d in drives)
        {
            if (d.Period is not { } row) { t.AppendLine($"- {d.Root}：本周没有容量记录"); continue; }
            t.AppendLine($"- {d.Root}：已用 {SignedGiB(row.NetGiB)}（{row.FirstSample[5..16]} {row.FirstUsedGiB:N2} GiB → {row.LastSample[5..16]} {row.LastUsedGiB:N2} GiB），现在剩余 {d.FreeBytes / 1073741824d:N2} GiB；"
                + $"期间累计增长 {row.ObservedGrowthGiB:N2} GiB、释放 {row.ObservedReleaseGiB:N2} GiB；采样 {row.Samples:N0} 次" + (row.Coverage.StartsWith("有采样间断") ? "（监测有中断，例如关机或退出程序）" : ""));
        }
        t.AppendLine();
        t.AppendLine("## 增长最多的应用 / 来源");
        foreach (var d in drives)
        {
            if (d.Apps is not { Compared: true } apps) { t.AppendLine($"- {d.Root}：本周没有可对比的两次扫描"); continue; }
            var top = apps.Rows.Where(r => r.GrowthBytes > 0).Take(5).ToList();
            t.AppendLine($"- {d.Root}（扫描对比 {apps.Baseline!.Finished.ToLocalTime():MM-dd HH:mm} → {apps.Current.Finished.ToLocalTime():MM-dd HH:mm}，扫描到的文件净变化 {Labels.SignedSize(apps.NetBytes)}）：");
            if (top.Count == 0) t.AppendLine("  没有发现增长的应用 / 来源");
            int i = 0;
            foreach (var r in top) t.AppendLine($"  {++i}. {r.App}{(r.Known ? "" : "（未识别目录）")} {Labels.SignedSize(r.GrowthBytes!.Value)}，现在 {Labels.SmartSize(r.CurrentBytes)}，占本次增长 {r.Share:P0}");
        }
        t.AppendLine();
        t.AppendLine("## 提醒");
        if (alerts.Count == 0) t.AppendLine("本周没有触发提醒。");
        else
        {
            t.AppendLine($"本周共 {alerts.Count} 条：" + string.Join("，", alerts.GroupBy(a => a.Kind).Select(g => $"{KindName(g.Key)} {g.Count()} 条")) + "。");
            foreach (var a in alerts.TakeLast(8)) t.AppendLine($"- {TimeZoneInfo.ConvertTime(a.Time, zone):MM-dd HH:mm} {KindName(a.Kind)}：{a.Message}");
            if (alerts.Count > 8) t.AppendLine($"（较早的 {alerts.Count - 8} 条见“提醒历史”）");
        }
        t.AppendLine();
        t.AppendLine("## 塞满预测（生成时）");
        foreach (var d in drives) t.AppendLine($"- {d.Root}：" + (d.Forecast is { Length: > 0 } f ? Forecast.Describe(f) : "没有足够的记录，暂不预测"));
        return t.ToString();
    }
}