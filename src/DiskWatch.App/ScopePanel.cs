using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskWatch.Core;

namespace DiskWatch.App;

/// <summary>设置页：扫描范围勾选列表、排除目录列表、未保存更改提示与“开始扫描”前的确认。</summary>
public sealed partial class MainWindow
{
    private readonly StackPanel scopeList = new(), exclusionList = new();
    private List<ScopeItem> scopeItems = [];
    private List<DriveEntry> drives = [], presentDrives = [];
    private List<string> exclusionItems = [];
    private bool settingsDirty, populatingSettings;
    private readonly TextBlock dirtyLabel = new() { Text = "● 有未保存的更改", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Visibility = Visibility.Collapsed };

    private static List<DriveEntry> DetectDrives()
    {
        var list = new List<DriveEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                list.Add(new DriveEntry(d.RootDirectory.FullName, d.VolumeLabel, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace, d.DriveType == DriveType.Removable));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    private UIElement ScopeEditor()
    {
        var panel = new StackPanel();
        panel.Children.Add(Copy("勾选要扫描的磁盘或文件夹", 12, Muted));
        var quick = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
        quick.Children.Add(Button("全选磁盘", () => { ScanScope.SelectAllFixed(scopeItems, drives); RenderScope(); MarkDirty(); return Task.CompletedTask; }));
        quick.Children.Add(Button("只扫 C 盘", () => { ScanScope.OnlySystemDrive(scopeItems); RenderScope(); MarkDirty(); return Task.CompletedTask; }));
        quick.Children.Add(Button("添加文件夹…", () => { foreach (var f in PickFolders("选择要扫描的文件夹")) ScanScope.Add(scopeItems, f, Directory.Exists); RenderScope(); MarkDirty(); return Task.CompletedTask; }));
        quick.Children.Add(Button("刷新磁盘列表", () => { var picked = ScanScope.ToRoots(scopeItems); var keepDirty = settingsDirty; drives = DetectDrives(); scopeItems = ScanScope.Merge(drives, picked, Directory.Exists); RenderScope(); if (keepDirty) MarkDirty(); return Task.CompletedTask; }));
        foreach (var b in quick.Children.OfType<FrameworkElement>()) b.Margin = new Thickness(0, 0, 8, 4);
        panel.Children.Add(quick);
        panel.Children.Add(Card(scopeList, new Thickness(12, 6, 12, 6)));
        panel.Children.Add(new TextBlock { Text = "排除目录：这些文件夹不参与扫描，并在覆盖说明中标注", FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 14, 0, 3) });
        var addExclusion = Button("添加排除目录…", () => { foreach (var f in PickFolders("选择不扫描的文件夹")) exclusionItems = ScanScope.NormalizeList(exclusionItems.Append(f)); RenderExclusions(); MarkDirty(); return Task.CompletedTask; });
        addExclusion.HorizontalAlignment = HorizontalAlignment.Left; addExclusion.Margin = new Thickness(0, 2, 0, 6);
        panel.Children.Add(addExclusion);
        panel.Children.Add(Card(exclusionList, new Thickness(12, 6, 12, 6)));
        return panel;
    }

