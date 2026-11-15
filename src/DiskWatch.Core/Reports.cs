using System.Globalization;
using System.Text;

namespace DiskWatch.Core;

public sealed record PeriodReport(string Period, string Root, string VolumeId, string FirstSample, string LastSample,
    double FirstUsedGiB, double LastUsedGiB, double NetGiB, double ObservedGrowthGiB, double ObservedReleaseGiB, int Samples, string Coverage);
public sealed record BucketChange(string Path, string Category, double CurrentGiB, double? ChangeGiB, string Quality);
public static class Reports
{
    public static List<PeriodReport> Build(IEnumerable<VolumeSample> samples, string period, TimeZoneInfo zone)
    {
        DateTime Key(VolumeSample s)
        {
            var d = TimeZoneInfo.ConvertTime(s.Time, zone).Date;
            return period switch { "月" => new(d.Year, d.Month, 1), "周" => d.AddDays(-((int)d.DayOfWeek + 6) % 7), _ => d };
        }
        List<PeriodReport> rows = [];
        foreach (var volume in samples.GroupBy(x => x.VolumeId))
        {
            var ordered = volume.OrderBy(x => x.Time).ToList();
            // Deltas crossing midnight belong to the later sample, exactly once.
            var deltas = ordered.Select((s, i) => (Sample: s, Previous: i == 0 ? null : ordered[i - 1])).ToList();
            foreach (var group in deltas.GroupBy(x => Key(x.Sample)))
            {
                var points = group.ToList();
                var first = points[0].Previous ?? points[0].Sample; var last = points[^1].Sample;
                var growth = points.Sum(x => x.Previous == null ? 0 : Math.Max(0, x.Sample.UsedBytes - x.Previous.UsedBytes));
                var release = points.Sum(x => x.Previous == null ? 0 : Math.Max(0, x.Previous.UsedBytes - x.Sample.UsedBytes));
                bool gap = points.Any(x => x.Previous != null && x.Sample.Time - x.Previous.Time > TimeSpan.FromMinutes(3));
                rows.Add(new(group.Key.ToString("yyyy-MM-dd"), last.Root, last.VolumeId, first.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), last.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    first.UsedBytes / 1073741824d, last.UsedBytes / 1073741824d, (last.UsedBytes - first.UsedBytes) / 1073741824d,
                    growth / 1073741824d, release / 1073741824d, points.Count,
                    gap ? "有采样间断；跨期变化归入后次观测区间，非精确自然期边界" : "按观测边界统计；未捕获采样间瞬态变化"));
            }
        }
        return rows.OrderByDescending(x => x.Period).ThenBy(x => x.Root).ToList();
    }
    public static List<BucketChange> Compare(Snapshot current, Snapshot? previous)
    {
        var canCompare = previous != null && previous.VolumeId == current.VolumeId && previous.Root == current.Root && previous.IsComparable && current.IsComparable && previous.RuleVersion == current.RuleVersion;
        var old = previous?.Buckets.ToDictionary(x => (x.Path, x.Category), x => x.AllocatedBytes) ?? [];
        var now = current.Buckets.ToDictionary(x => (x.Path, x.Category), x => x.AllocatedBytes);
        return now.Keys.Union(canCompare ? old.Keys : []).Select(k => new BucketChange(k.Path, Labels.CategoryName(k.Category), now.GetValueOrDefault(k) / 1073741824d,
            canCompare ? (now.GetValueOrDefault(k) - old.GetValueOrDefault(k)) / 1073741824d : null,
            canCompare ? "扫描窗口差值，非逐文件事件" : "基线不足 / 覆盖不完整 / 规则变更；不推断删除")).OrderByDescending(x => Math.Abs(x.ChangeGiB ?? x.CurrentGiB)).ToList();
    }
    public static string CsvCell(object? value)
    {
        var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (value is string && s.TrimStart().StartsWithAny('=', '+', '-', '@')) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
    public static void WriteCsv(string path, IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine(string.Join(',', header.Select(CsvCell)));
        foreach (var row in rows) writer.WriteLine(string.Join(',', row.Select(CsvCell)));
    }
    private static bool StartsWithAny(this string s, params char[] values) => s.Length > 0 && values.Contains(s[0]);
}
