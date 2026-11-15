using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DiskWatch.Core;
using DiskWatch.Storage;
using DiskWatch.Windows;

namespace DiskWatch.App;

/// <summary>“变化明细”：保存文件级明细、总览里的折叠栏、单独窗口、设置页的“变化分级”。</summary>
public sealed partial class MainWindow
{
    private readonly TextBox growthSignificantInput = new() { Width = 100 }, growthRapidInput = new() { Width = 100 }, growthPercentInput = new() { Width = 100 }, detailRetentionInput = new() { Width = 100 };
    private Classifier? changeClassifier;

    /// <summary>扫描保存后写入文件级明细；失败只记日志，不影响快照本身。</summary>
    private async Task SaveFileDetails(Snapshot snapshot)
    {
        var details = snapshot.FileDetails; snapshot.FileDetails = null;
        if (details == null || !ScanCoverage.Verified(snapshot)) return;
        try
        {
            var bytes = await Task.Run(() => repository.SaveFileIndex(snapshot, details));
            if (bytes > 0) Program.Log($"文件明细已保存：{snapshot.Root}，{details.Files.Count:N0} 个文件，{details.Directories.Count:N0} 个目录，{Labels.SmartSize(bytes)}");
        }
        catch (Exception ex) { Program.Log("文件明细保存失败（扫描结果已保存）：" + ex.Message); }
    }

    public sealed record ChangeComparison(ChangeNode Tree, string Text);

    public static string MethodName(string ruleVersion) => ruleVersion.EndsWith("+mft", StringComparison.Ordinal) ? "NTFS 快速扫描" : "目录遍历";

    private async Task<ChangeComparison?> BuildChangeComparison(string root)
    {
        return await Task.Run(() =>
        {
            var pair = repository.ComparablePair(root);
            if (pair == null) return null;
            var (b, a) = pair.Value;
            var before = repository.LoadFileIndex(b.SnapshotId); var after = repository.LoadFileIndex(a.SnapshotId);
            if (before == null || after == null) return null;
            var changes = FileDiff.Compare(before, after);
            var tree = ChangeTree.Build(string.IsNullOrEmpty(after.Root) ? root : after.Root, before, after, changes);
            changeClassifier ??= new Classifier(ApplicationDiscovery.Discover(settings));
            return new ChangeComparison(tree, $"对比：上次 {b.Finished.ToLocalTime():yyyy-MM-dd HH:mm}（{b.Files:N0} 个文件）→ 当前 {a.Finished.ToLocalTime():yyyy-MM-dd HH:mm}（{a.Files:N0} 个文件）。大小按磁盘占用计算。");
        });
    }

    private string? NotDeletable(string path)
    {
        var c = changeClassifier; if (c == null) return null;
        try { return Cleanup.NotDeletableReason(path, c.Classify(path).Category); } catch (ArgumentException) { return null; }
    }

    private Expander ChangeDetailsFold(Snapshot s)
    {
        string hint;
        try
        {
            var list = repository.FileIndexes(s.Root);
            var pair = repository.ComparablePair(s.Root);
            hint = pair is { } p ? $"对比 {p.Before.Finished.ToLocalTime():MM-dd HH:mm} 与 {p.After.Finished.ToLocalTime():MM-dd HH:mm} 两次扫描的文件新增、变更和删除"
                : settings.FileDetailRetention == 0 ? "已在设置中关闭文件明细"
                : list.Count == 0 ? "将从下一次完整扫描开始记录文件明细。再扫描一次后即可对比。"
                : "还没有可对比的两次完整扫描。读取失败的文件不会被当作已删除。";
        }
        catch (Exception ex) { hint = "读取明细失败：" + ex.Message; }
        var host = new StackPanel();
        host.Children.Add(Copy("对比两次扫描的文件变化。蓝色为新增，灰色删除线为已删除；目录按增长幅度分为红色显著增长、橙色快速增长、黄色轻微增长。", 11, Muted));
        var fold = Fold("变化明细", hint, host);
        bool built = false;
        fold.Expanded += async (_, args) =>
        {
            if (args.Source != fold || built) return; built = true;
            var waiting = Copy("正在对比两次扫描的文件变化…", 12, Muted); host.Children.Add(waiting);
            try
            {
                var result = await BuildChangeComparison(s.Root);
                host.Children.Remove(waiting);
                if (result == null) { host.Children.Add(Copy(hint, 12, Muted)); return; }
                var view = new ChangeDetailsView(result.Text, result.Tree, GrowthThresholds.From(settings), NotDeletable) { Height = 620, Margin = new Thickness(0, 6, 0, 0) };
                host.Children.Add(Button("在新窗口打开变化明细", () => { OpenChangeWindow(result, view.Current.Path); return Task.CompletedTask; }));
                host.Children.Add(view);
            }
            catch (Exception ex) { host.Children.Remove(waiting); host.Children.Add(Copy("对比失败：" + ex.Message, 12, Danger)); built = false; }
        };
        return fold;
    }

