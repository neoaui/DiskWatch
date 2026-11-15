using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DiskWatch.Core;

namespace DiskWatch.App;

/// <summary>
/// “变化明细”视图：对比两次文件级快照，左侧子目录、右侧本目录的文件变化，顶部汇总句和面包屑，双击目录可无限下钻。
/// 只展示，不提供删除按钮。
/// </summary>
public sealed class ChangeDetailsView : DockPanel
{
    public static readonly Brush Ink = Brush("#172B36"), Muted = Brush("#73818C"), Accent = Brush("#087F8C"),
        SignificantBrush = Brush("#D32F2F"), RapidBrush = Brush("#EF6C00"), MinorBrush = Brush("#F9A825"), NewBrush = Brush("#1E66D0"),
        RemovedBrush = Brush("#9AA4AC"), Danger = Brush("#C62828"), DangerSoft = Brush("#FDECEA");
    public const int MaxFileRows = 2000;
    private readonly ChangeNode root;
    private readonly GrowthThresholds thresholds;
    private readonly Func<string, string?> notDeletable;
    private readonly TextBlock summary = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 0, 0, 4) };
    private readonly WrapPanel breadcrumb = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock currentLine = new() { FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly DataGrid directories, files;
    private readonly TextBlock fileNote = new() { FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private ChangeNode current;

    private static Brush Brush(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    public sealed class DirectoryRow
    {
        public required ChangeNode Node { get; init; }
        public string Name { get; init; } = "";
        public string Before { get; init; } = "";
        public string After { get; init; } = "";
        public string Delta { get; init; } = "";
        public string Grade { get; init; } = "";
        public string Counts { get; init; } = "";
        public Brush Foreground { get; init; } = Ink;
        public Brush GradeBrush { get; init; } = Brushes.Transparent;
        public Brush GradeText { get; init; } = Ink;
        public TextDecorationCollection? Decorations { get; init; }
    }
    public sealed class FileChangeRow
    {
        public string Name { get; init; } = "";
        public string Before { get; init; } = "";
        public string After { get; init; } = "";
        public string Delta { get; init; } = "";
        public string State { get; init; } = "";
        public string Lock { get; init; } = "";
        public string Path { get; init; } = "";
        public Brush Foreground { get; init; } = Ink;
        public Brush GradeBrush { get; init; } = Brushes.Transparent;
        public Brush GradeText { get; init; } = Ink;
        public TextDecorationCollection? Decorations { get; init; }
    }

    public ChangeDetailsView(string comparedText, ChangeNode root, GrowthThresholds thresholds, Func<string, string?> notDeletable)
    {
        this.root = root; this.thresholds = thresholds; this.notDeletable = notDeletable; current = root;
        LastChildFill = true;
        var top = new StackPanel();
        summary.Text = ChangeTree.Summarize(root, thresholds).Text;
        top.Children.Add(summary);
        top.Children.Add(new TextBlock { Text = comparedText, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        top.Children.Add(Legend());
        top.Children.Add(breadcrumb);
        top.Children.Add(currentLine);
        SetDock(top, Dock.Top); Children.Add(top);

        directories = Grid(true, ("子目录", "Name", 2.2), ("上次", "Before", 1), ("当前", "After", 1), ("变化量", "Delta", 1), ("分级", "Grade", 0.9), ("新增 / 变更 / 删除", "Counts", 1.3));
        files = Grid(false, ("文件", "Name", 2.4), ("上次", "Before", 1), ("当前", "After", 1), ("变化量", "Delta", 1), ("状态", "State", 0.9), ("提示", "Lock", 1.6));
        directories.MouseDoubleClick += (_, e) => { if (directories.SelectedItem is DirectoryRow row && row.Node.Children.Count + row.Node.Files.Count > 0) { current = row.Node; Render(); } };
        directories.KeyDown += (_, e) => { if (e.Key == Key.Enter && directories.SelectedItem is DirectoryRow row) { current = row.Node; Render(); e.Handled = true; } };
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        var left = Pane("子目录（双击进入下一层）", directories, null);
        var right = Pane("本目录内的文件变化", files, fileNote);
        System.Windows.Controls.Grid.SetColumn(right, 2);
        body.Children.Add(left); body.Children.Add(right);
        Children.Add(body);
        Render();
    }

    private static DockPanel Pane(string title, DataGrid grid, UIElement? footer)
    {
        var p = new DockPanel();
        var head = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 4) };
        SetDock(head, Dock.Top); p.Children.Add(head);
        if (footer != null) { SetDock(footer, Dock.Bottom); p.Children.Add(footer); }
        p.Children.Add(grid); return p;
    }

    private UIElement Legend()
    {
        var w = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Chip(string text, Brush fill, Brush fore, bool strike = false)
        {
            var t = new TextBlock { Text = text, FontSize = 11, Foreground = fore };
            if (strike) t.TextDecorations = TextDecorations.Strikethrough;
            w.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 6, 2), Child = t });
        }
        Chip($"显著增长 > {Labels.SmartSize(thresholds.SignificantBytes)}", SignificantBrush, Brushes.White);
        Chip($"快速增长 > {Labels.SmartSize(thresholds.RapidBytes)} 且 > {thresholds.RapidPercent:0.#}%", RapidBrush, Brushes.White);
        Chip("轻微增长", MinorBrush, Ink);
        Chip("新增（蓝色）", Brushes.Transparent, NewBrush);
        Chip("已删除（灰色删除线）", Brushes.Transparent, RemovedBrush, true);
        Chip("不可删除（红色）", DangerSoft, Danger);
        return w;
    }

    private static DataGrid Grid(bool directoryGrid, params (string Header, string Path, double Width)[] columns)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Brush("#EEF2F5"), Background = Brushes.White, RowBackground = Brushes.White, AlternatingRowBackground = Brushes.White, BorderBrush = Brush("#E5EBEF"),
            SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false, FontSize = 12, RowHeight = 24, MinHeight = 160
        };
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        foreach (var (header, path, width) in columns)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 0, 4, 0)));
            if (path is "Grade" or "State")
            {
                style.Setters.Add(new Setter(TextBlock.BackgroundProperty, new Binding("GradeBrush")));
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding("GradeText")));
                style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 1, 6, 1)));
                style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left));
            }
            else if (path == "Lock") style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Danger));
            else
            {
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding("Foreground")));
                style.Setters.Add(new Setter(TextBlock.TextDecorationsProperty, new Binding("Decorations")));
            }
            if (path is "Before" or "After" or "Delta") style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = new DataGridLength(width, DataGridLengthUnitType.Star), ElementStyle = style });
        }
        return grid;
    }

    private (Brush Fill, Brush Text) GradeColors(GrowthGrade g) => g switch
    {
        GrowthGrade.Significant => (SignificantBrush, Brushes.White),
        GrowthGrade.Rapid => (RapidBrush, Brushes.White),
        GrowthGrade.Minor => (MinorBrush, Ink),
        GrowthGrade.New => (Brushes.Transparent, NewBrush),
        GrowthGrade.Removed => (Brushes.Transparent, RemovedBrush),
        _ => (Brushes.Transparent, Muted)
    };

    public ChangeNode Current => current;
    public void Navigate(ChangeNode node) { current = node; Render(); }

    private void Render()
    {
        breadcrumb.Children.Clear();
        var chain = new List<ChangeNode>();
        for (var n = current; n != null; n = n.Parent) chain.Insert(0, n);
        for (int i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            if (i > 0) breadcrumb.Children.Add(new TextBlock { Text = " › ", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = node.Name, Padding = new Thickness(6, 2, 6, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = node == current ? Ink : Accent, FontWeight = node == current ? FontWeights.SemiBold : FontWeights.Normal, Cursor = Cursors.Hand };
            b.Click += (_, _) => { current = node; Render(); };
            breadcrumb.Children.Add(b);
        }
        var grade = Growth.Grade(current.BeforeBytes, current.AfterBytes, thresholds);
        currentLine.Text = $"{current.Path}，上次 {Labels.SmartSize(current.BeforeBytes)}，当前 {Labels.SmartSize(current.AfterBytes)}，变化 {Labels.SignedSize(current.Delta)}（{Growth.Name(grade)}），新增 {current.Added:N0}，变更 {current.Changed:N0}，删除 {current.Removed:N0}";
        directories.ItemsSource = current.Children.Where(c => c.Delta != 0 || c.Added + c.Removed + c.Changed > 0).Select(DirectoryRowOf).ToList();
        var shown = current.Files.Take(MaxFileRows).Select(FileRowOf).ToList();
        files.ItemsSource = shown;
        fileNote.Text = current.Files.Count == 0 ? "本目录内没有直接的文件变化，请查看左侧子目录。" :
            current.Files.Count > MaxFileRows ? $"共 {current.Files.Count:N0} 个文件变化，按变化量显示前 {MaxFileRows:N0} 个。" : $"共 {current.Files.Count:N0} 个文件变化，按变化量从大到小。";
    }

    private DirectoryRow DirectoryRowOf(ChangeNode n)
    {
        bool isNew = !n.ExistedBefore, gone = !n.ExistsAfter;
        var g = isNew ? GrowthGrade.New : gone ? GrowthGrade.Removed : Growth.Grade(n.BeforeBytes, n.AfterBytes, thresholds);
        var (fill, text) = GradeColors(g);
        return new DirectoryRow
        {
            Node = n, Name = n.Name + (n.Children.Count > 0 ? "  ›" : ""), Before = isNew ? "—" : Labels.SmartSize(n.BeforeBytes), After = gone ? "—" : Labels.SmartSize(n.AfterBytes),
            Delta = Labels.SignedSize(n.Delta), Grade = Growth.Name(g), Counts = $"{n.Added:N0} / {n.Changed:N0} / {n.Removed:N0}",
            Foreground = isNew ? NewBrush : gone ? RemovedBrush : Ink, Decorations = gone ? TextDecorations.Strikethrough : null, GradeBrush = fill, GradeText = text
        };
    }

    private FileChangeRow FileRowOf(FileChange c)
    {
        var g = Growth.Grade(c, thresholds);
        var (fill, text) = GradeColors(g);
        var reason = c.Kind == ChangeKind.Removed ? null : notDeletable(c.Path);
        return new FileChangeRow
        {
            Name = c.Name, Path = c.Path,
            Before = c.Kind == ChangeKind.Added ? "—" : Labels.SmartSize(c.BeforeBytes), After = c.Kind == ChangeKind.Removed ? "—" : Labels.SmartSize(c.AfterBytes),
            Delta = Labels.SignedSize(c.Delta), State = c.Kind == ChangeKind.Changed ? "变更 · " + Growth.Name(g) : Growth.Name(g),
            Lock = reason == null ? "" : "不可删除：" + reason,
            Foreground = reason != null && c.Kind == ChangeKind.Changed ? Danger : c.Kind == ChangeKind.Added ? NewBrush : c.Kind == ChangeKind.Removed ? RemovedBrush : Ink,
            Decorations = c.Kind == ChangeKind.Removed ? TextDecorations.Strikethrough : null, GradeBrush = fill, GradeText = text
        };
    }
}