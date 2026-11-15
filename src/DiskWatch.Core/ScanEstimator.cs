namespace DiskWatch.Core;

/// <summary>扫描进度横幅显示用的估算结果。Fraction 为 null 表示拿不到可靠百分比（显示不确定进度）。</summary>
public sealed record ScanEstimate(double? Fraction, bool Indeterminate, TimeSpan Elapsed, TimeSpan? Remaining, string Phase);

/// <summary>
/// 纯计算：用“已扫描的分配空间 / 卷已用空间”估算扫描进度（封顶 99%，扫完才显示 100%），
/// 并在进度超过 5% 且连续几次估算稳定后才给出预计剩余时间。只是估计：跳过的目录、硬链接去重等会让实际完成点早于 100%。
/// </summary>
public sealed class ScanEstimator
{
    public const double Cap = 0.99, MinFractionForEta = 0.05, StableTolerance = 0.3;
    public const int StableSamplesRequired = 3;
    private readonly long? expected;
    private readonly DateTimeOffset started;
    private DateTimeOffset? baseTime;
    private double baseFraction;
    private double? smoothed;
    private int stable;

    public ScanEstimator(long? expectedBytes, DateTimeOffset started) { expected = expectedBytes; this.started = started; }

    /// <summary>估计进度：expected 不可用（如只扫某个文件夹）时返回 null。</summary>
    public static double? Fraction(long scannedBytes, long? expectedBytes)
    {
        if (expectedBytes is not > 0) return null;
        return Math.Clamp((double)Math.Max(0, scannedBytes) / expectedBytes.Value, 0, Cap);
    }

    /// <summary>按从 baseFraction 到 fraction 所用时间线性外推剩余时间；进度不足 5% 或样本太短时返回 null。</summary>
    public static TimeSpan? RawRemaining(double fraction, double baseFraction, TimeSpan span)
    {
        if (fraction <= MinFractionForEta || span < TimeSpan.FromSeconds(5) || fraction - baseFraction < 0.005) return null;
        return TimeSpan.FromSeconds(span.TotalSeconds * (1 - fraction) / (fraction - baseFraction));
    }

    public ScanEstimate Update(long scannedBytes, bool readingIndex, DateTimeOffset now)
    {
        var elapsed = now - started;
        if (readingIndex)
        {
            baseTime = null; smoothed = null; stable = 0;
            return new(null, true, elapsed, null, "正在读取文件目录");
        }
        var fraction = Fraction(scannedBytes, expected);
        if (fraction == null) return new(null, true, elapsed, null, "正在扫描，正在估算进度");
        if (baseTime == null) { baseTime = now; baseFraction = fraction.Value; }
        var raw = RawRemaining(fraction.Value, baseFraction, now - baseTime.Value);
        if (raw == null) { stable = 0; return new(fraction, false, elapsed, null, "正在扫描"); }
        var seconds = raw.Value.TotalSeconds;
        if (smoothed is double s && Math.Abs(seconds - s) <= StableTolerance * Math.Max(s, 20)) stable++; else stable = smoothed == null ? 1 : 0;
        smoothed = smoothed == null ? seconds : 0.7 * smoothed.Value + 0.3 * seconds;
        return new(fraction, false, elapsed, stable >= StableSamplesRequired ? TimeSpan.FromSeconds(smoothed.Value) : null, "正在扫描");
    }

    public ScanEstimate Complete(DateTimeOffset now) => new(1, false, now - started, TimeSpan.Zero, "扫描完成");

    /// <summary>时长文案：1:05、12:30、1:02:03。</summary>
    public static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    /// <summary>路径中间截断，保留盘符开头和末尾目录名。</summary>
    public static string Shorten(string path, int max = 70)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= max || max < 10) return path ?? "";
        int head = Math.Min(path.Length, max / 3), tail = max - head - 1;
        return path[..head] + "…" + path[^tail..];
    }
}