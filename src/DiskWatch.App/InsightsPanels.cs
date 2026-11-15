using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskWatch.Core;

namespace DiskWatch.App;

/// <summary>应用排行、塞满预测、周报摘要、骤降抓现场（可选）。</summary>
public sealed partial class MainWindow
{
    private Dictionary<string, FillForecast[]> forecasts = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset forecastAt = DateTimeOffset.MinValue;
    private readonly List<Alert> captureQueue = [];
    private readonly CheckBox autoCapture = new() { Content = "空间骤降时自动扫描该磁盘", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) };
    private readonly TextBox weeklyText = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Height = 330, Padding = new Thickness(12), BorderThickness = new Thickness(0), Background = Brushes.White };
    private readonly ComboBox weeklyWhich = new() { ItemsSource = new[] { "上周", "本周（到现在）" }, SelectedIndex = 0, MinWidth = 140, Margin = new Thickness(4) };
    private readonly TextBlock weeklyStatus = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private WeeklyCache? weeklyShown;

    // ---------- 塞满预测 ----------
    /// <summary>每 10 分钟用最近 30 天的容量采样重算一次（后台线程），然后刷新磁盘卡片。</summary>
    private async Task RefreshForecasts()
    {
        if (DateTimeOffset.Now - forecastAt < TimeSpan.FromMinutes(10)) return;
        forecastAt = DateTimeOffset.Now;
        var result = await Task.Run(() => Forecast.ForVolumes(repository.Samples(DateTimeOffset.UtcNow.AddDays(-30)), DateTimeOffset.Now, TimeZoneInfo.Local));
        forecasts = result;
        if (latestVolumes.Count > 0) UpdateDriveCards(latestVolumes.ToList());
    }
    private static Brush ForecastBrush(FillForecast f) => f.Kind == ForecastKind.Growing ? (f.DaysToFull < 30 ? Danger : Hex("#B56A22")) : Muted;
    private UIElement? ForecastLine(string root)
    {
        if (!forecasts.TryGetValue(root, out var f)) return null;
        var line = Copy(f[0].Short, 10, ForecastBrush(f[0])); line.ToolTip = "塞满预测：" + Forecast.Describe(f) + "\n根据近期剩余空间变化估算。数据不足时仅供参考，不给出天数。";
        System.Windows.Automation.AutomationProperties.SetName(line, "塞满预测：" + Forecast.Describe(f));
        return line;
    }
    private UIElement? ForecastDetail(string root)
    {
        if (!forecasts.TryGetValue(root, out var f)) return null;
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        p.Children.Add(new Border { Background = Hex("#EAF5F5"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 2, 8, 2), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = "塞满预测", FontSize = 11, Foreground = Accent } });
        var text = Copy(Forecast.Describe(f), 12, ForecastBrush(f[0])); text.MaxWidth = 860; p.Children.Add(text);
        return p;
    }

    // ---------- 应用排行 ----------
    private Expander AppRankingFold(Snapshot s)
    {
        var host = new StackPanel();
        var period = new ComboBox { ItemsSource = AppRanking.Periods, SelectedIndex = 0, MinWidth = 150, Margin = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        var body = new StackPanel(); host.Children.Add(period); host.Children.Add(body);
        var fold = Fold("应用排行", $"{s.Root} 各应用及来源的占用与增长", host);
        fold.ToolTip = "按安装位置识别应用，对比最近两次扫描计算增长。";
        List<AppUsageRecord>? history = null; bool built = false;
        async Task Render()
        {
            body.Children.Clear();
            try
            {
                history ??= await Task.Run(() => repository.AppUsageHistory(s.Root));
                var current = history.FirstOrDefault(h => h.SnapshotId == s.Id);
                if (current == null) { body.Children.Add(Copy("这次扫描没有应用占用记录。完成一次新的扫描后可查看当前占用，再扫描一次后可查看增长。", 12, Muted)); return; }
                var p = period.SelectedItem as string ?? AppRanking.Periods[0];
                body.Children.Add(AppRankingList(AppRanking.Build(current, AppRanking.Baseline(history, current, AppRanking.PeriodSpan(p)), p), GrowthThresholds.From(settings)));
            }
            catch (Exception ex) { body.Children.Add(Copy("读取应用排行失败：" + ex.Message, 12, Danger)); }
        }
        fold.Expanded += async (_, args) => { if (args.Source != fold || built) return; built = true; await Render(); };
        period.SelectionChanged += async (_, _) => { if (built) await Render(); };
        return fold;
    }

    private static Brush GradeBrush(GrowthGrade g) => g switch
    {
        GrowthGrade.Significant => Danger, GrowthGrade.Rapid => Hex("#D9770B"), GrowthGrade.Minor => Hex("#A07A00"), GrowthGrade.Shrink => Hex("#2E7D32"), _ => Muted
    };

    /// <summary>排行列表：名次、应用 / 来源、增长、当前占用、占总增长。没有基线时按当前占用排，最后一列为占比。</summary>
    internal static UIElement AppRankingList(AppRankingResult r, GrowthThresholds t, int max = 15)
    {
        var p = new StackPanel();
        p.Children.Add(Copy(r.Compared ? $"扫描文件净变化 {Labels.SignedSize(r.NetBytes)}，增长合计 {Labels.SmartSize(r.GrowthTotal)}" : $"当前占用 {Labels.SmartSize(r.Current.Total)}，共 {r.Rows.Count} 个应用 / 来源", 15));
        p.Children.Add(Copy(r.Note, 11, Muted));
        var rows = r.Compared ? r.Rows.Where(x => x.GrowthBytes > 0).ToList() : r.Rows;
        System.Windows.Controls.Grid Row(params UIElement[] cells)
        {
            var g = new System.Windows.Controls.Grid { Margin = new Thickness(0, 3, 0, 3) };
            foreach (var w in new[] { 40, 0, 120, 120, 170 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            for (int i = 0; i < cells.Length; i++) { System.Windows.Controls.Grid.SetColumn(cells[i], i); g.Children.Add(cells[i]); }
            return g;
        }
        TextBlock Cell(string text, Brush? color = null, double size = 13, bool right = false) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, TextAlignment = right ? TextAlignment.Right : TextAlignment.Left, Margin = new Thickness(0, 0, right ? 14 : 6, 0) };
        var header = Row(Cell("#", Muted, 11), Cell("应用 / 来源", Muted, 11), Cell(r.Compared ? "增长" : "", Muted, 11, true), Cell("当前占用", Muted, 11, true), Cell(r.Compared ? "占总增长" : "占已扫描", Muted, 11));
        header.Margin = new Thickness(0, 10, 0, 2); p.Children.Add(header);
        p.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 0, 0, 4) });
        if (r.Compared && rows.Count == 0) p.Children.Add(Copy("两次扫描之间没有应用或来源增长。", 12, Muted));
        int rank = 0;
        foreach (var x in rows.Take(max))
        {
            rank++;
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var label = Cell(x.App, x.Known ? Ink : Hex("#4A5A64")); label.ToolTip = x.Known ? x.App : x.App + "\n未能识别为已安装应用，已按所在文件夹归组。"; name.Children.Add(label);
            if (!x.Known) name.Children.Add(new Border { Background = Hex("#EEF2F5"), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "未识别目录", FontSize = 10, Foreground = Muted } });
            var grade = x.GrowthBytes is long d ? Growth.Grade(x.BeforeBytes, x.CurrentBytes, t) : GrowthGrade.None;
            var growth = Cell(x.GrowthBytes is long g ? Labels.SignedSize(g) : "", GradeBrush(grade), 13, true); growth.FontWeight = grade is GrowthGrade.Significant or GrowthGrade.Rapid ? FontWeights.SemiBold : FontWeights.Normal;
            var share = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            share.Children.Add(new ProgressBar { Width = 90, Height = 6, Maximum = 1, Value = x.Share, Foreground = r.Compared ? GradeBrush(grade == GrowthGrade.None ? GrowthGrade.Minor : grade) : Accent, Background = Hex("#EAF0F3"), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center });
            share.Children.Add(new TextBlock { Text = $"  {x.Share:P1}", FontSize = 11, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
            var row = Row(Cell(rank.ToString(), Muted, 12), name, growth, Cell(Labels.SmartSize(x.CurrentBytes), Ink, 12, true), share);
            System.Windows.Automation.AutomationProperties.SetName(row, $"第 {rank} 名 {x.App}，{(x.GrowthBytes is long gg ? "增长 " + Labels.SignedSize(gg) + "，" : "")}当前 {Labels.SmartSize(x.CurrentBytes)}");
            p.Children.Add(row);
        }
        if (rows.Count > max) p.Children.Add(Copy($"另有 {rows.Count - max} 项较小的{(r.Compared ? "增长" : "来源")}未列出。", 11, Muted));
        if (r.Compared)
        {
            var shrink = r.Rows.Where(x => x.GrowthBytes < 0).OrderBy(x => x.GrowthBytes).Take(3).ToList();
            if (shrink.Count > 0) p.Children.Add(Copy("减少最多：" + string.Join("、", shrink.Select(x => $"{x.App} {Labels.SignedSize(x.GrowthBytes!.Value)}")), 11, Hex("#2E7D32")));
        }
        return p;
    }

    // ---------- 周报摘要（界面在 WeeklyPanel.cs） ----------


    private WeeklyReport ComposeWeekly(DateTime weekStart)
    {
        var zone = TimeZoneInfo.Local; var from = WeeklySummary.StartOf(weekStart, zone); var to = WeeklySummary.StartOf(weekStart.AddDays(7), zone);
        var now = DateTimeOffset.Now; var at = now < to ? now : to;
        var samples = repository.Samples(at.AddDays(-30)).Where(s => s.Time <= at).ToList();
        var forecastByRoot = Forecast.ForVolumes(samples, at, zone);
        var drives = new List<WeeklyDrive>();
        foreach (var root in settings.Roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var volume = Path.GetPathRoot(root) ?? root;
            var volumeSamples = samples.Where(s => s.Root.Equals(volume, StringComparison.OrdinalIgnoreCase)).ToList();
            AppRankingResult? apps = null;
            var history = repository.AppUsageHistory(root);
            var current = history.Where(h => h.Usable && h.Finished < to && h.Finished >= from).OrderByDescending(h => h.Finished).FirstOrDefault();
            if (current != null && AppRanking.Baseline(history, current, current.Finished - from) is { } baseline) apps = AppRanking.Build(current, baseline);
            drives.Add(new(root, volumeSamples, apps, Classifier.Normalize(root).Equals(Classifier.Normalize(volume), StringComparison.OrdinalIgnoreCase) ? forecastByRoot.GetValueOrDefault(volume) : null));
        }
        return WeeklySummary.Compose(new(weekStart, now, drives, repository.RecentAlerts(1000), zone));
    }

    private async Task GenerateWeekly()
    {
        var zone = TimeZoneInfo.Local;
        var start = WeeklySummary.WeekStart(DateTimeOffset.Now, zone); if (weeklyWhich.SelectedIndex == 0) start = start.AddDays(-7);
        weeklyStatus.Text = "正在生成…"; weeklyStatus.Foreground = Muted;
        var report = await Task.Run(() => ComposeWeekly(start));
        weeklyShown = new WeeklyCache(start.ToString("yyyy-MM-dd"), report.Generated, report.Text);
        repository.SetState("weekly:latest", weeklyShown);
        weeklyText.Text = report.Text;
        RenderWeekly(report);
        weeklyStatus.Text = $"已生成 {report.WeekStart:MM-dd} 至 {report.WeekEnd:MM-dd} 的周报（{DateTime.Now:HH:mm}）。"; weeklyStatus.Foreground = Accent;
    }

    /// <summary>启动时：上周还没有容量采样就先看本周；否则看上周。不是计划任务。</summary>
    private async Task EnsureWeekly()
    {
        var zone = TimeZoneInfo.Local;
        var samples = await Task.Run(() => repository.Samples(DateTimeOffset.UtcNow.AddDays(-20)));
        var preferred = WeeklySummary.PreferredWeekStart(DateTimeOffset.Now, zone, samples);
        var thisWeek = WeeklySummary.WeekStart(DateTimeOffset.Now, zone);
        weeklyWhich.SelectedIndex = preferred == thisWeek ? 1 : 0;
        await GenerateWeekly();
    }

    private Task ExportWeekly()
    {
        if (weeklyShown == null || string.IsNullOrWhiteSpace(weeklyText.Text)) { weeklyStatus.Text = "请先生成周报。"; return Task.CompletedTask; }
        var folder = Path.Combine(Program.Root, "exports"); Directory.CreateDirectory(folder);
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "导出周报摘要", Filter = "Markdown 文件 (*.md)|*.md|文本文件 (*.txt)|*.txt", FileName = $"DiskWatch-周报-{weeklyShown.WeekStart}.md", InitialDirectory = folder, AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        File.WriteAllText(dialog.FileName, weeklyText.Text, new UTF8Encoding(true));
        weeklyStatus.Text = "已导出：" + dialog.FileName; weeklyStatus.Foreground = Accent; status.Text = weeklyStatus.Text;
        return Task.CompletedTask;
    }

    // ---------- 骤降抓现场（可选） ----------
    private UIElement AutoCaptureSettings()
    {
        var p = new StackPanel();
        p.Children.Add(autoCapture);
        p.Children.Add(Copy("可用空间突然下降时，自动扫描该磁盘，并在提醒中列出增长较多的应用和文件夹。", 12, Muted));
        p.Children.Add(Copy("扫描可能需要几分钟。同一磁盘 30 分钟内最多自动扫描一次；已有扫描进行时不会重复开始。", 11, Muted));
        HookDirty(autoCapture);
        return p;
    }

    private async Task MaybeAutoCapture(Alert alert)
    {
        if (smoke || uiReview || closing) return;
        var key = AutoCapture.StateKeyPrefix + alert.Root;
        var reason = AutoCapture.Decide(settings, alert, DateTimeOffset.Now, repository.GetState<DateTimeOffset?>(key), scanning, paused);
        if (reason != null) { if (settings.AutoCaptureOnSuddenDrop) Program.Log($"骤降抓现场未启动（{alert.Root}）：{reason}"); return; }
        var roots = AutoCapture.RootsFor(alert.Root, settings.Roots);
        if (roots.Count == 0) { Program.Log($"骤降抓现场未启动（{alert.Root}）：该盘不在扫描范围内"); return; }
        repository.SetState(key, DateTimeOffset.Now);
        var before = roots.ToDictionary(r => r, r => repository.LatestSnapshots(r, 1).FirstOrDefault(), StringComparer.OrdinalIgnoreCase);
        Program.Log($"骤降抓现场：开始自动扫描 {string.Join("、", roots)}");
        await ScanRoots(roots, automatic: true);
        foreach (var root in roots)
        {
            var latest = await Task.Run(() => repository.LatestSnapshots(root, 1).FirstOrDefault());
            if (latest == null || latest.Id == before[root]?.Id) { Program.Log($"骤降抓现场：{root} 没有保存新的扫描结果"); continue; }
            var history = await Task.Run(() => repository.AppUsageHistory(root, 20));
            var current = history.FirstOrDefault(h => h.SnapshotId == latest.Id);
            var apps = current == null ? null : AppRanking.Build(current, AppRanking.Baseline(history, current, null));
            var text = AutoCapture.Report(alert.Message, latest, before[root], apps);
            var record = new Alert(Guid.NewGuid().ToString("N"), DateTimeOffset.Now, alert.VolumeId, alert.Root, "SuddenDropCapture", text);
            await Task.Run(() => repository.AddAlert(record));
            Program.Log(text);
            tray.ShowBalloonTip(8000, "DiskWatch2 骤降抓现场", text.Length > 250 ? text[..250] + "…" : text, System.Windows.Forms.ToolTipIcon.Warning);
            status.Text = text;
        }
        alerts.ItemsSource = repository.RecentAlerts().Select(x => new { Time = x.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), x.Root, Kind = WeeklySummary.KindName(x.Kind), x.Message }).ToList();
    }
}
