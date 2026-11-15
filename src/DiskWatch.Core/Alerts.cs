namespace DiskWatch.Core;

public sealed class AlertState
{
    public HashSet<double> FiredThresholds { get; set; } = [];
    public DateTimeOffset? LastSuddenAlert { get; set; }
    public List<VolumeSample> Recent { get; set; } = [];
}
public sealed record Alert(string Id, DateTimeOffset Time, string VolumeId, string Root, string Kind, string Message);
public static class AlertEngine
{
    public static List<Alert> Evaluate(VolumeSample sample, AlertState state, Settings settings)
    {
        List<Alert> alerts = [];
        var free = sample.FreeBytes / 1073741824d;
        state.FiredThresholds.RemoveWhere(t => (free > t && free >= t + settings.HysteresisGiB) || !settings.ThresholdsGiB.Contains(t));
        var crossed = settings.ThresholdsGiB.Distinct().Where(t => free <= t && !state.FiredThresholds.Contains(t)).ToArray();
        if (crossed.Length > 0)
        {
            foreach (var t in crossed) state.FiredThresholds.Add(t);
            alerts.Add(new(Guid.NewGuid().ToString("N"), sample.Time, sample.VolumeId, sample.Root, "LowSpace",
                $"{sample.Root} 剩余 {free:N2} GiB，已达到 {crossed.Min():G} GiB 提醒档位。请查看占用来源。"));
        }
        // No comparison across long sleep/offline gaps, clock rollback, or different volume identities.
        var last = state.Recent.LastOrDefault();
        if (last != null && sample.Time < last.Time) state.LastSuddenAlert = null;
        if (last != null && (sample.Time <= last.Time || sample.Time - last.Time > TimeSpan.FromSeconds(settings.SampleSeconds * 3) || last.VolumeId != sample.VolumeId)) state.Recent.Clear();
        state.Recent.RemoveAll(x => sample.Time - x.Time > TimeSpan.FromMinutes(settings.SuddenWindowMinutes));
        var peak = state.Recent.OrderByDescending(x => x.FreeBytes).FirstOrDefault();
        if (peak != null && peak.FreeBytes - sample.FreeBytes >= settings.SuddenDropGiB * 1073741824d &&
            (state.LastSuddenAlert == null || sample.Time - state.LastSuddenAlert >= TimeSpan.FromMinutes(settings.SuddenCooldownMinutes)))
        {
            state.LastSuddenAlert = sample.Time;
            alerts.Add(new(Guid.NewGuid().ToString("N"), sample.Time, sample.VolumeId, sample.Root, "SuddenDrop",
                $"{sample.Root} 在 {(sample.Time - peak.Time).TotalMinutes:N1} 分钟内减少 {Labels.Size(peak.FreeBytes - sample.FreeBytes)} 可用空间。扫描完成后可查看来源。"));
        }
        state.Recent.Add(sample);
        return alerts;
    }
}
