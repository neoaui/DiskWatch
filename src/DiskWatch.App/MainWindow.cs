using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using DiskWatch.Core;
using DiskWatch.Storage;
using DiskWatch.Windows;
using Forms = System.Windows.Forms;

namespace DiskWatch.App;

public sealed partial class MainWindow : Window
{
    private Settings settings;
    private readonly Repository repository;
    private readonly bool smoke;
    private readonly bool uiReview;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly DispatcherTimer timer = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Foreground = Brushes.DimGray };
    private readonly TextBlock snapshotNote = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly DataGrid volumes = Grid(("卷", "Root"), ("总容量 GiB", "Total"), ("已用 GiB", "Used"), ("剩余 GiB", "Free"), ("剩余比例", "Percent"), ("采样时间", "Time"));
    private readonly DataGrid categories = Grid(("分类", "Name"), ("占用 GiB", "GiB"), ("口径", "Note"));
    private readonly DataGrid changes = Grid(("目录", "Path"), ("分类", "Category"), ("本次 GiB", "CurrentGiB"), ("变化 GiB", "ChangeGiB"), ("质量说明", "Quality"));
    private readonly DataGrid largeFiles = Grid(("文件", "Path"), ("逻辑 GiB", "Logical"), ("分配 GiB", "Allocated"), ("分类", "Category"), ("规则分", "Confidence"), ("证据", "Evidence"));
    private readonly DataGrid alerts = Grid(("时间", "Time"), ("卷", "Root"), ("类型", "Kind"), ("提醒内容", "Message"));
    private readonly DataGrid reports = Grid(("期间", "Period"), ("卷", "Root"), ("期初观测 GiB", "FirstUsedGiB"), ("期末观测 GiB", "LastUsedGiB"), ("净变化 GiB", "NetGiB"), ("观测增长 GiB", "ObservedGrowthGiB"), ("观测释放 GiB", "ObservedReleaseGiB"), ("采样数", "Samples"), ("覆盖说明", "Coverage"));
    private readonly DataGrid issues = Grid(("路径", "Path"), ("原因", "Reason"));
    private readonly ComboBox selectedRoot = new() { MinWidth = 100, Margin = new Thickness(4) };
    private readonly ComboBox period = new() { ItemsSource = new[] { "日", "周", "月" }, SelectedIndex = 0, Width = 70, Margin = new Thickness(4) };

    private readonly TextBox scheduleInput = new() { Width = 100 };
    private readonly CheckBox scheduleEnabled = new() { Content = "程序运行时每日自动扫描" };
    private readonly CheckBox fastScan = new() { Content = "NTFS 快速扫描", Margin = new Thickness(0, 10, 0, 4) };
    private readonly TextBlock elevationStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 0, 0, 6) };
    private readonly bool elevated = MftScanner.IsElevated();
    private bool fastScanTouched;
    private readonly CheckBox installationMetadata = new() { Content = "读取已安装应用和游戏库信息，用于识别应用占用。", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox thresholdsInput = new() { Width = 200 };
    private readonly TextBox suddenInput = new() { Width = 100 };
    private readonly TextBox rulePath = new() { MinWidth = 280 };
    private readonly ComboBox ruleCategory = new() { MinWidth = 200 };
    private readonly ListBox rulesList = new() { Height = 110 };
    private List<PeriodReport> reportRows = [];
    private Snapshot? displayedSnapshot;
    private CancellationTokenSource? scanCancellation;
    private bool scanning, sampling, paused, closing, warnedFailure;
    private string? lastMaintenance;

    public MainWindow(Settings settings, Repository repository, bool smoke, bool uiReview = false)
    {
        this.settings = settings; this.repository = repository; this.smoke = smoke; this.uiReview = uiReview;
        Title = "DiskWatch2" + (uiReview ? "（界面验收：定时扫描暂停）" : "");
        Icon = AppIcon.WindowIcon();
        Width = Math.Min(1220, SystemParameters.WorkArea.Width - 20); Height = Math.Min(820, SystemParameters.WorkArea.Height - 20);
        MinWidth = Math.Min(900, Width); MinHeight = Math.Min(600, Height); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        BuildModernShell();
        selectedRoot.SelectionChanged += async (_, _) => await Safe(LoadSnapshot);
        trayIcon = AppIcon.TrayIcon();
        tray = new Forms.NotifyIcon { Text = "DiskWatch2", Icon = trayIcon, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("打开", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("暂停 / 恢复", null, (_, _) => Dispatcher.Invoke(TogglePause));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.InvokeAsync(async () => await Exit())); tray.ContextMenuStrip = menu;
        Closing += (_, e) => { if (!closing) { e.Cancel = true; Hide(); tray.ShowBalloonTip(3000, "DiskWatch2", "已最小化到托盘。退出请使用托盘菜单。", Forms.ToolTipIcon.Info); } };
        timer.Interval = TimeSpan.FromSeconds(settings.SampleSeconds); timer.Tick += async (_, _) => await Safe(Tick);
        Loaded += async (_, _) =>
        {
            Program.Log("桌面启动" + (smoke ? "（UI 验证模式）" : "") + (elevated ? "（管理员）" : Program.ElevationDeclined ? "（普通权限）" : "（普通权限）"));
            var unfinished = repository.GetState<string>("scan:active");
            if (!string.IsNullOrEmpty(unfinished)) { status.Text = "上次扫描未完成：" + unfinished + "已保留此前的完整结果，可重新扫描。"; repository.SetState("scan:active", ""); }
            await Safe(Sample); await Safe(LoadSnapshot); await Safe(RefreshSummary); await Safe(RefreshTrend); await Safe(EnsureWeekly); timer.Start();
            if (smoke) { await Task.Delay(12000); await Exit(); }
        };
        PopulateSettings(); status.Text = "已就绪，选择磁盘查看占用";
    }
    private static DataGrid Grid(params (string Header, string Binding)[] columns)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, RowHeight = 30, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252)), Margin = new Thickness(4) };
        foreach (var (header, binding) in columns) grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(binding) { StringFormat = binding.EndsWith("GiB") ? "N3" : null }, MinWidth = 90, MaxWidth = 650, Width = DataGridLength.SizeToCells });
        return grid;
    }
    private Button Button(string label, Func<Task> action)
    {
        var b = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4) };
        b.Click += async (_, _) => await Safe(action); return b;
    }
    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Program.Log(ex.ToString()); status.Text = "操作失败：" + ex.Message; if (!warnedFailure) { warnedFailure = true; tray.ShowBalloonTip(5000, "DiskWatch2 需要注意", ex.Message, Forms.ToolTipIcon.Warning); } }
    }
    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void TogglePause() { paused = !paused; if (paused) scanCancellation?.Cancel(); status.Text = paused ? "已暂停容量采样和定时扫描。" : "已恢复监测。"; tray.Text = paused ? "DiskWatch2（暂停）" : "DiskWatch2"; }
    private async Task Tick()
    {
        if (paused || closing) return;
        await Sample();
        if (smoke || uiReview || scanning || !settings.ScheduledScans) return;
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (DateTime.Now.TimeOfDay < new TimeSpan(settings.ScanHour, settings.ScanMinute, 0)) return;
        var pending = settings.Roots.Where(r => repository.GetState<string>("scan:date:" + r) != today && Directory.Exists(r)).ToList();
        if (pending.Count > 0) await ScanRoots(pending);
    }
    private async Task Sample()
    {
        if (sampling) return; sampling = true;
        try
        {
            var roots = settings.Roots.ToList();
            var result = await Task.Run(() =>
            {
                var samples = new List<VolumeSample>(); var notifications = new List<Alert>(); var failures = new List<string>();
                foreach (var root in roots)
                {
                    try { var s = NativeMetadata.Sample(root); if (samples.All(x => x.VolumeId != s.VolumeId)) { samples.Add(s); notifications.AddRange(repository.RecordSample(s)); } }
                    catch (Exception ex) { failures.Add(root + "：" + ex.Message); }
                }
                return (samples, notifications, failures);
            });
            UpdateDriveCards(result.samples);
            if (!scanning) RefreshScanRangeOptions();
            volumes.ItemsSource = result.samples.Select(x => new { x.Root, Total = (x.TotalBytes / 1073741824d).ToString("N2"), Used = (x.UsedBytes / 1073741824d).ToString("N2"), Free = (x.FreeBytes / 1073741824d).ToString("N2"), Percent = ((double)x.FreeBytes / x.TotalBytes).ToString("P1"), Time = x.Time.ToLocalTime().ToString("HH:mm:ss") }).ToList();
            foreach (var alert in result.notifications) { tray.ShowBalloonTip(6000, "DiskWatch2 空间提醒", alert.Message, Forms.ToolTipIcon.Warning); Program.Log(alert.Message); }
            captureQueue.AddRange(result.notifications.Where(a => a.Kind == "SuddenDrop"));
            alerts.ItemsSource = repository.RecentAlerts().Select(x => new { Time = x.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), x.Root, Kind = WeeklySummary.KindName(x.Kind), x.Message }).ToList();
            if (result.failures.Count > 0) { status.Text = "部分采样失败：" + string.Join("；", result.failures); Program.Log(status.Text); }
            if (lastMaintenance != DateTime.Now.ToString("yyyy-MM-dd")) { await Task.Run(repository.Maintain); lastMaintenance = DateTime.Now.ToString("yyyy-MM-dd"); }
            if (tabs.SelectedItem == trendTab) await RefreshTrend();
        }
        finally { sampling = false; }
        // 骤降抓现场（可选，默认关闭）：采样结束后再判断，避免和采样互相等待。
        if (captureQueue.Count > 0) { var due = captureQueue.ToList(); captureQueue.Clear(); foreach (var a in due) _ = Safe(() => MaybeAutoCapture(a)); }
        await RefreshForecasts();
    }
    private async Task ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择要扫描的文件夹" };
        if (dialog.ShowDialog(this) == true) await ScanRoots([dialog.FolderName]);
    }
    private async Task ScanRoots(List<string> roots, bool automatic = false)
    {
        if (scanning) { status.Text = "已有扫描正在进行，请等待或取消。"; return; }
        if (paused) { status.Text = "请先恢复监测。"; return; }
        scanning = true; scanCancellation = new CancellationTokenSource();
        scanAction.IsEnabled = false; cancelAction.Visibility = Visibility.Visible; scanRange.IsEnabled = ScanRangeChoice.CanChange(true);
        try
        {
            var context = await Task.Run(() => ApplicationDiscovery.Discover(settings));
            var scanner = new Scanner(new Classifier(context), settings, Program.RuleVersion(settings));
            int index = 0;
            foreach (var root in roots)
            {
                if (scanCancellation.IsCancellationRequested) break;
                index++;
                repository.SetState("scan:date:" + root, DateTime.Now.ToString("yyyy-MM-dd"));
                repository.SetState("scan:active", root + " @ " + DateTimeOffset.Now.ToString("O"));
                status.Text = "正在扫描 " + root;
                // 百分比估算：已扫描分配空间 / 卷已用空间（只对整盘扫描有效；文件夹扫描显示不确定进度）。
                long? expected = null; bool wholeVolume = false;
                try { var full = Path.GetFullPath(root); var sample = NativeMetadata.Sample(full); wholeVolume = Classifier.Normalize(full).Equals(Classifier.Normalize(sample.Root), StringComparison.OrdinalIgnoreCase); if (wholeVolume) expected = sample.UsedBytes; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
                var title = roots.Count > 1 ? $"正在扫描 {root} · 第 {index}/{roots.Count} 个{(wholeVolume ? "盘" : "范围")}" : $"正在扫描 {root}";
                var estimator = new ScanEstimator(expected, DateTimeOffset.Now);
                ScanProgress? last = null;
                void Refresh() { var p = last; RenderScanBanner(title, estimator.Update(p?.AllocatedBytes ?? 0, p?.CurrentDirectory == "正在读取文件目录", DateTimeOffset.Now), p?.Files ?? 0, p?.AllocatedBytes ?? 0, p?.Errors ?? 0, p?.CurrentDirectory ?? root); }
                var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; ticker.Tick += (_, _) => Refresh();
                Refresh(); ticker.Start();
                var progress = new Progress<ScanProgress>(p => { last = p; status.Text = $"正在扫描 {p.Root}，已扫描 {p.Files:N0} 个文件，{Labels.Size(p.AllocatedBytes)}，{p.Errors} 项读取失败，{p.CurrentDirectory}"; });
                Snapshot snapshot;
                try { snapshot = await scanner.ScanAsync(root, progress, scanCancellation.Token); }
                finally { ticker.Stop(); }
                var doneTitle = (snapshot.Status == "Cancelled" ? $"{root} 扫描已取消" : $"{root} 扫描完成") + (roots.Count > 1 ? $" · 第 {index}/{roots.Count} 个" : "");
                RenderScanBanner(doneTitle, snapshot.Status == "Cancelled" ? new ScanEstimate(estimator.Update(snapshot.AllocatedBytes, false, DateTimeOffset.Now).Fraction, false, DateTimeOffset.Now - snapshot.Started, null, "") : estimator.Complete(DateTimeOffset.Now), snapshot.Files, snapshot.AllocatedBytes, snapshot.Errors, "");
                await Task.Run(() => repository.SaveSnapshot(snapshot));
                await SaveFileDetails(snapshot);
                repository.SetState("scan:active", "");
                if (!selectedRoot.Items.Contains(snapshot.Root)) selectedRoot.Items.Add(snapshot.Root);
                selectedRoot.SelectedItem = snapshot.Root;
                await LoadSnapshot();
                status.Text = $"{snapshot.Root} 扫描结束：{snapshot.Coverage}，{snapshot.Files:N0} 个文件，用时 {(snapshot.Finished - snapshot.Started).TotalSeconds:N0} 秒。";
                Program.Log(status.Text);
            }
        }
        finally
        {
            scanning = false; scanCancellation.Dispose(); scanCancellation = null;
            _ = HideScanBannerLater();
            scanAction.IsEnabled = true; cancelAction.Visibility = Visibility.Collapsed; scanRange.IsEnabled = true;
            await RefreshSummary();
            await SaveSummary();
            // 骤降抓现场是后台自动扫描：不切换用户正在看的页面，结果另有提示。
            if (!automatic)
            {
                Navigate(summaryTab, "空间总览", "选择磁盘，查看空间占用。");
                tray.ShowBalloonTip(4000, "DiskWatch2 扫描结果", "扫描已结束，总结已保存。可在「扫描总结」中查看完成状态和覆盖说明。", Forms.ToolTipIcon.Info);
            }
        }
    }
    private async Task LoadSnapshot()
    {
        if (selectedRoot.SelectedItem is not string root) return;
        var snapshots = await Task.Run(() => repository.LatestSnapshots(root));
        if (snapshots.Count == 0) { snapshotNote.Text = "此范围还没有扫描结果。容量监测不会代替目录扫描。"; categories.ItemsSource = null; changes.ItemsSource = null; largeFiles.ItemsSource = null; issues.ItemsSource = null; displayedSnapshot = null; RenderEmptyDisk(root); return; }
        var s = snapshots[0]; displayedSnapshot = s;
        var categoryRows = Enum.GetValues<Category>().Select(c => new
        {
            Name = Labels.CategoryName(c),
            GiB = c == Category.Unexplained ? s.Residual is long residual ? (residual / 1073741824d).ToString("N3") : "不适用" : (s.Buckets.Where(x => x.Category == c).Sum(x => x.AllocatedBytes) / 1073741824d).ToString("N3"),
            Note = c == Category.Unexplained ? "整盘已用减去已解释占用，可能包括权限限制、文件系统开销和扫描时差" : "按磁盘占用计算，硬链接只计一次"
        }).ToList();
        categories.ItemsSource = categoryRows;
        snapshotNote.Text = $"{s.Root}，{s.Finished.ToLocalTime():yyyy-MM-dd HH:mm:ss}，{s.Coverage}\n已解释占用 {Labels.Size(s.AllocatedBytes)}，逻辑大小 {Labels.Size(s.LogicalBytes)}。" +
            (s.IsWholeVolume ? $" 卷已用 {Labels.Size(s.After?.UsedBytes ?? 0)}；扫描期间卷净变化 {Labels.Size((s.After?.UsedBytes ?? 0) - (s.Before?.UsedBytes ?? 0))}。" : " 当前为文件夹扫描，不与整盘占用合计。") +
            (s.Residual < 0 ? " 注意：出现负差额，结果需要核对，未做凑平处理。" : "") +
            "\n各目录互不重复统计。分类纠正在下次扫描后生效。";
        changes.ItemsSource = Reports.Compare(s, snapshots.ElementAtOrDefault(1));
        largeFiles.ItemsSource = s.LargestFiles.Select(x => new { x.Path, Logical = (x.LogicalBytes / 1073741824d).ToString("N3"), Allocated = (x.AllocatedBytes / 1073741824d).ToString("N3"), Category = Labels.CategoryName(x.Category), x.Confidence, x.Evidence }).ToList();
        issues.ItemsSource = s.Issues;
        RenderDiskDetails(s, snapshots.ElementAtOrDefault(1));
    }
    private async Task BuildReports()
    {
        var selected = period.SelectedItem as string ?? "日"; status.Text = "正在生成历史报表…";
        reportRows = await Task.Run(() => Reports.Build(repository.Samples(DateTimeOffset.UtcNow.AddDays(-settings.HistoryDays)), selected, TimeZoneInfo.Local));
        reports.ItemsSource = reportRows; status.Text = $"已生成 {reportRows.Count} 行{selected}报。";
    }
    private Task ExportReport()
    {
        if (reportRows.Count == 0) { status.Text = "请先生成报表。"; return Task.CompletedTask; }
        var path = ExportPath("period");
        Reports.WriteCsv(path, ["期间", "卷", "卷标识", "期初观测", "期末观测", "期初已用GiB", "期末已用GiB", "净变化GiB", "观测增长GiB", "观测释放GiB", "采样数", "覆盖说明"], reportRows.Select(x => new object?[] { x.Period, x.Root, x.VolumeId, x.FirstSample, x.LastSample, x.FirstUsedGiB, x.LastUsedGiB, x.NetGiB, x.ObservedGrowthGiB, x.ObservedReleaseGiB, x.Samples, x.Coverage }));
        status.Text = "已导出：" + path; return Task.CompletedTask;
    }
    private Task ExportSnapshot()
    {
        var s = displayedSnapshot; if (s == null) { status.Text = "请先扫描并选择范围。"; return Task.CompletedTask; }
        var path = ExportPath("categories");
        Reports.WriteCsv(path, ["范围", "完成时间", "分类", "分配字节", "GiB", "覆盖状态", "口径"], Enum.GetValues<Category>().Select(c =>
        {
            long? bytes = c == Category.Unexplained ? s.Residual : s.Buckets.Where(x => x.Category == c).Sum(x => x.AllocatedBytes);
            return new object?[] { s.Root, s.Finished.ToLocalTime().ToString("O"), Labels.CategoryName(c), bytes, bytes / 1073741824d, s.Coverage, c == Category.Unexplained ? "未解释差额，不能视为系统占用或可清理空间" : "已扫描分配空间" };
        }));
        status.Text = "已导出：" + path; return Task.CompletedTask;
    }
    private static string ExportPath(string kind) { var folder = Path.Combine(Program.Root, "exports"); Directory.CreateDirectory(folder); return Path.Combine(folder, $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.csv"); }
    private UIElement SettingsPanel()
    {
        var panel=new StackPanel();
        var schedule=new StackPanel();schedule.Children.Add(scheduleEnabled);schedule.Children.Add(Copy("每日扫描时间",12,Muted));schedule.Children.Add(scheduleInput);
        var locations=new StackPanel();locations.Children.Add(ScopeEditor());locations.Children.Add(fastScan);locations.Children.Add(Copy("在 NTFS 磁盘上加快扫描。需管理员权限，不可用时自动改用普通扫描。",11,Muted));locations.Children.Add(elevationStatus);fastScan.Click+=(_,_)=>fastScanTouched=true;schedule.Children.Add(Fold("调整扫描范围","勾选要扫描的磁盘或文件夹",locations));
        panel.Children.Add(Card(Fold("扫描计划","选择扫描时间和范围",schedule)));
        var alertsPanel=new StackPanel();alertsPanel.Children.Add(Copy("低空间提醒档位（GiB，用逗号分隔）",12,Muted));alertsPanel.Children.Add(thresholdsInput);alertsPanel.Children.Add(Copy("10 分钟内可用空间减少达到多少 GiB 时提醒",12,Muted));alertsPanel.Children.Add(suddenInput);alertsPanel.Children.Add(Copy("空间恢复后会重新启用提醒。同类骤降提醒至少间隔 30 分钟。",11,Muted));alertsPanel.Children.Add(AutoCaptureSettings());
        var alertCard=Card(Fold("空间提醒","剩余空间不足或突然下降时发出提醒",alertsPanel));alertCard.Margin=new Thickness(0,12,0,0);panel.Children.Add(alertCard);
        var rules=new StackPanel();rules.Children.Add(installationMetadata);rules.Children.Add(Copy("将目录及其内容归入指定分类，下次扫描生效。",12,Muted));rules.Children.Add(rulePath);
        ruleCategory.ItemsSource=Enum.GetValues<Category>().Where(x=>x!=Category.Unexplained).Select(x=>new{Id=x,Name=Labels.CategoryName(x)}).ToList();ruleCategory.DisplayMemberPath="Name";ruleCategory.SelectedValuePath="Id";ruleCategory.SelectedIndex=0;rules.Children.Add(ruleCategory);rules.Children.Add(Button("添加 / 更新规则",AddRule));rules.Children.Add(rulesList);rules.Children.Add(Button("撤销所选规则",RemoveRule));
        var rulesCard=Card(Fold("分类与纠正","调整文件分类",rules));rulesCard.Margin=new Thickness(0,12,0,0);panel.Children.Add(rulesCard);
        panel.Children.Add(GrowthSettingsCard());
        // 保存按钮固定在设置页底部，始终可见；有未保存的更改时旁边显示提示。
        var save=Button("保存设置",SaveSettings);save.Background=Accent;save.Foreground=Brushes.White;save.FontSize=14;save.FontWeight=FontWeights.SemiBold;save.Padding=new Thickness(22,8,22,8);
        var saveBar=new DockPanel{Margin=new Thickness(0,10,0,0)};var saveRow=new StackPanel{Orientation=Orientation.Horizontal};saveRow.Children.Add(save);saveRow.Children.Add(dirtyLabel);saveBar.Children.Add(saveRow);
        var saveCard=new Border{Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,10,0,0),Child=saveBar};
        HookDirty(scheduleEnabled,scheduleInput,fastScan,installationMetadata,thresholdsInput,suddenInput);
        var privacy=new StackPanel();privacy.Children.Add(Copy("记录保存在本机。关闭窗口后仍在托盘运行，退出后停止监测。",12,Muted));panel.Children.Add(Fold("数据与运行","记录保存在本机",privacy));
        var page=new DockPanel();DockPanel.SetDock(saveCard,Dock.Bottom);page.Children.Add(saveCard);page.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        return page;
    }
    private void PopulateSettings()
    {
        populatingSettings = true;
        LoadScope();
        scheduleInput.Text = $"{settings.ScanHour:00}:{settings.ScanMinute:00}"; scheduleEnabled.IsChecked = settings.ScheduledScans;
        installationMetadata.IsChecked = settings.ReadInstallationMetadata; fastScan.IsChecked = settings.FastScanEnabled(elevated); fastScanTouched = false; elevationStatus.Text = Labels.ElevationStatus(elevated, settings.FastScanEnabled(elevated)); elevationStatus.Foreground = elevated ? Accent : Muted;
        thresholdsInput.Text = string.Join(',', settings.ThresholdsGiB); suddenInput.Text = settings.SuddenDropGiB.ToString(); autoCapture.IsChecked = settings.AutoCaptureOnSuddenDrop;
        foreach (var root in settings.Roots) if (!selectedRoot.Items.Contains(root)) selectedRoot.Items.Add(root);
        if (selectedRoot.SelectedIndex < 0 && selectedRoot.Items.Count > 0) selectedRoot.SelectedIndex = 0;
        RefreshRules(); PopulateGrowth();
        populatingSettings = false; ClearDirty();
    }
    private void RefreshRules() => rulesList.ItemsSource = settings.Overrides.Select(x => x.Path + " → " + Labels.CategoryName(x.Category)).ToList();
    private Task SaveSettings()
    {
        if (scanning) throw new InvalidOperationException("扫描期间请先取消或等待完成，再更改设置。");
        var updated = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        updated.Roots = ScanScope.ToRoots(scopeItems); updated.ExcludedPaths = ScanScope.NormalizeList(exclusionItems);
        if (ScanScope.Validate(updated.Roots) is string empty) { status.Text = empty; MessageBox.Show(this, empty, "DiskWatch2", MessageBoxButton.OK, MessageBoxImage.Warning); return Task.CompletedTask; }
        if (!TimeSpan.TryParse(scheduleInput.Text, out var time) || time.TotalHours >= 24 || time < TimeSpan.Zero) throw new ArgumentException("时间应为 HH:mm。");
        updated.ScanHour = time.Hours; updated.ScanMinute = time.Minutes; updated.ScheduledScans = scheduleEnabled.IsChecked == true;
        updated.ReadInstallationMetadata = installationMetadata.IsChecked == true; if (fastScanTouched) updated.FastScan = fastScan.IsChecked == true; // 没动过这个勾就保持“未设置”（管理员时默认开启）
        updated.ThresholdsGiB = thresholdsInput.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x.Trim())).Distinct().OrderByDescending(x => x).ToList();
        updated.SuddenDropGiB = double.Parse(suddenInput.Text); updated.AutoCaptureOnSuddenDrop = autoCapture.IsChecked == true; ReadGrowth(updated); updated.Validate();
        SettingsFile.Save(Program.SettingsPath, updated);
        // The repository shares this object for alert policy; update fields without replacing its reference.
        settings.Roots = updated.Roots; settings.ExcludedPaths = updated.ExcludedPaths; settings.ScanHour = updated.ScanHour; settings.ScanMinute = updated.ScanMinute;
        settings.ScheduledScans = updated.ScheduledScans; settings.ThresholdsGiB = updated.ThresholdsGiB; settings.SuddenDropGiB = updated.SuddenDropGiB; settings.AutoCaptureOnSuddenDrop = updated.AutoCaptureOnSuddenDrop;
        settings.ReadInstallationMetadata = updated.ReadInstallationMetadata; settings.FastScan = updated.FastScan; ApplyGrowth(updated);
        PopulateSettings(); status.Text = "设置已保存。"; return Task.CompletedTask;
    }
    private Task AddRule()
    {
        if (scanning) throw new InvalidOperationException("请等扫描完成后再纠正分类。");
        if (!Path.IsPathFullyQualified(rulePath.Text.Trim())) throw new ArgumentException("请输入绝对路径。");
        var path = Path.GetFullPath(rulePath.Text.Trim());
        settings.Overrides.RemoveAll(x => Classifier.Normalize(x.Path).Equals(Classifier.Normalize(path), StringComparison.OrdinalIgnoreCase));
        settings.Overrides.Add(new(path, (Category)ruleCategory.SelectedValue)); SettingsFile.Save(Program.SettingsPath, settings); RefreshRules(); status.Text = "纠正规则已保存；下一次扫描生效。"; return Task.CompletedTask;
    }
    private Task RemoveRule()
    {
        if (scanning) throw new InvalidOperationException("请等扫描完成后再修改规则。");
        if (rulesList.SelectedIndex >= 0) { settings.Overrides.RemoveAt(rulesList.SelectedIndex); SettingsFile.Save(Program.SettingsPath, settings); RefreshRules(); }
        return Task.CompletedTask;
    }
    private async Task Exit()
    {
        if (closing) return; closing = true; timer.Stop(); scanCancellation?.Cancel(); status.Text = "正在退出并保存扫描状态…";
        while (scanning || sampling) await Task.Delay(100);
        tray.Visible = false; tray.Dispose(); trayIcon.Dispose(); Program.Log("桌面正常退出"); System.Windows.Application.Current.Shutdown();
    }
}
