using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DiskWatch.Core;

namespace DiskWatch.App;

public sealed partial class MainWindow
{
    private readonly TabControl tabs = new();
    private readonly TabItem summaryTab = new() { Header="扫描总结" };
    private readonly TabItem trendTab = new() { Header="空间趋势" };
    private readonly TextBox summaryText = new() { IsReadOnly=true, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, FontSize=14, Padding=new Thickness(18), BorderThickness=new Thickness(0) };
    private readonly TrendChart chart = new() { MinHeight=300, Margin=new Thickness(8) };
    private readonly ComboBox trendRange = new() { ItemsSource=new[]{"最近24小时","最近7天","最近30天"}, SelectedIndex=0, MinWidth=125, Margin=new Thickness(4) };
    private readonly ComboBox trendMetric = new() { ItemsSource=new[]{"剩余空间","已用空间","已用净变化"}, SelectedIndex=0, MinWidth=125, Margin=new Thickness(4) };
    private readonly ComboBox trendRoot = new() { MinWidth=100, Margin=new Thickness(4) };
    private readonly TextBlock trendNote = new() { Margin=new Thickness(12), TextWrapping=TextWrapping.Wrap };
    private List<VolumeSample> trendSamples = new();
    private int trendRequest;

    private void AddSummaryAndTrendTabs()
    {
        summaryBody.Children.Add(Copy("磁盘",14,Muted));
        summaryBody.Children.Add(driveCards);
        summaryBody.Children.Add(diskDetails);
        var scope=new WrapPanel();scope.Children.Add(Copy("已扫描范围",12,Muted));scope.Children.Add(selectedRoot);
        summaryBody.Children.Add(Fold("其他扫描范围", "查看单独扫描过的文件夹", scope));
        summaryTab.Content=new ScrollViewer{Content=summaryBody,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};tabs.Items.Add(summaryTab);
        var trend=new DockPanel();var tools=new WrapPanel();
        trendRoot.Items.Add("全部卷");foreach(var root in settings.Roots)trendRoot.Items.Add(root);trendRoot.SelectedIndex=0;
        tools.Children.Add(trendRange);tools.Children.Add(trendMetric);tools.Children.Add(trendRoot);tools.Children.Add(Button("刷新曲线",RefreshTrend));tools.Children.Add(Button("导出曲线数据 CSV",ExportTrend));
        DockPanel.SetDock(tools,Dock.Top);trend.Children.Add(tools);DockPanel.SetDock(trendNote,Dock.Bottom);trend.Children.Add(trendNote);trend.Children.Add(chart);trendTab.Content=Card(trend);tabs.Items.Add(trendTab);
        trendRange.SelectionChanged+=async(_,_)=>await Safe(RefreshTrend);trendMetric.SelectionChanged+=async(_,_)=>await Safe(RefreshTrend);trendRoot.SelectionChanged+=async(_,_)=>await Safe(RefreshTrend);
        tabs.SelectionChanged+=async(sender,args)=>{if(args.Source==tabs && tabs.SelectedItem==trendTab)await Safe(RefreshTrend);};
    }
    private async Task RefreshSummary()
    {
        var roots=settings.Roots.Concat(selectedRoot.Items.Cast<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        summaryText.Text=await Task.Run(()=>
        {
            var pairs=new List<(Snapshot,Snapshot?)>();var missing=new List<string>();
            foreach(var root in roots){var list=repository.LatestSnapshots(root,2);if(list.Count>0)pairs.Add((list[0],list.ElementAtOrDefault(1)));else missing.Add(root);}
            return (pairs.Count>0?ScanSummary.Build(pairs):"还没有扫描结果。请先选择范围并开始扫描。")+(missing.Count>0?"\n尚未扫描："+string.Join("、",missing):"");
        });
    }
    private async Task SaveSummary()
    {
        if(string.IsNullOrEmpty(summaryText.Text))await RefreshSummary();
        var folder=Path.Combine(Program.Root,"exports");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,$"scan-summary-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
        await File.WriteAllTextAsync(path,summaryText.Text,new UTF8Encoding(true));status.Text="扫描总结已保存："+path;
    }
    private async Task RefreshTrend()
    {
        int request=++trendRequest;var days=trendRange.SelectedIndex switch {1=>7,2=>30,_=>1};var root=trendRoot.SelectedItem as string;var metric=trendMetric.SelectedItem as string??"剩余空间";
        var points=await Task.Run(()=>repository.Samples(DateTimeOffset.UtcNow.AddDays(-days)));
        if(request!=trendRequest)return;
        trendSamples=points.Where(s=>root=="全部卷"||s.Root==root).OrderBy(s=>s.Time).ToList();
        chart.SetData(trendSamples,metric,settings.SampleSeconds);
        trendNote.Text=$"{trendSamples.Count:N0} 条容量记录，每 {settings.SampleSeconds} 秒记录一次。曲线反映整盘容量变化，不对应具体目录或进程。选择单个磁盘可查看更细的变化。";
    }
    private Task ExportTrend()
    {
        if(trendSamples.Count==0){status.Text="当前时间范围暂无数据。";return Task.CompletedTask;}
        var path=ExportPath("trend");Reports.WriteCsv(path,new[]{"时间","卷","卷标识","总容量GiB","已用GiB","剩余GiB"},trendSamples.Select(s=>new object?[]{s.Time.ToLocalTime().ToString("O"),s.Root,s.VolumeId,s.TotalBytes/1073741824d,s.UsedBytes/1073741824d,s.FreeBytes/1073741824d}));status.Text="曲线数据已导出："+path;return Task.CompletedTask;
    }
}