    private void OpenChangeWindow(ChangeComparison result, string? startPath = null)
    {
        var view = new ChangeDetailsView(result.Text, result.Tree, GrowthThresholds.From(settings), NotDeletable) { Margin = new Thickness(18) };
        if (startPath != null && ChangeTree.Find(result.Tree, startPath) is { } node) view.Navigate(node);
        var window = new Window { Title = "变化明细 · DiskWatch2", Owner = this, Width = 1180, Height = 760, Content = view, Background = System.Windows.Media.Brushes.White, Icon = Icon, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Show();
    }

    private UIElement GrowthSettingsCard()
    {
        var p = new StackPanel();
        p.Children.Add(Copy("目录和文件按增长幅度分级，阈值为严格大于。", 12, Muted));
        p.Children.Add(Copy("显著增长（红）：增长超过多少 MiB", 12, Muted)); p.Children.Add(growthSignificantInput);
        p.Children.Add(Copy("快速增长（橙）：增长超过多少 MiB", 12, Muted)); p.Children.Add(growthRapidInput);
        p.Children.Add(Copy("快速增长还需增长率超过多少 %（新建目录视为超过该比例）", 12, Muted)); p.Children.Add(growthPercentInput);
        p.Children.Add(Copy("每个扫描范围保留几份文件明细（0 表示不记录；此前扫描仍保留目录统计）", 12, Muted)); p.Children.Add(detailRetentionInput);
        HookDirty(growthSignificantInput, growthRapidInput, growthPercentInput, detailRetentionInput);
        var card = Card(Fold("变化分级", "增长分级阈值与明细保留份数", p)); card.Margin = new Thickness(0, 12, 0, 0); return card;
    }

    private void PopulateGrowth()
    {
        growthSignificantInput.Text = settings.GrowthSignificantMiB.ToString(CultureInfo.InvariantCulture);
        growthRapidInput.Text = settings.GrowthRapidMiB.ToString(CultureInfo.InvariantCulture);
        growthPercentInput.Text = settings.GrowthRapidPercent.ToString(CultureInfo.InvariantCulture);
        detailRetentionInput.Text = settings.FileDetailRetention.ToString(CultureInfo.InvariantCulture);
    }

    private void ReadGrowth(Settings updated)
    {
        static double Number(TextBox box, string name) => double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : throw new ArgumentException(name + "应为数字。");
        updated.GrowthSignificantMiB = Number(growthSignificantInput, "显著增长阈值");
        updated.GrowthRapidMiB = Number(growthRapidInput, "快速增长阈值");
        updated.GrowthRapidPercent = Number(growthPercentInput, "快速增长百分比");
        updated.FileDetailRetention = int.TryParse(detailRetentionInput.Text.Trim(), out var n) ? n : throw new ArgumentException("明细保留份数应为整数。");
    }

    private void ApplyGrowth(Settings updated)
    {
        settings.GrowthSignificantMiB = updated.GrowthSignificantMiB; settings.GrowthRapidMiB = updated.GrowthRapidMiB;
        settings.GrowthRapidPercent = updated.GrowthRapidPercent; settings.FileDetailRetention = updated.FileDetailRetention;
    }
}