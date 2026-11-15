using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using DiskWatch.Core;

namespace DiskWatch.App;

public sealed partial class MainWindow
{
    private static readonly Brush Ink = Hex("#172B36"), Muted = Hex("#73818C"), Accent = Hex("#087F8C"), Line = Hex("#E5EBEF");
    private readonly TextBlock pageTitle = new(), pageHint = new();
    private readonly UniformGrid driveCards = new() { Columns=4, Margin=new Thickness(-5,12,-5,16) };
    private readonly StackPanel diskDetails = new();
    private readonly StackPanel summaryBody = new();
    private readonly List<(Button Button,TabItem Tab)> navigation = new();
    private readonly List<VolumeSample> latestVolumes = new();
    private readonly TabItem reportTab = new(), preferencesTab = new();
    private Button scanAction = null!, cancelAction = null!;
    private readonly ComboBox scanRange = new() { MinWidth = 128, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
    private static Brush Hex(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static TextBlock Copy(string text,double size=13,Brush? color=null) => new() { Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3) };
    private static Border Card(UIElement child,Thickness? padding=null) => new() { Background=Brushes.White,CornerRadius=new CornerRadius(12),BorderBrush=Line,BorderThickness=new Thickness(1),Padding=padding??new Thickness(20),Child=child };

    private void BuildModernShell()
    {
        Background=Hex("#F6F8FA");Foreground=Ink;FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");FontSize=13;
        Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#344954"/><Setter Property="BorderBrush" Value="#E0E7EB"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Padding" Value="14,9"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.78"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#087F8C"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="Padding" Value="10,7"/><Setter Property="MinHeight" Value="36"/><Setter Property="VerticalContentAlignment" Value="Center"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="White" BorderBrush="#DCE5E9" BorderThickness="1" CornerRadius="6"/></ControlTemplate></ToggleButton.Template></ToggleButton><Grid IsHitTestVisible="False" Margin="10,7"><ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" VerticalAlignment="Center" Margin="0,0,20,0"/><TextBlock Text="⌄" HorizontalAlignment="Right" Foreground="#73818C"/></Grid><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="White" BorderBrush="#DCE5E9" BorderThickness="1" CornerRadius="6" Padding="4" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="280"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></Style.Triggers>
          </Style>
          <Style TargetType="ComboBoxItem"><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="item" Background="White" CornerRadius="4" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="item" Property="Background" Value="#EAF5F5"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="#087F8C"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="TextBox"><Setter Property="Padding" Value="8"/><Setter Property="BorderBrush" Value="#DCE4E8"/></Style>
          <Style TargetType="DataGrid"><Setter Property="BorderThickness" Value="0"/><Setter Property="Background" Value="White"/><Setter Property="HorizontalGridLinesBrush" Value="#EEF2F5"/></Style>
          <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#F5F8FA"/><Setter Property="Foreground" Value="#73818C"/><Setter Property="Padding" Value="10"/><Setter Property="BorderThickness" Value="0"/><Setter Property="FontWeight" Value="Normal"/></Style>
          <Style TargetType="Expander"><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Margin" Value="0,4"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Expander"><StackPanel><ToggleButton x:Name="toggle" IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}}" Background="Transparent" BorderThickness="0" HorizontalContentAlignment="Stretch"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="head" Background="Transparent" Padding="4,12"><DockPanel><TextBlock x:Name="arrow" Text="›" FontSize="20" Foreground="#73818C" Margin="0,0,12,0" VerticalAlignment="Center"/><ContentPresenter Content="{TemplateBinding Content}" VerticalAlignment="Center"/></DockPanel></Border><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="arrow" Property="Text" Value="⌄"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="head" Property="Background" Value="#F4F8F9"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="head" Property="Background" Value="#EAF5F5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template><ContentPresenter ContentSource="Header"/></ToggleButton><ContentPresenter x:Name="body" ContentSource="Content" Visibility="Collapsed" Margin="26,0,0,12"/></StackPanel><ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="body" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        </ResourceDictionary>
        """));
        var shell=new System.Windows.Controls.Grid{Background=Background};shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(182)});shell.ColumnDefinitions.Add(new ColumnDefinition());Content=shell;
        var side=new DockPanel{Margin=new Thickness(18,26,18,18)};
        var brand=new StackPanel{Margin=new Thickness(12,0,0,36)};brand.Children.Add(BrandRow());brand.Children.Add(Copy("磁盘空间观察",11,Muted));DockPanel.SetDock(brand,Dock.Top);side.Children.Add(brand);
        var foot=new StackPanel();foot.Children.Add(Button("?  名词说明",()=>{Navigate(glossaryTab,"名词说明","栏目含义与统计说明");return Task.CompletedTask;}));foot.Children.Add(Copy("●  本地存储",11,Muted));foot.Children.Add(Copy("每分钟记录空间变化",11,Muted));DockPanel.SetDock(foot,Dock.Bottom);side.Children.Add(foot);
        var nav=new StackPanel();side.Children.Add(nav);
        void Nav(string caption,TabItem tab,string title,string hint){var b=Button(caption,()=>{Navigate(tab,title,hint);return Task.CompletedTask;});b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,4,0,4);b.Padding=new Thickness(16,12,12,12);b.BorderThickness=new Thickness(0);nav.Children.Add(b);navigation.Add((b,tab));}
        Nav("◫   空间总览",summaryTab,"空间总览","选择磁盘，查看空间占用。");Nav("⌁   变化趋势",trendTab,"变化趋势","查看空间随时间的变化。");Nav("▤   历史报表",reportTab,"历史报表","按日、周、月查看空间变化。");Nav("⚙   设置",preferencesTab,"偏好设置","扫描、提醒与分类。");
        var sideBorder=new Border{Background=Hex("#EEF3F5"),Child=side};shell.Children.Add(sideBorder);
        var main=new DockPanel{Margin=new Thickness(30,26,30,12)};System.Windows.Controls.Grid.SetColumn(main,1);shell.Children.Add(main);
        var top=new DockPanel{Margin=new Thickness(0,0,0,20)};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);
        cancelAction=Button("取消扫描",()=>{scanCancellation?.Cancel();return Task.CompletedTask;});cancelAction.Visibility=Visibility.Collapsed;actions.Children.Add(cancelAction);
        scanAction=Button("开始扫描",StartScanClicked);scanAction.Background=Accent;scanAction.Foreground=Brushes.White;scanAction.BorderThickness=new Thickness(0);actions.Children.Add(scanAction);
        scanRange.ToolTip="选择本次扫描范围，点击「开始扫描」后开始。不会更改设置中保存的范围。";
        System.Windows.Automation.AutomationProperties.SetName(scanRange,"扫描范围");
        scanRange.DisplayMemberPath="Label";scanRange.SelectedValuePath="Key";
        actions.Children.Add(scanRange);
        var more=Button("···",()=>Task.CompletedTask);more.ToolTip="更多操作";var menu=new ContextMenu();
        void Item(string label,Func<Task> action){var i=new MenuItem{Header=label};i.Click+=async(_,_)=>await Safe(action);menu.Items.Add(i);}
        Item("选择文件夹扫描…",ChooseFolder);Item("刷新容量",Sample);Item("暂停 / 恢复监测",()=>{TogglePause();return Task.CompletedTask;});menu.Items.Add(new Separator());Item("保存完整总结",SaveSummary);Item("退出程序",Exit);
        more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};actions.Children.Add(more);
        var heading=new StackPanel();pageTitle.FontSize=27;pageTitle.FontWeight=FontWeights.SemiBold;pageHint.FontSize=12;pageHint.Foreground=Muted;pageHint.Margin=new Thickness(0,7,0,0);heading.Children.Add(pageTitle);heading.Children.Add(pageHint);top.Children.Add(heading);DockPanel.SetDock(top,Dock.Top);main.Children.Add(top);
        DockPanel.SetDock(scanBanner,Dock.Top);main.Children.Add(BuildScanBanner());
        status.Margin=new Thickness(0,12,0,0);status.FontSize=11;status.Foreground=Muted;status.MaxHeight=36;status.TextTrimming=TextTrimming.CharacterEllipsis;DockPanel.SetDock(status,Dock.Bottom);main.Children.Add(status);
        tabs.BorderThickness=new Thickness(0);tabs.Background=Brushes.Transparent;tabs.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TabControl'><ContentPresenter ContentSource='SelectedContent'/></ControlTemplate>");main.Children.Add(tabs);
        AddSummaryAndTrendTabs();
        var reportPanel=new StackPanel();reportPanel.Children.Add(WeeklyCard());var reportTools=new WrapPanel();reportTools.Children.Add(period);reportTools.Children.Add(Button("生成报表",BuildReports));reportTools.Children.Add(Button("导出 CSV",ExportReport));reportPanel.Children.Add(reportTools);reports.Height=320;reportPanel.Children.Add(Card(reports));alerts.Height=230;reportPanel.Children.Add(Fold("提醒历史","查看低空间和骤降记录",alerts));reportTab.Content=new ScrollViewer{Content=reportPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};tabs.Items.Add(reportTab);
        preferencesTab.Content=SettingsPanel();tabs.Items.Add(preferencesTab);
        glossaryTab.Content=GlossaryPanel();tabs.Items.Add(glossaryTab);
        Navigate(summaryTab,"空间总览","选择磁盘，查看空间占用。");
    }
    private void Navigate(TabItem tab,string title,string hint)
    {
        tabs.SelectedItem=tab;pageTitle.Text=title;pageHint.Text=hint;
        foreach(var pair in navigation){pair.Button.Background=pair.Tab==tab?Brushes.White:Brushes.Transparent;pair.Button.Foreground=pair.Tab==tab?Accent:Muted;pair.Button.FontWeight=pair.Tab==tab?FontWeights.SemiBold:FontWeights.Normal;}
    }
    private static Expander Fold(string title,string hint,UIElement content)
    {var h=new StackPanel();h.Children.Add(Copy(title,15));if(hint.Length>0)h.Children.Add(Copy(hint,11,Muted));return new Expander{Header=h,Content=content};}
    private void UpdateDriveCards(IEnumerable<VolumeSample> samples)
    {
        latestVolumes.Clear();latestVolumes.AddRange(samples);driveCards.Children.Clear();
        driveCards.Columns=Math.Min(4,Math.Max(1,latestVolumes.Count));
        foreach(var s in latestVolumes)
        {
            bool chosen=(selectedRoot.SelectedItem as string)==s.Root;bool low=s.FreeBytes<=settings.ThresholdsGiB.Max()*1073741824;
            var content=new StackPanel();var label=new DockPanel();var state=Copy(low?"空间偏低":"可用",10,low?Hex("#B56A22"):Muted);DockPanel.SetDock(state,Dock.Right);label.Children.Add(state);label.Children.Add(Copy(s.Root,17,chosen?Accent:Ink));content.Children.Add(label);
            content.Children.Add(Copy($"{s.FreeBytes/1073741824d:N1} GiB",23));
            content.Children.Add(new ProgressBar{Value=100d*s.UsedBytes/Math.Max(1,s.TotalBytes),Maximum=100,Height=4,Margin=new Thickness(0,8,0,6),Foreground=low?Hex("#CF9252"):Accent,Background=Hex("#EAF0F3"),BorderThickness=new Thickness(0)});
            content.Children.Add(Copy($"剩余 / 共 {s.TotalBytes/1073741824d:N0} GiB",11,Muted));if(ForecastLine(s.Root) is UIElement forecastLine)content.Children.Add(forecastLine);
            var b=Button("",async()=>{if(!selectedRoot.Items.Contains(s.Root))selectedRoot.Items.Add(s.Root);selectedRoot.SelectedItem=s.Root;PreferScanRange(s.Root);UpdateDriveCards(latestVolumes.ToList());await LoadSnapshot();});b.Content=content;b.Margin=new Thickness(5,0,5,0);b.Padding=new Thickness(14);b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.BorderBrush=chosen?Accent:Line;b.BorderThickness=new Thickness(chosen?2:1);driveCards.Children.Add(b);
        }
    }
    private void RenderEmptyDisk(string root){diskDetails.Children.Clear();diskDetails.Children.Add(Card(Copy(root+" 还没有扫描结果。点击右上角「开始扫描」。",15)));}
    private void RenderDiskDetails(Snapshot s,Snapshot? previous)
    {
        foreach(var control in new FrameworkElement[]{snapshotNote,categories,changes}) if(control.Parent is Panel old)old.Children.Remove(control);
        diskDetails.Children.Clear();
        var top=new StackPanel();var titleRow=new DockPanel();
        var trendLink=Button("查看趋势  →",async()=>{trendRoot.SelectedItem=s.Root;Navigate(trendTab,"变化趋势",s.Root+" 的空间变化记录");await RefreshTrend();});DockPanel.SetDock(trendLink,Dock.Right);titleRow.Children.Add(trendLink);
        var caption=new StackPanel();caption.Children.Add(Copy(s.Root+"，最近一次扫描",18));caption.Children.Add(Copy($"{s.Finished.ToLocalTime():MM月dd日 HH:mm}，{s.Files:N0} 个文件，{(s.Finished-s.Started).TotalMinutes:N1} 分钟",11,Muted));titleRow.Children.Add(caption);top.Children.Add(titleRow);
        var change=s.IsWholeVolume&&s.After!=null&&previous?.After!=null&&previous.VolumeId==s.VolumeId?$"较上次扫描，已用空间 {(s.After.UsedBytes-previous.After.UsedBytes)/1073741824d:+0.00;-0.00;0.00} GiB":"扫描结果已保存，可从下方分类查看。";
        top.Children.Add(Copy(change,15));
        if(s.IsWholeVolume&&ForecastDetail(s.Root) is UIElement forecastDetail)top.Children.Add(forecastDetail);
        if(s.Status!="Complete")top.Children.Add(Copy(s.Coverage,12,Hex("#B56A22")));
        diskDetails.Children.Add(Card(top,new Thickness(18,14,18,14)));
        var detail=new StackPanel();
        var categoryPanel=new StackPanel();categoryPanel.Children.Add(Copy("展开分类查看目录，再展开查看已记录的大文件。",11,Muted));
        var folds=new List<Expander>();
        foreach(var group in s.Buckets.GroupBy(b=>b.Category).OrderByDescending(g=>g.Sum(b=>b.AllocatedBytes)))
        {
            long total=group.Sum(b=>b.AllocatedBytes);if(total==0)continue;
            var dirs=new StackPanel();bool populated=false;
            var fold=Fold(Labels.CategoryName(group.Key),Labels.Size(total),dirs);
            fold.Expanded+=(_,args)=>
            {
                if(args.Source!=fold)return;
                foreach(var other in folds)if(other!=fold)other.IsExpanded=false;
                if(populated)return;populated=true;
                var buckets=group.OrderByDescending(b=>b.AllocatedBytes).ToList();int offset=0;
                Button? moreDirs=null;
                void Fill()
                {
                    if(moreDirs!=null)dirs.Children.Remove(moreDirs);
                    foreach(var bucket in buckets.Skip(offset).Take(20))
                    {
                        var files=new StackPanel();var dir=Fold(bucket.Path,Labels.Size(bucket.AllocatedBytes),files);bool filesBuilt=false;
                        dir.Expanded+=(_,eventArgs)=>
                        {
                            if(eventArgs.Source!=dir||filesBuilt)return;filesBuilt=true;
                            files.Children.Add(Copy("该路径下已记录的大文件（不是完整文件列表）",11,Muted));
                            var found=s.LargestFiles.Where(f=>f.Category==bucket.Category&&f.Path.StartsWith(bucket.Path.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)).OrderByDescending(f=>f.AllocatedBytes).Take(8).ToList();
                            foreach(var f in found)files.Children.Add(LargeFileLine(f));
                            if(found.Count==0)files.Children.Add(Copy("此目录没有达到记录条件的大文件。",12,Muted));
                        };
                        dirs.Children.Add(dir);
                    }
                    offset+=20;if(offset<buckets.Count){moreDirs=Button($"再显示 20 个目录（剩余 {buckets.Count-offset}）",()=>{Fill();return Task.CompletedTask;});dirs.Children.Add(moreDirs);}
                }
                Fill();
            };
            folds.Add(fold);categoryPanel.Children.Add(fold);
        }
        detail.Children.Add(Fold("占用分类",$"{s.Buckets.Select(b=>b.Category).Distinct().Count()} 个分类，按占用从大到小",categoryPanel));
        detail.Children.Add(AppRankingFold(s));
        detail.Children.Add(Fold("占用地图","按面积查看空间占用，点击文件夹进入下一层",OccupancyMapPanel(s)));
        var large=new StackPanel();var topLarge=s.LargestFiles.OrderByDescending(f=>f.AllocatedBytes).Take(20).ToList();
        int lockedCount=topLarge.Count(f=>!Cleanup.IsDeletable(f.Path,f.Category));
        if(lockedCount>0)large.Children.Add(Copy("红色标记为「不可删除」，表示系统或已安装软件需要的文件。",11,Danger));
        foreach(var f in topLarge)large.Children.Add(LargeFileLine(f));if(s.LargestFiles.Count==0)large.Children.Add(Copy("暂无大文件记录。",12,Muted));
        detail.Children.Add(Fold("大文件",lockedCount>0?$"占用最大的前 20 项，其中 {lockedCount} 项不可删除":"占用最大的前 20 项",large));
        detail.Children.Add(ChangeDetailsFold(s));
        var coverage=new StackPanel();coverage.Children.Add(Copy(s.Coverage,12,Muted));coverage.Children.Add(Copy("扫描方式："+(string.IsNullOrEmpty(s.ScanMethod)?"目录遍历":s.ScanMethod),12,Muted));coverage.Children.Add(Copy("当前权限："+Labels.ElevationStatus(elevated,settings.FastScanEnabled(elevated)),12,elevated?Accent:Muted));coverage.Children.Add(Copy(s.Residual is long residual?$"未解释差额 {Labels.Size(residual)}。可能包含未覆盖区域、文件系统开销和扫描时差，并不等于可清理空间。":"当前为文件夹扫描，不计算整盘未解释差额。",12,Muted));foreach(var item in s.Issues)coverage.Children.Add(FileLine(item.Path,0,item.Reason));
        detail.Children.Add(Fold("扫描覆盖",s.Errors>0?$"{s.Errors} 项读取失败，已保存的数据不受影响":"查看统计说明与覆盖范围",coverage));
        var advanced=new StackPanel();snapshotNote.FontSize=11;advanced.Children.Add(snapshotNote);categories.Height=300;advanced.Children.Add(categories);changes.Height=260;advanced.Children.Add(changes);advanced.Children.Add(Button("导出分类 CSV",ExportSnapshot));
        var plan=Cleanup.Plan(s);
        detail.Children.Add(Fold("瘦身建议",plan.Move.Count+plan.Delete.Count>0?$"可转移 {Labels.Size(plan.MoveBytes)}，可删除 {Labels.Size(plan.DeleteBytes)}，仅供参考":"暂时没有可转移或可删除的项目",SlimPanel(s,plan)));
        detail.Children.Add(Fold("统计明细","分类、目录变化与导出",advanced));
        foreach(var section in detail.Children.OfType<Expander>())section.Expanded+=(_,args)=>{if(args.Source==section)foreach(var other in detail.Children.OfType<Expander>())if(other!=section)other.IsExpanded=false;};
        var detailCard=Card(detail,new Thickness(16,8,16,8));detailCard.Margin=new Thickness(0,12,0,0);diskDetails.Children.Add(detailCard);
        if(latestVolumes.Count>0)UpdateDriveCards(latestVolumes.ToList());
    }
    /// <summary>侧栏标题：与程序图标一致的简化哨塔（尖顶塔楼、一排窗、两条塔腿加一道交叉支撑），矢量绘制，小尺寸也清晰。</summary>
    private static UIElement BrandRow()
    {
        var tower=new Canvas{Width=24,Height=24};
        tower.Children.Add(new System.Windows.Shapes.Path{Fill=Accent,Data=Geometry.Parse("F0 M3.5,9 L12,2.5 L20.5,9 Z M5.5,9.8 H18.5 V14.6 H5.5 Z M7.2,11 H9.6 V13.2 H7.2 Z M10.8,11 H13.2 V13.2 H10.8 Z M14.4,11 H16.8 V13.2 H14.4 Z")});
        tower.Children.Add(new System.Windows.Shapes.Path{Stroke=Accent,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Data=Geometry.Parse("M8.2,14.6 L6.2,22.2 M15.8,14.6 L17.8,22.2 M7.7,16.8 L16.9,21 M16.3,16.8 L7.1,21")});
        var mark=new Viewbox{Width=28,Height=28,Child=tower,Margin=new Thickness(0,0,6,2),VerticalAlignment=VerticalAlignment.Center,SnapsToDevicePixels=true};
        System.Windows.Automation.AutomationProperties.SetName(mark,"程序图标");
        var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(mark);
        var title=Copy("DiskWatch",21,Accent);title.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(title);
        return row;
    }
    // ---- 扫描进度横幅：扫描时显示在页面标题下方，所有页面可见；没在扫描时收起 ----
    private readonly Border scanBanner=new(){Visibility=Visibility.Collapsed};
    private readonly WatchtowerProgress scanBar=new(){Margin=new Thickness(0,6,0,8)};
    private readonly TextBlock scanTitle=new(),scanPercent=new(),scanDetail=new(),scanDirectory=new();
    private int scanBannerVersion;
    private UIElement BuildScanBanner()
    {
        scanBanner.Background=Hex("#E7F4F5");scanBanner.BorderBrush=Accent;scanBanner.BorderThickness=new Thickness(1.5);scanBanner.CornerRadius=new CornerRadius(10);
        scanBanner.Padding=new Thickness(16,12,16,12);scanBanner.Margin=new Thickness(0,0,0,16);
        System.Windows.Automation.AutomationProperties.SetName(scanBanner,"扫描进度");
        var dock=new DockPanel();
        var cancel=Button("取消扫描",()=>{scanCancellation?.Cancel();scanTitle.Text="正在取消…";return Task.CompletedTask;});
        cancel.VerticalAlignment=VerticalAlignment.Center;cancel.Margin=new Thickness(16,0,0,0);DockPanel.SetDock(cancel,Dock.Right);dock.Children.Add(cancel);
        var body=new StackPanel();
        var head=new DockPanel();
        scanPercent.FontSize=22;scanPercent.FontWeight=FontWeights.SemiBold;scanPercent.Foreground=Accent;DockPanel.SetDock(scanPercent,Dock.Right);head.Children.Add(scanPercent);
        scanTitle.FontSize=15;scanTitle.FontWeight=FontWeights.SemiBold;scanTitle.Foreground=Ink;scanTitle.VerticalAlignment=VerticalAlignment.Center;scanTitle.TextTrimming=TextTrimming.CharacterEllipsis;head.Children.Add(scanTitle);
        body.Children.Add(head);
        body.Children.Add(scanBar);
        scanDetail.FontSize=12;scanDetail.Foreground=Ink;scanDetail.TextTrimming=TextTrimming.CharacterEllipsis;body.Children.Add(scanDetail);
        scanDirectory.FontSize=11;scanDirectory.Foreground=Muted;scanDirectory.Margin=new Thickness(0,3,0,0);scanDirectory.TextTrimming=TextTrimming.CharacterEllipsis;body.Children.Add(scanDirectory);
        dock.Children.Add(body);scanBanner.Child=dock;return scanBanner;
    }
    /// <summary>刷新扫描横幅。files 在读取 MFT 目录阶段表示已读取的文件记录数。</summary>
    private void RenderScanBanner(string title,ScanEstimate e,long files,long bytes,long errors,string directory)
    {
        scanBannerVersion++;scanBanner.Visibility=Visibility.Visible;
        scanTitle.Text=string.IsNullOrEmpty(e.Phase)?title:title+" · "+e.Phase;
        scanBar.SetState(e.Fraction,e.Indeterminate,!string.IsNullOrEmpty(directory));
        scanPercent.Text=e.Fraction is double f?$"{Math.Floor(f*100):0}%":"";
        bool readingIndex=e.Indeterminate&&e.Phase.Contains("文件目录");
        var counted=readingIndex?$"已读取 {files:N0} 条文件记录":$"已扫描 {files:N0} 个文件，{Labels.Size(bytes)}";
        var remaining=e.Remaining is TimeSpan r?(r==TimeSpan.Zero?"已完成":"预计剩余 "+ScanEstimator.Duration(r)):"预计剩余：估算中";
        scanDetail.Text=$"{counted}，{errors:N0} 项读取失败，已用时 {ScanEstimator.Duration(e.Elapsed)}，{remaining}";
        scanDirectory.Text=string.IsNullOrEmpty(directory)?"":"当前目录："+ScanEstimator.Shorten(directory,110);
        scanDirectory.Visibility=string.IsNullOrEmpty(directory)?Visibility.Collapsed:Visibility.Visible;
    }
    private async Task HideScanBannerLater(int seconds=4)
    {
        var version=scanBannerVersion;await Task.Delay(TimeSpan.FromSeconds(seconds));
        if(version==scanBannerVersion&&!scanning)scanBanner.Visibility=Visibility.Collapsed;
    }
    private static Brush CategoryBrush(Category c)=>Hex(c switch
    {
        Category.Windows=>"#4F6FA8",Category.SpecialFiles=>"#7E57C2",Category.Vendor=>"#C27C2E",Category.Applications=>"#2E8B7E",Category.AppData=>"#5DA9E9",
        Category.Games=>"#7CB342",Category.Temporary=>"#F2B33D",Category.Personal=>"#E07BAA",Category.Development=>"#26A6B8",Category.Unexplained=>"#9E9E9E",_=>"#B0BEC5"
    });
    private static Brush TileText(Brush background)
    {
        var c=((SolidColorBrush)background).Color;return (0.299*c.R+0.587*c.G+0.114*c.B)/255>0.62?Ink:Brushes.White;
    }
    /// <summary>占用地图：Squarified 树图，按 11 类着色，「不可删除」区域加红框；点击文件夹进入，面包屑返回。</summary>
    private UIElement OccupancyMapPanel(Snapshot s)
    {
        var root=OccupancyMap.Build(s);var current=root;
        var panel=new StackPanel();
        panel.Children.Add(Copy("色块大小表示占用空间，颜色表示分类，红框表示不可删除。点击文件夹进入下一层，点击上方路径返回。",11,Muted));
        var crumbs=new WrapPanel{Margin=new Thickness(0,4,0,6)};panel.Children.Add(crumbs);
        var host=new Canvas{Height=380,ClipToBounds=true,Background=Hex("#F3F6F8")};panel.Children.Add(host);
        var legend=new WrapPanel{Margin=new Thickness(0,8,0,0)};
        foreach(var c in Enum.GetValues<Category>().Where(c=>c!=Category.Unexplained))
        {
            var item=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,12,2)};
            item.Children.Add(new Border{Width=10,Height=10,Background=CategoryBrush(c),CornerRadius=new CornerRadius(2),Margin=new Thickness(0,0,4,0),VerticalAlignment=VerticalAlignment.Center});
            item.Children.Add(new TextBlock{Text=Labels.CategoryName(c),FontSize=11,Foreground=Muted});legend.Children.Add(item);
        }
        var lockedKey=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,12,2)};
        lockedKey.Children.Add(new Border{Width=10,Height=10,BorderBrush=Danger,BorderThickness=new Thickness(2),Margin=new Thickness(0,0,4,0),VerticalAlignment=VerticalAlignment.Center});
        lockedKey.Children.Add(new TextBlock{Text="不可删除",FontSize=11,Foreground=Danger});legend.Children.Add(lockedKey);
        panel.Children.Add(legend);
        void Render()
        {
            crumbs.Children.Clear();
            var chain=new List<MapNode>();for(var n=current;n!=null;n=n.Parent)chain.Insert(0,n);
            foreach(var n in chain)
            {
                if(crumbs.Children.Count>0)crumbs.Children.Add(new TextBlock{Text=" › ",Foreground=Muted,VerticalAlignment=VerticalAlignment.Center});
                var target=n;var crumb=Button(n==root?n.Path:n.Name,()=>{current=target;Render();return Task.CompletedTask;});crumb.Padding=new Thickness(8,4,8,4);crumb.IsEnabled=n!=current;crumbs.Children.Add(crumb);
            }
            crumbs.Children.Add(new TextBlock{Text="   "+Labels.Size(current.Bytes),Foreground=Muted,FontSize=11,VerticalAlignment=VerticalAlignment.Center});
            host.Children.Clear();
            double width=host.ActualWidth>0?host.ActualWidth:860,height=host.Height;
            var items=current.Children.Where(c=>c.Bytes>0).ToList();
            const int maxTiles=60;long restBytes=0;int restCount=0;
            if(items.Count>maxTiles){restCount=items.Count-maxTiles;restBytes=items.Skip(maxTiles).Sum(c=>c.Bytes);items=items.Take(maxTiles).ToList();}
            var weights=items.Select(c=>(double)c.Bytes).ToList();if(restBytes>0)weights.Add(restBytes);
            if(weights.Count==0){host.Children.Add(Copy("这一层没有可显示的占用。",12,Muted));return;}
            foreach(var r in Treemap.Squarify(weights,0,0,width,height))
            {
                var node=r.Index<items.Count?items[r.Index]:null;
                bool locked=node?.ProtectedReason!=null;
                var fill=node==null?Hex("#CFD8DC"):CategoryBrush(node.Category);
                var tile=new Border{Width=r.Width,Height=r.Height,Background=fill,BorderBrush=locked?Danger:Brushes.White,BorderThickness=new Thickness(locked?2.5:1)};
                Canvas.SetLeft(tile,r.X);Canvas.SetTop(tile,r.Y);
                var name=node==null?$"其余 {restCount} 项（较小）":node.Name;
                long bytes=node?.Bytes??restBytes;
                var tip=node==null?$"{name}\n{Labels.Size(bytes)}":$"{node.Path}{(node.IsFolder?"":"\n"+node.Name)}\n{Labels.Size(bytes)} · {Labels.CategoryName(node.Category)}{(locked?"\n不可删除："+node.ProtectedReason:"")}{(node.IsFolder&&node.Children.Count>0?"\n点击进入下一层":"")}";
                tile.ToolTip=tip;System.Windows.Automation.AutomationProperties.SetName(tile,tip.Replace("\n","，"));
                if(r.Width>64&&r.Height>24)
                {
                    var text=TileText(fill);var label=new StackPanel{Margin=new Thickness(5,3,4,2)};
                    label.Children.Add(new TextBlock{Text=name,FontSize=11,Foreground=text,TextTrimming=TextTrimming.CharacterEllipsis});
                    if(r.Height>40)label.Children.Add(new TextBlock{Text=Labels.Size(bytes)+(locked?" · 不可删除":""),FontSize=10,Foreground=text,Opacity=0.9,TextTrimming=TextTrimming.CharacterEllipsis});
                    tile.Child=label;
                }
                if(node is {IsFolder:true}&&node.Children.Count>0){tile.Cursor=System.Windows.Input.Cursors.Hand;var target=node;tile.MouseLeftButtonUp+=(_,_)=>{current=target;Render();};}
                host.Children.Add(tile);
            }
        }
        host.SizeChanged+=(_,e)=>{if(Math.Abs(e.NewSize.Width-e.PreviousSize.Width)>1)Render();};
        Render();return panel;
    }
    private static readonly Brush Danger=Hex("#C62828"),DangerSoft=Hex("#FDECEA");
    private static UIElement LargeFileLine(LargeFile f)
    {
        var reason=Cleanup.NotDeletableReason(f.Path,f.Category);if(reason==null)return FileLine(f.Path,f.AllocatedBytes);
        var p=new StackPanel{Margin=new Thickness(0,7,0,7)};var row=new DockPanel();
        var tag=new Border{Background=DangerSoft,BorderBrush=Danger,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(6,1,6,1),Margin=new Thickness(0,2,8,2),VerticalAlignment=VerticalAlignment.Top,Child=new TextBlock{Text="不可删除",FontSize=11,Foreground=Danger}};
        DockPanel.SetDock(tag,Dock.Left);row.Children.Add(tag);row.Children.Add(Copy(f.Path,12,Danger));p.Children.Add(row);
        p.Children.Add(Copy($"{Labels.Size(f.AllocatedBytes)} · {reason}",11,Danger));
        System.Windows.Automation.AutomationProperties.SetName(p,"不可删除："+f.Path+"，"+reason);return p;
    }
    private UIElement SlimPanel(Snapshot snapshot,SlimPlan plan)
    {
        var panel=new StackPanel();
        panel.Children.Add(Copy("根据本次扫描给出可转移或可删除的项目，仅供参考。请确认后再自行处理。标记为「不可删除」的文件不会出现在这里。",11,Muted));
        var exportRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,4)};
        var exportStatus=Copy("",11,Muted);exportStatus.VerticalAlignment=VerticalAlignment.Center;exportStatus.Margin=new Thickness(10,0,0,0);
        var exportButton=Button("导出 AI 提示词",()=>
        {
            // 只生成文字：复制到剪贴板并保存到 exports，不执行任何删除或移动。
            try
            {
                var prompt=Cleanup.BuildAiPrompt(snapshot,plan);
                var folder=System.IO.Path.Combine(Program.Root,"exports");System.IO.Directory.CreateDirectory(folder);
                var file=System.IO.Path.Combine(folder,$"ai-cleanup-prompt-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                System.IO.File.WriteAllText(file,prompt,new System.Text.UTF8Encoding(true));
                string copied;try{Clipboard.SetText(prompt);copied="已复制到剪贴板，";}catch(Exception){copied="剪贴板暂时不可用，";}
                exportStatus.Text=$"{copied}并保存到 {file}。交给 AI 后，应先核实并列出计划，每一步都需确认后再执行。";exportStatus.Foreground=Accent;
            }
            catch(Exception ex){exportStatus.Text="导出失败："+ex.Message;exportStatus.Foreground=Danger;}
            return Task.CompletedTask;
        });
        exportButton.ToolTip="生成可交给 AI 的清理提示词，含不可删除清单和安全规则。复制到剪贴板，并保存到 exports 文件夹。";
        exportButton.IsEnabled=plan.Move.Count+plan.Delete.Count>0;
        exportRow.Children.Add(exportButton);exportRow.Children.Add(exportStatus);panel.Children.Add(exportRow);
        void Group(string title,string hint,IReadOnlyList<SlimSuggestion> items,long total)
        {
            var body=new StackPanel();
            if(items.Count==0)body.Children.Add(Copy("暂时没有符合条件的项目。",12,Muted));
            foreach(var item in items.Take(30))
            {
                var note=$"{Labels.Size(item.AllocatedBytes)} · {item.Reason}";
                if(!item.IsFile)note+=$"（目录中归为「{Labels.CategoryName(item.Category)}」的部分）";
                body.Children.Add(FileLine(item.Path,item.AllocatedBytes,note));
            }
            if(items.Count>30)body.Children.Add(Copy($"另有 {items.Count-30} 项较小的建议未列出。",11,Muted));
            panel.Children.Add(Fold(title,items.Count==0?"暂无":$"{items.Count} 项 · 共 {Labels.Size(total)} · {hint}",body));
        }
        Group("可以转移","建议移到 D: 等其他盘",plan.Move,plan.MoveBytes);
        Group("可以删除","确认不再需要后可删除",plan.Delete,plan.DeleteBytes);
        return panel;
    }
    private static UIElement FileLine(string path,long bytes,string? note=null)
    {var p=new StackPanel{Margin=new Thickness(0,7,0,7)};p.Children.Add(Copy(path,12));p.Children.Add(Copy(note??Labels.Size(bytes),11,Muted));return p;}
}
