namespace DiskWatch.Core;

public enum ForecastKind { Insufficient, NoGrowth, Growing }

/// <summary>塞满预测结果。所有数都由容量采样算出；没有足够数据时不给数字。</summary>
public sealed record FillForecast(string Root, int WindowDays, ForecastKind Kind, double SpanDays, int DailyPoints, long FreeBytes,
    double? GrowthBytesPerDay, double? DaysToFull, DateTime? FullDate, bool LowConfidence)
{
    /// <summary>完整一句，例如「按近 7 天速度约 23 天塞满（到 2026-12-07）」。</summary>
    public string Text => Kind switch
    {
        ForecastKind.Insufficient => $"近 {WindowDays} 天：记录不足 1 天，暂不预测",
        ForecastKind.NoGrowth => GrowthBytesPerDay is double g && g > 0 && DaysToFull > Forecast.MaxDays
            ? $"近 {WindowDays} 天增长很慢（每天约 {Labels.SmartSize((long)g)}），按这个速度 10 年内不会塞满"
            : $"近 {WindowDays} 天：近期没有持续增长",
        _ => (DaysToFull < 1 ? $"按近 {WindowDays} 天速度不到 1 天就会塞满" : $"按近 {WindowDays} 天速度约 {Math.Floor(DaysToFull!.Value):N0} 天塞满（到 {FullDate:yyyy-MM-dd}）")
            + $"，每天约多用 {Labels.SmartSize((long)GrowthBytesPerDay!.Value)}"
            + (SpanDays < WindowDays - 0.5 ? $"（实际只有 {SpanDays:0.#} 天数据）" : "")
            + (LowConfidence ? " · 数据不足 3 天，仅供参考" : "")
    };
    /// <summary>磁盘卡片上的短句。</summary>
    public string Short => Kind switch
    {
        ForecastKind.Insufficient => "塞满预测：数据不足",
        ForecastKind.NoGrowth => "近期没有持续增长",
        _ => (DaysToFull < 1 ? "不到 1 天塞满" : $"约 {Math.Floor(DaysToFull!.Value):N0} 天塞满") + (LowConfidence ? "（参考）" : "")
    };
}

/// <summary>
/// 塞满预测：取窗口内每天剩余空间的最低值（当天用得最多的时刻），用 Theil–Sen 中位数斜率估算每天减少多少，
/// 再用当前剩余空间除以这个速度。中位数斜率不怕个别天的临时大文件；没有持续减少就不预测。
/// </summary>
public static class Forecast
{
    /// <summary>每天增长低于 100 MiB 视为“没有持续增长”。</summary>
    public const double FlatBytesPerDay = 100d * 1048576;
    public const double MaxDays = 3650;

    public static FillForecast Estimate(IEnumerable<VolumeSample> samples, int windowDays, DateTimeOffset now, TimeZoneInfo zone)
    {
        var all = samples.Where(s => s.Time <= now && s.Time > now.AddDays(-windowDays)).OrderBy(s => s.Time).ToList();
        if (all.Count == 0) return new("", windowDays, ForecastKind.Insufficient, 0, 0, 0, null, null, null, true);
        var latest = all[^1];
        var points = all.Where(s => s.VolumeId == latest.VolumeId).ToList();
        double span = (points[^1].Time - points[0].Time).TotalDays;
        var daily = points.GroupBy(s => TimeZoneInfo.ConvertTime(s.Time, zone).Date).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(s => s.FreeBytes).ThenBy(s => s.Time).First()).ToList();
        bool low = span < 3 || daily.Count < 3;
        if (span < 1 || daily.Count < 2) return new(latest.Root, windowDays, ForecastKind.Insufficient, span, daily.Count, latest.FreeBytes, null, null, null, true);
        var slopes = new List<double>();
        for (int i = 0; i < daily.Count; i++)
            for (int j = i + 1; j < daily.Count; j++)
            {
                double dt = (daily[j].Time - daily[i].Time).TotalDays;
                if (dt >= 0.25) slopes.Add((daily[j].FreeBytes - daily[i].FreeBytes) / dt);
            }
        if (slopes.Count == 0) return new(latest.Root, windowDays, ForecastKind.Insufficient, span, daily.Count, latest.FreeBytes, null, null, null, true);
        slopes.Sort();
        double median = slopes.Count % 2 == 1 ? slopes[slopes.Count / 2] : (slopes[slopes.Count / 2 - 1] + slopes[slopes.Count / 2]) / 2;
        double growth = -median;
        bool netDecline = daily[^1].FreeBytes < daily[0].FreeBytes;
        if (growth <= FlatBytesPerDay || !netDecline)
            return new(latest.Root, windowDays, ForecastKind.NoGrowth, span, daily.Count, latest.FreeBytes, growth > 0 ? growth : null, null, null, low);
        double days = Math.Max(0, latest.FreeBytes) / growth;
        if (days > MaxDays) return new(latest.Root, windowDays, ForecastKind.NoGrowth, span, daily.Count, latest.FreeBytes, growth, days, null, low);
        var date = TimeZoneInfo.ConvertTime(now, zone).DateTime.AddDays(days).Date;
        return new(latest.Root, windowDays, ForecastKind.Growing, span, daily.Count, latest.FreeBytes, growth, days, date, low);
    }

    /// <summary>每个卷按 7 天、30 天各估一次；结果按卷根目录归组。</summary>
    public static Dictionary<string, FillForecast[]> ForVolumes(IEnumerable<VolumeSample> samples, DateTimeOffset now, TimeZoneInfo zone) =>
        samples.GroupBy(s => s.VolumeId).Select(g => g.ToList()).Where(g => g.Count > 0)
            .GroupBy(g => g.MaxBy(s => s.Time)!.Root, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => { var list = g.OrderByDescending(x => x.Max(s => s.Time)).First(); return new[] { Estimate(list, 7, now, zone), Estimate(list, 30, now, zone) }; }, StringComparer.OrdinalIgnoreCase);

    /// <summary>界面上的完整说明：7 天为主；30 天窗口里实际数据比 7 天多时才另列。</summary>
    public static string Describe(FillForecast[] f)
    {
        var text = f[0].Text;
        if (f.Length > 1 && f[1].SpanDays > f[0].SpanDays + 0.5) text += "；" + f[1].Text;
        else if (f.Length > 1 && f[1].Kind != ForecastKind.Insufficient) text += $"；近 30 天的数据只有 {f[1].SpanDays:0.#} 天，与近 7 天相同";
        return text;
    }
}
