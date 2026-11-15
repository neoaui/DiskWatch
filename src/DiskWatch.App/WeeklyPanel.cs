using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DiskWatch.Core;

namespace DiskWatch.App;

public sealed partial class MainWindow
{
    private readonly StackPanel weeklyBody = new();
    private readonly TextBlock weeklyRange = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };

    private UIElement WeeklyCard()
    {
        var p = new StackPanel();
        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        title.Children.Add(Copy("周报摘要", 17));
        title.Children.Add(Copy("各盘空间变化、增长较多的应用、提醒和塞满预测。", 11, Muted));
        title.Children.Add(weeklyRange);
        p.Children.Add(title);
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 10) };
        tools.Children.Add(weeklyWhich);
        tools.Children.Add(Button("生成周报", GenerateWeekly));
        tools.Children.Add(Button("导出为文件…", ExportWeekly));
        tools.Children.Add(weeklyStatus);
        p.Children.Add(tools);
        weeklyText.Visibility = Visibility.Collapsed;
        weeklyText.Height = 0;
        p.Children.Add(weeklyText);
        p.Children.Add(weeklyBody);
        var card = Card(p, new Thickness(18, 16, 18, 18));
        card.Margin = new Thickness(0, 0, 0, 12);
        System.Windows.Automation.AutomationProperties.SetName(card, "周报摘要");
        return card;
    }

    private void RenderWeekly(WeeklyReport report)
    {
        weeklyBody.Children.Clear();
        var zone = TimeZoneInfo.Local;
        var localGen = TimeZoneInfo.ConvertTime(report.Generated, zone);
        weeklyRange.Text = $"{report.WeekStart:yyyy年M月d日}（周一）至 {report.WeekEnd:M月d日}（周日）"
            + (report.Partial ? $" · 本周还没过完，统计到 {localGen:M月d日 HH:mm}" : "");
        weeklyRange.Foreground = Muted;

        var kpis = new UniformGrid { Columns = Math.Min(5, Math.Max(1, report.Kpis.Count)), Margin = new Thickness(-6, 4, -6, 8) };
        foreach (var k in report.Kpis) kpis.Children.Add(KpiCard(k));
        weeklyBody.Children.Add(kpis);

        weeklyBody.Children.Add(SectionTitle("本周各盘", report.Drives.All(d => d.Period == null) ? "这一周还没有容量记录。程序运行并开始监测后，将显示每天的占用变化。" : "柱状图表示当天已用空间，没有记录的日期不显示柱子。"));
        var drives = new UniformGrid { Columns = Math.Min(2, Math.Max(1, report.Drives.Count)), Margin = new Thickness(-6, 0, -6, 8) };
        foreach (var d in report.Drives) drives.Children.Add(DriveCard(d, report.Partial));
        weeklyBody.Children.Add(drives);

        weeklyBody.Children.Add(SectionTitle("增长最多的应用 / 来源", null));
        weeklyBody.Children.Add(AppsBlock(report));

        weeklyBody.Children.Add(SectionTitle("提醒", null));
        weeklyBody.Children.Add(AlertsBlock(report, zone));

        weeklyBody.Children.Add(SectionTitle("塞满预测", "根据近期剩余空间变化估算，数据不足时不给出天数。"));
        weeklyBody.Children.Add(ForecastBlock(report));
    }

    private UIElement SectionTitle(string title, string? hint)
    {
        var p = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
        p.Children.Add(Copy(title, 15));
        if (!string.IsNullOrEmpty(hint)) p.Children.Add(Copy(hint, 11, Muted));
        return p;
    }

    private UIElement KpiCard(WeeklyKpi k)
    {
        var color = k.Tone switch
        {
            "up" => Hex("#B56A22"),
            "down" => Hex("#2E7D32"),
            "warn" => Danger,
            "empty" => Muted,
            _ => Ink
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = k.Label, FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 0, 0, 4) });
        var value = new TextBlock { Text = k.Value, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = color };
        body.Children.Add(value);
        body.Children.Add(new TextBlock { Text = k.Hint, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var card = new Border { Background = Hex("#F7FAFB"), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(6), Child = body };
        System.Windows.Automation.AutomationProperties.SetName(card, k.Label + " " + k.Value + "，" + k.Hint);
        return card;
    }

    private UIElement DriveCard(WeeklyDriveReport d, bool partial)
    {
        var body = new StackPanel();
        var head = new DockPanel();
        if (d.FreeBytes is long free)
        {
            var remain = new TextBlock { Text = "剩余 " + Labels.Size(free), FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(remain, Dock.Right); head.Children.Add(remain);
        }
        head.Children.Add(new TextBlock { Text = d.Root, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ink });
        body.Children.Add(head);

        if (d.Period is { } row)
        {
            var net = WeeklySummary.SignedGiB(row.NetGiB);
            var tone = row.NetGiB > 0.005 ? Hex("#B56A22") : row.NetGiB < -0.005 ? Hex("#2E7D32") : Ink;
            body.Children.Add(new TextBlock { Text = net, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = tone, Margin = new Thickness(0, 6, 0, 0) });
            body.Children.Add(Copy($"已用净变化，累计增长 {row.ObservedGrowthGiB:N2} GiB，释放 {row.ObservedReleaseGiB:N2} GiB", 11, Muted));
            body.Children.Add(WeekBars(d));
            var note = $"采样 {row.Samples:N0} 次";
            if (row.Coverage.StartsWith("有采样间断")) note += "，期间监测有中断（例如关机或退出程序）";
            if (partial) note += "，统计至生成时";
            body.Children.Add(Copy(note, 11, Muted));
        }
        else
        {
            body.Children.Add(Copy("这一周还没有这个磁盘的容量记录。", 13, Muted));
            body.Children.Add(Copy("开始监测后，将在这里显示每天的占用变化。", 11, Muted));
            body.Children.Add(WeekBars(d));
        }

        if (d.Forecast is { Length: > 0 } f) body.Children.Add(ForecastChip(d.Root, f, thick: false));
        else body.Children.Add(Chip("塞满预测：记录不足，暂不预测", Muted, Hex("#EEF2F5")));

        var card = new Border { Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(6), Child = body };
        System.Windows.Automation.AutomationProperties.SetName(card, d.Root + (d.Period is { } p ? " 已用净变化 " + WeeklySummary.SignedGiB(p.NetGiB) : " 本周没有容量采样"));
        return card;
    }

    private UIElement WeekBars(WeeklyDriveReport d)
    {
        const double height = 72;
        var used = d.Days.Where(x => x.UsedBytes is long).Select(x => (double)x.UsedBytes!).ToList();
        double min = used.Count > 0 ? used.Min() : 0, max = used.Count > 0 ? used.Max() : 0, span = max - min;
        var row = new UniformGrid { Columns = 7, Height = height + 22, Margin = new Thickness(0, 10, 0, 6) };
        foreach (var day in d.Days)
        {
            var col = new DockPanel { Margin = new Thickness(3, 0, 3, 0) };
            var label = new TextBlock { Text = "周" + day.Weekday, FontSize = 11, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(label, Dock.Bottom); col.Children.Add(label);
            var slot = new Grid { Height = height };
            if (day.UsedBytes is long bytes)
            {
                double h = span < 50L * 1048576 ? height * 0.45 : Math.Max(6, 8 + (bytes - min) / span * (height - 16));
                var bar = new Border { Height = h, Background = Accent, CornerRadius = new CornerRadius(3, 3, 1, 1), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch };
                bar.ToolTip = $"{day.Date:M月d日}（周{day.Weekday}）：已用 {Labels.Size(bytes)}，剩余 {Labels.Size(day.FreeBytes ?? 0)}，当天净变化 {(day.NetBytes is long n ? Labels.SignedSize(n) : "—")}，{day.Samples} 次采样";
                slot.Children.Add(bar);
            }
            else
            {
                var tick = new Border { Height = 2, Background = Line, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch };
                var future = day.Date > TimeZoneInfo.ConvertTime(DateTimeOffset.Now, TimeZoneInfo.Local).Date;
                tick.ToolTip = $"{day.Date:M月d日}（周{day.Weekday}）：" + (future ? "还没到这一天" : "这一天没有容量记录（程序未运行或刚开始监测）");
                slot.Children.Add(tick);
            }
            col.Children.Add(slot);
            row.Children.Add(col);
        }
        System.Windows.Automation.AutomationProperties.SetName(row, d.Root + " 一周占用柱状图");
        return row;
    }

    private UIElement AppsBlock(WeeklyReport report)
    {
        if (report.TopApps.Count == 0)
        {
            bool anyScan = report.Drives.Any(d => d.Apps is { Compared: true });
            return Copy(anyScan ? "两次扫描之间没有发现增长的应用或来源。" : "还没有可对比的两次扫描。完成两次完整扫描后即可查看增长排行。", 12, Muted);
        }
        var p = new StackPanel();
        double max = report.TopApps[0].GrowthBytes;
        int rank = 0;
        foreach (var row in report.TopApps.Take(8))
        {
            rank++;
            var line = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var size = new TextBlock { Text = Labels.SignedSize(row.GrowthBytes), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Hex("#B56A22"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 96, TextAlignment = TextAlignment.Right, Margin = new Thickness(10, 0, 0, 0) };
            DockPanel.SetDock(size, Dock.Right); line.Children.Add(size);
            var name = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = $"{rank}. {row.App}", FontSize = 13, Foreground = row.Known ? Ink : Hex("#4A5A64"), VerticalAlignment = VerticalAlignment.Center });
            if (!row.Known) title.Children.Add(new Border { Background = Hex("#EEF2F5"), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "未识别目录", FontSize = 10, Foreground = Muted } });
            title.Children.Add(new TextBlock { Text = "  " + row.Root, FontSize = 11, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
            name.Children.Add(title);
            var bar = new ProgressBar { Height = 7, Maximum = 1, Value = max <= 0 ? 0 : (double)row.GrowthBytes / max, Foreground = Accent, Background = Hex("#EAF0F3"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 5, 0, 0) };
            name.Children.Add(bar);
            name.Children.Add(new TextBlock { Text = $"当前 {Labels.SmartSize(row.CurrentBytes)}，占所在磁盘本次增长 {row.Share:P0}", FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) });
            line.Children.Add(name);
            System.Windows.Automation.AutomationProperties.SetName(line, $"第 {rank} 名 {row.App}（{row.Root}），增长 {Labels.SignedSize(row.GrowthBytes)}");
            p.Children.Add(line);
        }
        if (report.TopApps.Count > 8) p.Children.Add(Copy($"另有 {report.TopApps.Count - 8} 项较小的增长未列出。", 11, Muted));
        return p;
    }

    private UIElement AlertsBlock(WeeklyReport report, TimeZoneInfo zone)
    {
        if (report.Alerts.Count == 0) return Copy("本周没有触发提醒。", 12, Muted);
        var p = new StackPanel();
        p.Children.Add(Copy($"本周共 {report.Alerts.Count} 条：" + string.Join("，", report.Alerts.GroupBy(a => a.Kind).Select(g => $"{WeeklySummary.KindName(g.Key)} {g.Count()} 条")) + "。", 12, Muted));
        foreach (var a in report.Alerts.TakeLast(10))
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var when = new TextBlock { Text = TimeZoneInfo.ConvertTime(a.Time, zone).ToString("MM-dd HH:mm"), FontSize = 12, Foreground = Muted, MinWidth = 88, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
            DockPanel.SetDock(when, Dock.Left); row.Children.Add(when);
            var text = new StackPanel();
            text.Children.Add(Chip(WeeklySummary.KindName(a.Kind), a.Kind == "LowSpace" ? Hex("#B56A22") : Danger, a.Kind == "LowSpace" ? Hex("#F8F1E7") : DangerSoft));
            text.Children.Add(new TextBlock { Text = a.Message, FontSize = 13, Foreground = Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            row.Children.Add(text);
            p.Children.Add(row);
        }
        if (report.Alerts.Count > 10) p.Children.Add(Copy($"较早的 {report.Alerts.Count - 10} 条见下方“提醒历史”。", 11, Muted));
        return p;
    }

    private UIElement ForecastBlock(WeeklyReport report)
    {
        var wrap = new WrapPanel();
        foreach (var d in report.Drives)
        {
            if (d.Forecast is { Length: > 0 } f) wrap.Children.Add(ForecastChip(d.Root, f, thick: true));
            else wrap.Children.Add(Chip($"{d.Root}  没有足够的采样，暂不预测", Muted, Hex("#EEF2F5")));
        }
        if (wrap.Children.Count == 0) return Copy("没有可显示的塞满预测。", 12, Muted);
        return wrap;
    }

    private Border ForecastChip(string root, FillForecast[] f, bool thick)
    {
        var main = f[0];
        var fg = main.Kind == ForecastKind.Growing ? (main.DaysToFull < 30 ? Danger : Hex("#B56A22")) : Muted;
        var bg = main.Kind == ForecastKind.Growing ? (main.DaysToFull < 30 ? DangerSoft : Hex("#F8F1E7")) : Hex("#EEF2F5");
        var chip = Chip($"{root}  {main.Short}", fg, bg);
        chip.ToolTip = Forecast.Describe(f);
        chip.Margin = new Thickness(0, 0, 10, 8);
        if (thick) chip.Padding = new Thickness(10, 6, 10, 6);
        System.Windows.Automation.AutomationProperties.SetName(chip, root + " 塞满预测：" + Forecast.Describe(f));
        return chip;
    }

    private static Border Chip(string text, Brush fg, Brush bg) => new()
    {
        Background = bg, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, FontSize = 12, Foreground = fg }
    };
}