    private IEnumerable<string> PickFolders(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = true };
        return dialog.ShowDialog(this) == true ? dialog.FolderNames : [];
    }

    private void RenderScope()
    {
        scopeList.Children.Clear();
        if (scopeItems.Count == 0) scopeList.Children.Add(Copy("没有检测到可扫描的磁盘。请使用「添加文件夹…」选择范围。", 12, Muted));
        foreach (var item in scopeItems)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            if (item.Removable)
            {
                var remove = Button("移除", () => { scopeItems.Remove(item); RenderScope(); MarkDirty(); return Task.CompletedTask; });
                remove.Padding = new Thickness(10, 3, 10, 3); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            }
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = item.IsDrive ? item.Path : item.Path, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = item.Description, FontSize = 11, Foreground = item.Exists ? Muted : Hex("#B56A22"), TextTrimming = TextTrimming.CharacterEllipsis });
            var box = new CheckBox { IsChecked = item.Checked, Content = text, VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(box, item.Path + " " + item.Description);
            box.Checked += (_, _) => { item.Checked = true; MarkDirty(); };
            box.Unchecked += (_, _) => { item.Checked = false; MarkDirty(); };
            row.Children.Add(box); scopeList.Children.Add(row);
        }
    }

    private void RenderExclusions()
    {
        exclusionList.Children.Clear();
        if (exclusionItems.Count == 0) exclusionList.Children.Add(Copy("暂无排除目录。", 12, Muted));
        foreach (var path in exclusionItems.ToList())
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var remove = Button("移除", () => { exclusionItems.Remove(path); RenderExclusions(); MarkDirty(); return Task.CompletedTask; });
            remove.Padding = new Thickness(10, 3, 10, 3); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            bool exists = Directory.Exists(path);
            row.Children.Add(new TextBlock { Text = exists ? path : path + "（未找到）", FontSize = 12, Foreground = exists ? Ink : Hex("#B56A22"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            exclusionList.Children.Add(row);
        }
    }

    private void LoadScope()
    {
        drives = DetectDrives();
        scopeItems = ScanScope.Merge(drives, settings.Roots, Directory.Exists);
        exclusionItems = ScanScope.NormalizeList(settings.ExcludedPaths);
        RenderScope(); RenderExclusions();
        RefreshScanRangeOptions();
    }

    private void RefreshScanRangeOptions()
    {
        if (scanning) return;
        presentDrives = DetectDrives();
        var options = ScanRangeChoice.Options(presentDrives);
        var keep = ScanRangeChoice.Retain(scanRange.SelectedValue as string, options);
        scanRange.DisplayMemberPath = "Label";
        scanRange.SelectedValuePath = "Key";
        scanRange.ItemsSource = options;
        scanRange.SelectedValue = keep ?? ScanRangeChoice.DefaultKey;
    }

    private void PreferScanRange(string root)
    {
        if (!ScanRangeChoice.CanChange(scanning)) return;
        RefreshScanRangeOptions();
        var key = ScanScope.Normalize(root);
        if (presentDrives.Any(d => ScanScope.Normalize(d.Root).Equals(key, StringComparison.OrdinalIgnoreCase)))
            scanRange.SelectedValue = key;
    }

    private void HookDirty(params Control[] controls)
    {
        foreach (var c in controls)
        {
            if (c is TextBox t) t.TextChanged += (_, _) => MarkDirty();
            else if (c is CheckBox b) { b.Checked += (_, _) => MarkDirty(); b.Unchecked += (_, _) => MarkDirty(); }
        }
    }

    private void MarkDirty()
    {
        if (populatingSettings) return;
        settingsDirty = true; dirtyLabel.Foreground = Hex("#B56A22"); dirtyLabel.Visibility = Visibility.Visible;
    }

    private void ClearDirty() { settingsDirty = false; dirtyLabel.Visibility = Visibility.Collapsed; }

    /// <summary>“开始扫描”：设置页有未保存更改时先询问，避免改了设置却按旧设置扫描。</summary>
    private async Task StartScanClicked()
    {
        if (settingsDirty && !scanning)
        {
            var choice = AskSaveBeforeScan();
            if (choice == 2) { status.Text = "已取消扫描。设置页仍有未保存的更改。"; return; }
            if (choice == 0) { await SaveSettings(); if (settingsDirty) return; }
        }
        RefreshScanRangeOptions();
        var roots = ScanRangeChoice.Resolve(scanRange.SelectedValue as string, settings.Roots, presentDrives);
        if (ScanScope.Validate(roots) is string empty)
        {
            status.Text = empty;
            MessageBox.Show(this, empty, "DiskWatch2", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await ScanRoots(roots);
    }

    /// <summary>0 = 先保存再扫描，1 = 按旧设置扫描，2 = 取消。</summary>
    private int AskSaveBeforeScan()
    {
        int result = 2;
        var window = new Window { Title = "DiskWatch2", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Icon = Icon };
        if (IsVisible) window.Owner = this;
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 18), MaxWidth = 460 };
        panel.Children.Add(Copy("设置中有未保存的更改", 15, Ink));
        panel.Children.Add(Copy("例如扫描范围、排除目录或「NTFS 快速扫描」。若不保存，本次将按原来的设置扫描："
            + string.Join("、", settings.Roots) + "。", 12, Muted));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        System.Windows.Controls.Button Choice(string label, int value, bool primary = false, bool cancel = false)
        {
            var b = new System.Windows.Controls.Button { Content = label, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0), IsDefault = primary, IsCancel = cancel };
            if (primary) { b.Background = Accent; b.Foreground = Brushes.White; }
            b.Click += (_, _) => { result = value; window.Close(); }; return b;
        }
        row.Children.Add(Choice("先保存再扫描", 0, primary: true)); row.Children.Add(Choice("按旧设置扫描", 1)); row.Children.Add(Choice("取消", 2, cancel: true));
        panel.Children.Add(row); window.Content = panel; window.ShowDialog();
        return result;
    }
}