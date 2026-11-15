using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Reflection;
using System.Threading.Tasks;
using DiskWatch.App;
using DiskWatch.Core;
using DiskWatch.Storage;

internal static class Program
{
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DiskWatch2.sln"))) return dir.FullName;
        throw new InvalidOperationException("找不到仓库根目录（DiskWatch2.sln）。");
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--watchtower-only"))
        {
            // 独立矢量渲染检查：不构建 MainWindow、不打开数据库、不扫描磁盘。
            var folder = Path.Combine(FindRepoRoot(), "work", "ui-checks", "watchtower"); Directory.CreateDirectory(folder);
            foreach (var width in new[] { 320, 1000 })
            foreach (var progress in new[] { 0d, .5, 1d })
            {
                var scene = new WatchtowerProgress(); scene.SetState(progress, false, false);
                scene.Measure(new Size(width, 96)); scene.Arrange(new Rect(0, 0, width, 96)); scene.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, 96, 96, 96, PixelFormats.Pbgra32); bitmap.Render(scene);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, $"tower-{width}-{progress * 100:0}.png")); png.Save(file);
            }
            // 在无窗口的动画时钟上跳转两帧，确认扫光位置改变；卸载后必须释放时钟。
            var animated = new WatchtowerProgress(); animated.SetState(null, true, true);
            animated.Measure(new Size(1000,96)); animated.Arrange(new Rect(0,0,1000,96));
            var sweep = (DependencyProperty)typeof(WatchtowerProgress).GetField("SweepProperty", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var clock = (System.Windows.Media.Animation.AnimationClock)new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromSeconds(1)).CreateClock(true);
            animated.ApplyAnimationClock(sweep, clock); clock.Controller!.Begin();
            byte[] Frame(double seconds)
            {
                clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(seconds),System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
                animated.InvalidateVisual(); animated.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1000,96,96,96,PixelFormats.Pbgra32); bitmap.Render(animated);
                var data = new byte[1000*96*4]; bitmap.CopyPixels(data,4000,0); return data;
            }
            if(Frame(.2).SequenceEqual(Frame(.8)))throw new Exception("扫光动画两帧没有变化");
            animated.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            if(animated.HasAnimatedProperties)throw new Exception("卸载后动画未停止");
            Console.WriteLine("PASS 6 watchtower renders, moving light frames and unload clock cleanup; no database or scan.");
            return;
        }
        if(args.Contains("--icons-only"))
        {
            var iconPath=Path.Combine(FindRepoRoot(), "assets", "DiskWatch.ico");
            void SameIcon(System.Drawing.Icon actual,string label)
            {
                using var expected=new System.Drawing.Icon(iconPath,actual.Size);
                using var a=actual.ToBitmap();using var b=expected.ToBitmap();
                for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())throw new Exception(label+" pixels differ from approved icon");
                Console.WriteLine("PASS "+label+" matches approved icon ("+a.Width+"x"+a.Height+")");
            }
            foreach(var exe in new[]{Path.Combine(FindRepoRoot(),@"src\DiskWatch.App\bin\Release\net10.0-windows\DiskWatch.App.exe"),Path.Combine(FindRepoRoot(),"DiskWatch2.exe")})
            {using var extracted=System.Drawing.Icon.ExtractAssociatedIcon(exe)??throw new Exception("No EXE icon: "+exe);SameIcon(extracted,Path.GetFileName(exe));}
            var type=typeof(MainWindow).Assembly.GetType("DiskWatch.App.AppIcon")!;
            var windowIcon=(BitmapFrame)type.GetMethod("WindowIcon",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
            if(!windowIcon.IsFrozen||windowIcon.PixelWidth<16)throw new Exception("WPF icon failed to decode");
            Console.WriteLine("PASS embedded WPF icon decoded and frozen");
            using var tray=(System.Drawing.Icon)type.GetMethod("TrayIcon",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;SameIcon(tray,"Tray");
            return;
        }
        // DISKWATCH_UI_DATA 可指向数据库副本，避免在正式程序扫描时动到正在使用的数据库。
        var uiData = Environment.GetEnvironmentVariable("DISKWATCH_UI_DATA");
        if (string.IsNullOrWhiteSpace(uiData)) throw new InvalidOperationException("请设置 DISKWATCH_UI_DATA 指向专用的测试数据目录，不要使用正在使用的数据库。");
        var repo = new Repository(uiData, new Settings());
        var output=Path.Combine(FindRepoRoot(),"work","ui-checks");Directory.CreateDirectory(output);
        var samples=repo.Samples(DateTimeOffset.UtcNow.AddDays(-1));
        foreach(var metric in new[]{"剩余空间","已用空间","已用净变化"})
        {
            var chart=new TrendChart();chart.SetData(samples,metric,60);
            chart.Measure(new Size(1100,500));chart.Arrange(new Rect(0,0,1100,500));chart.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1100,500,96,96,PixelFormats.Pbgra32);bitmap.Render(chart);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(output,metric+".png")))encoder.Save(file);
        }
        var pairs=new List<(Snapshot,Snapshot?)>();
        foreach(var root in new[]{@"C:\",@"D:\",@"E:\",@"F:\"}){var list=repo.LatestSnapshots(root);if(list.Count>0)pairs.Add((list[0],list.ElementAtOrDefault(1)));}
        Directory.CreateDirectory(Path.Combine(FindRepoRoot(),"exports"));
        var summary=Path.Combine(FindRepoRoot(),"exports","scan-summary-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
        File.WriteAllText(summary,ScanSummary.Build(pairs),new System.Text.UTF8Encoding(true));
        Console.WriteLine("Rendered 3 charts with "+samples.Count+" real samples. Summary: "+summary);
        Console.WriteLine("SQLite integrity: "+repo.IntegrityCheck());
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Dispatcher.InvokeAsync(async()=>
        {
            MainWindow? window=null;
            try
            {
                window=new MainWindow(new Settings(),repo,false,true);
                object Field(string name)=>typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                async Task Call(string name)=>await (Task)typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
                typeof(MainWindow).GetMethod("UpdateDriveCards",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{samples.GroupBy(s=>s.Root).Select(g=>g.MaxBy(s=>s.Time)!).ToList()});
                await Call("RefreshSummary");await Call("LoadSnapshot");await Call("RefreshTrend");
                void Capture(string name,double width=1160,double height=740)
                {
                    var visual=(FrameworkElement)window.Content;visual.Measure(new Size(width,height));visual.Arrange(new Rect(0,0,width,height));visual.UpdateLayout();
                    var bmp=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);bmp.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,name+".png")))png.Save(f);
                }
                Capture("overview-new");Capture("overview-small",900,650);
                // 扫描进度横幅：用模拟进度渲染（不真的扫描）。
                var render=typeof(MainWindow).GetMethod("RenderScanBanner",BindingFlags.Instance|BindingFlags.NonPublic)!;
                render.Invoke(window,new object[]{@"正在扫描 C:\ · 第 1/4 个盘",new ScanEstimate(0.42,false,TimeSpan.FromSeconds(131),TimeSpan.FromSeconds(185),"正在扫描"),253_118L,85L*1073741824,12L,@"C:\Users\u\AppData\Local\Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache\Local\Microsoft\Windows\INetCache"});
                Capture("scan-progress");Capture("scan-progress-small",900,650);
                render.Invoke(window,new object[]{@"正在扫描 C:\",new ScanEstimate(null,true,TimeSpan.FromSeconds(9),null,"正在读取文件目录"),412_000L,0L,0L,"正在读取文件目录"});
                Capture("scan-progress-mft");
                var banner=(Border)Field("scanBanner");if(banner.Visibility!=Visibility.Visible)throw new Exception("scan banner not visible while scanning");
                banner.Visibility=Visibility.Collapsed;Console.WriteLine("UI: scan progress banner rendered (determinate 42% and MFT indeterminate).");
                // 扫描范围下拉：离屏渲染工具栏（不真的扫描）。
                {
                    typeof(MainWindow).GetMethod("RefreshScanRangeOptions",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    var combo=(ComboBox)Field("scanRange");
                    var start=(Button)Field("scanAction");
                    var cancel=(Button)Field("cancelAction");
                    var options=((System.Collections.IEnumerable)combo.ItemsSource).Cast<object>().ToList();
                    if((combo.SelectedValue as string)!=ScanRangeChoice.AllKey)throw new Exception("扫描范围默认应是全部磁盘，实际是 "+combo.SelectedValue);
                    if(options.Count<2)throw new Exception("扫描范围下拉应包含全部磁盘和至少一个盘，实际 "+options.Count);
                    var firstLabel=(string)options[0].GetType().GetProperty("Label")!.GetValue(options[0])!;
                    if(firstLabel!="全部磁盘")throw new Exception("第一项应是全部磁盘，实际是 "+firstLabel);
                    start.IsEnabled=true;cancel.Visibility=Visibility.Collapsed;combo.IsEnabled=true;banner.Visibility=Visibility.Collapsed;
                    BitmapSource Frame(double width=1160,double height=740)
                    {
                        var visual=(FrameworkElement)window.Content;visual.Measure(new Size(width,height));visual.Arrange(new Rect(0,0,width,height));visual.UpdateLayout();
                        var bmp=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);bmp.Render(visual);return bmp;
                    }
                    void SaveBmp(BitmapSource bmp,string name)
                    {
                        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(output,name+".png"));png.Save(f);
                    }
                    CroppedBitmap Corner(BitmapSource bmp,int extraH=0)
                    {
                        int w=Math.Min(640,bmp.PixelWidth-16), h=Math.Min(90+extraH,bmp.PixelHeight-8), x=Math.Max(0,bmp.PixelWidth-w-16);
                        return new CroppedBitmap(bmp,new Int32Rect(x,8,w,h));
                    }
                    var idle=Frame();SaveBmp(idle,"scan-range-idle");SaveBmp(Corner(idle),"scan-range-idle-toolbar");
                    Point origin=combo.TransformToAncestor((Visual)window.Content).Transform(new Point(0,combo.ActualHeight));
                    var list=new StackPanel();
                    foreach(var item in options)
                    {
                        var typ=item.GetType();
                        var key=(string)typ.GetProperty("Key")!.GetValue(item)!;
                        var label=(string)typ.GetProperty("Label")!.GetValue(item)!;
                        bool selectedItem=string.Equals(key,combo.SelectedValue as string,StringComparison.OrdinalIgnoreCase);
                        var row=new Border{Padding=new Thickness(10,8,10,8),CornerRadius=new CornerRadius(4),Background=selectedItem?(Brush)new BrushConverter().ConvertFromString("#EAF5F5")!:Brushes.White};
                        row.Child=new TextBlock{Text=label,FontSize=13,Foreground=selectedItem?(Brush)new BrushConverter().ConvertFromString("#087F8C")!:(Brush)new BrushConverter().ConvertFromString("#172B36")!};
                        list.Children.Add(row);
                    }
                    var popup=new Border{Background=Brushes.White,BorderBrush=(Brush)new BrushConverter().ConvertFromString("#DCE5E9")!,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(4),Child=list,MinWidth=Math.Max(128,combo.ActualWidth)};
                    popup.Measure(new Size(400,400));popup.Arrange(new Rect(popup.DesiredSize));popup.UpdateLayout();
                    var listBmp=new RenderTargetBitmap((int)Math.Ceiling(popup.DesiredSize.Width),(int)Math.Ceiling(popup.DesiredSize.Height),96,96,PixelFormats.Pbgra32);listBmp.Render(popup);
                    const double W=1160,H=740;
                    var dv=new DrawingVisual();
                    using(var dc=dv.RenderOpen())
                    {
                        dc.DrawImage(idle,new Rect(0,0,W,H));
                        dc.DrawImage(listBmp,new Rect(origin.X,origin.Y,listBmp.PixelWidth,listBmp.PixelHeight));
                    }
                    var opened=new RenderTargetBitmap((int)W,(int)H,96,96,PixelFormats.Pbgra32);opened.Render(dv);
                    SaveBmp(opened,"scan-range-open");SaveBmp(Corner(opened,20+(int)Math.Ceiling(popup.DesiredSize.Height)),"scan-range-open-toolbar");
                    start.IsEnabled=false;cancel.Visibility=Visibility.Visible;combo.IsEnabled=false;
                    render.Invoke(window,new object[]{@"正在扫描 C:\",new ScanEstimate(0.42,false,TimeSpan.FromSeconds(131),TimeSpan.FromSeconds(185),"正在扫描"),253_118L,85L*1073741824,12L,@"C:\Windows\System32"});
                    if(combo.IsEnabled||start.IsEnabled||cancel.Visibility!=Visibility.Visible)throw new Exception("扫描中应禁用范围下拉和开始扫描，并显示取消扫描");
                    var busy=Frame();SaveBmp(busy,"scan-range-scanning");SaveBmp(Corner(busy,80),"scan-range-scanning-toolbar");
                    banner.Visibility=Visibility.Collapsed;start.IsEnabled=true;cancel.Visibility=Visibility.Collapsed;combo.IsEnabled=true;
                    Console.WriteLine("UI: scan range dropdown rendered. options="+string.Join("、",options.Select(o=>(string)o.GetType().GetProperty("Label")!.GetValue(o)!)));
                }
                var selected=(ComboBox)Field("selectedRoot");
foreach(var root in new[]{@"D:\",@"E:\",@"F:\",@"C:\"}){selected.SelectedItem=root;await Call("LoadSnapshot");}
                static IEnumerable<Expander> Expanders(DependencyObject p){for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var c=VisualTreeHelper.GetChild(p,i);if(c is Expander e)yield return e;foreach(var nested in Expanders(c))yield return nested;}}
                Capture("overview-return");
                var rootVisual=(FrameworkElement)window.Content;
                var first=Expanders(rootVisual).First();first.IsExpanded=true;rootVisual.UpdateLayout();
                var nestedCategory=Expanders(first).First();nestedCategory.IsExpanded=true;rootVisual.UpdateLayout();
                var directory=Expanders(nestedCategory).First();directory.IsExpanded=true;rootVisual.UpdateLayout();Capture("overview-expanded");
                static Canvas? MapCanvas(object node){if(node is Canvas c&&c.Height==380)return c;if(node is DependencyObject d)foreach(var child in LogicalTreeHelper.GetChildren(d)){var hit=MapCanvas(child);if(hit!=null)return hit;}return null;}
                first.IsExpanded=false;rootVisual.UpdateLayout();
                var mapFold=Expanders(rootVisual).FirstOrDefault(e=>e.Content!=null&&MapCanvas(e.Content)!=null)??throw new Exception("占用地图 section missing");
                mapFold.IsExpanded=true;rootVisual.UpdateLayout();var mapCanvas=MapCanvas(mapFold.Content)!;mapCanvas.BringIntoView();rootVisual.UpdateLayout();Capture("occupancy-map");
                if(mapCanvas.Children.OfType<Border>().Count()==0)throw new Exception("占用地图 rendered no tiles");
                Console.WriteLine($"UI: occupancy map rendered {mapCanvas.Children.OfType<Border>().Count()} tiles, {mapCanvas.Children.OfType<Border>().Count(b=>b.BorderThickness.Left>2)} marked 不可删除.");
                typeof(MainWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{Field("trendTab"),"变化趋势","查看空间随时间的变化。"});await Call("RefreshTrend");Capture("trend-new");
                typeof(MainWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{Field("preferencesTab"),"偏好设置","扫描、提醒与分类。"});Capture("settings-new");
                {
                    var page=(FrameworkElement)((TabItem)Field("preferencesTab")).Content;rootVisual.UpdateLayout();
                    var folds=Expanders(page).ToList();if(folds.Count==0)throw new Exception("settings folds missing");
                    folds[0].IsExpanded=true;rootVisual.UpdateLayout();
                    var scope=Expanders(folds[0]).FirstOrDefault()??throw new Exception("调整扫描范围 fold missing");scope.IsExpanded=true;rootVisual.UpdateLayout();
                    var list=(StackPanel)Field("scopeList");if(list.Children.OfType<DockPanel>().Count()==0)throw new Exception("scope list has no rows");
                    var boxes=list.Children.OfType<DockPanel>().SelectMany(r=>r.Children.OfType<CheckBox>()).ToList();
                    if(boxes.Count==0||boxes.All(b=>b.IsChecked!=true))throw new Exception("scope list has no checked drive");
                    typeof(MainWindow).GetMethod("MarkDirty",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    var dirty=(TextBlock)Field("dirtyLabel");if(dirty.Visibility!=Visibility.Visible)throw new Exception("dirty label not shown");
                    scope.BringIntoView();rootVisual.UpdateLayout();Capture("settings-scope");
                    typeof(MainWindow).GetMethod("PopulateSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    if(dirty.Visibility==Visibility.Visible)throw new Exception("dirty label not cleared by reload");
                    Console.WriteLine($"UI: scan scope list rendered {boxes.Count} entries ({boxes.Count(b=>b.IsChecked==true)} checked); unsaved-change hint OK.");
                }
                typeof(MainWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{Field("glossaryTab"),"名词说明","栏目含义与统计说明"});Capture("glossary");
                var glossary=(DockPanel)((TabItem)Field("glossaryTab")).Content;
                var search=((StackPanel)glossary.Children[0]).Children.OfType<TextBox>().Single();search.Text="未解释差额";Capture("glossary-search");
                var matches=(StackPanel)((ScrollViewer)glossary.Children[1]).Content;
                if(matches.Children.Count==0||!matches.Children.OfType<Border>().All(b=>((Expander)b.Child).IsExpanded))throw new Exception("Glossary search did not expand matches");
                // 塞满预测（真实采样副本）、应用排行（合成数据）、周报摘要（真实采样副本）、骤降抓现场开关。
                {
                    var nav=typeof(MainWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    nav.Invoke(window,new object[]{Field("summaryTab"),"空间总览","选择磁盘，查看空间占用。"});
                    await Call("RefreshForecasts");
                    var forecastMap=(System.Collections.IDictionary)Field("forecasts");if(forecastMap.Count==0)throw new Exception("塞满预测没有结果");
                    typeof(MainWindow).GetMethod("UpdateDriveCards",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{samples.GroupBy(s=>s.Root).Select(g=>g.MaxBy(s=>s.Time)!).ToList()});
                    selected.SelectedItem=@"C:\";await Call("LoadSnapshot");((ScrollViewer)((TabItem)Field("summaryTab")).Content).ScrollToTop();Capture("fill-forecast");
                    foreach(System.Collections.DictionaryEntry e in forecastMap)Console.WriteLine($"UI: 塞满预测 {e.Key}: {Forecast.Describe((FillForecast[])e.Value!)}");
                    var t0=new DateTimeOffset(2026,11,13,13,0,0,TimeSpan.FromHours(8));
                    AppUsageRecord Rec(string id,DateTimeOffset at,params (string App,double GiB,bool Known)[] apps)=>new(id,@"C:\","vol",at,"Partial",12,"r1","coverage-v2:x","apps-v1:k","目录遍历",apps.Select(a=>new AppUsage(a.App,a.Known,(long)(a.GiB*1073741824),100)).ToList());
                    var before=Rec("a",t0,("Windows 系统",31.2,true),("微信",22.4,true),("Edge 缓存",1.1,true),("Temp 临时文件",2.3,true),("联想 Vantage",1.6,true),("Windows 更新",0.9,true),("Codex",3.4,true),(@"C:\Users\u\AppData\Local\FooApp",0.2,false),("页面 / 休眠文件",14.0,true));
                    var after=Rec("b",t0.AddDays(1),("Windows 系统",31.3,true),("微信",24.6,true),("Edge 缓存",1.9,true),("Temp 临时文件",6.1,true),("联想 Vantage",2.4,true),("Windows 更新",1.7,true),("Codex",3.9,true),(@"C:\Users\u\AppData\Local\FooApp",0.45,false),("页面 / 休眠文件",14.0,true));
                    before=before with{Apps=before.Apps.Select(a=>a with{Known=!a.App.StartsWith("C:")}).ToList()};after=after with{Apps=after.Apps.Select(a=>a with{Known=!a.App.StartsWith("C:")}).ToList()};
                    var list=typeof(MainWindow).GetMethod("AppRankingList",BindingFlags.Static|BindingFlags.NonPublic)!;
                    var ranked=AppRanking.Build(after,before);ranked=ranked with{Note="（界面检查用合成数据）"+ranked.Note};
                    var firstScan=AppRanking.Build(after,null);firstScan=firstScan with{Note="（界面检查用合成数据）"+firstScan.Note};
                    async Task Panel(string name,UIElement content,double w=1000,double h=560)
                    {
                        var host=new Border{Background=Brushes.White,Padding=new Thickness(24),Child=content};host.Measure(new Size(w,h));host.Arrange(new Rect(0,0,w,h));host.UpdateLayout();
                        await app.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);host.UpdateLayout();
                        var bmp=new RenderTargetBitmap((int)w,(int)h,96,96,PixelFormats.Pbgra32);bmp.Render(host);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,name+".png")))png.Save(f);
                    }
                    await Panel("app-ranking",(UIElement)list.Invoke(null,new object[]{ranked,new GrowthThresholds(),15})!);
                    await Panel("app-ranking-first",(UIElement)list.Invoke(null,new object[]{firstScan,new GrowthThresholds(),15})!);
                    Console.WriteLine("UI: app ranking rendered. Top: "+AppRanking.TopGrowth(ranked,3));
                    nav.Invoke(window,new object[]{Field("reportTab"),"历史报表","按日、周、月查看空间变化。"});
                    ((ComboBox)Field("weeklyWhich")).SelectedIndex=1;await Call("GenerateWeekly");
                    var weekly=(TextBox)Field("weeklyText");if(string.IsNullOrWhiteSpace(weekly.Text)||!weekly.Text.Contains("周报摘要"))throw new Exception("周报没有生成");
                    if(weekly.Text.Contains("不会发送到微信")||weekly.Text.Contains("只保存在本机")||weekly.Text.Contains("不会推送到"))throw new Exception("导出文案仍含本机/推送句子");
                    static IEnumerable<TextBlock> Texts(DependencyObject p){for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var c=VisualTreeHelper.GetChild(p,i);if(c is TextBlock t)yield return t;foreach(var n in Texts(c))yield return n;}}
                    if(Texts(rootVisual).Any(t=>(t.Text??"").Contains("不会推送到微信")||(t.Text??"").Contains("只在本机生成")))throw new Exception("界面仍显示本机/推送句子");
                    var weeklyBody=(StackPanel)Field("weeklyBody");if(weeklyBody.Children.Count<4)throw new Exception("周报视觉结构不完整："+weeklyBody.Children.Count);
                    rootVisual.UpdateLayout();Capture("weekly-summary",1160,1500);
                    async Task ShotWeekly(string name)
                    {
                        weeklyBody.UpdateLayout();
                        var card=(FrameworkElement)weeklyBody.Parent;
                        while(card!=null && (card is not Border || card.ActualWidth<200)) card=card.Parent as FrameworkElement;
                        card??=weeklyBody;
                        await app.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        card.UpdateLayout();
                        var w=Math.Max(1000,card.ActualWidth>0?card.ActualWidth:1000);
                        var h=Math.Max(900,card.ActualHeight>0?card.DesiredSize.Height:900);
                        card.Measure(new Size(w,h));card.Arrange(new Rect(0,0,w,Math.Max(h,card.DesiredSize.Height)));card.UpdateLayout();
                        var bh=Math.Max((int)Math.Ceiling(card.ActualHeight),(int)Math.Ceiling(card.DesiredSize.Height));
                        var bw=Math.Max(1000,(int)Math.Ceiling(card.ActualWidth));
                        var bmp=new RenderTargetBitmap(bw,Math.Max(bh,800),96,96,PixelFormats.Pbgra32);bmp.Render(card);
                        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,name+".png")))png.Save(f);
                    }
                    await ShotWeekly("weekly-summary-card");
                    File.WriteAllText(Path.Combine(output,"weekly-summary-sample.md"),weekly.Text,new System.Text.UTF8Encoding(true));
                    Console.WriteLine("UI: weekly summary (this week) generated ("+weekly.Text.Length+" chars).");
                    ((ComboBox)Field("weeklyWhich")).SelectedIndex=0;await Call("GenerateWeekly");
                    rootVisual.UpdateLayout();Capture("weekly-summary-lastweek",1160,1500);
                    await ShotWeekly("weekly-summary-lastweek-card");
                    File.WriteAllText(Path.Combine(output,"weekly-summary-lastweek.md"),weekly.Text,new System.Text.UTF8Encoding(true));
                    Console.WriteLine("UI: weekly summary (last week) generated ("+weekly.Text.Length+" chars).");
                    ((ComboBox)Field("weeklyWhich")).SelectedIndex=1;await Call("GenerateWeekly");
                    nav.Invoke(window,new object[]{Field("preferencesTab"),"偏好设置","扫描、提醒与分类。"});
                    var toggle=(CheckBox)Field("autoCapture");if(toggle.IsChecked==true)throw new Exception("骤降抓现场应默认关闭");
                    var page=(FrameworkElement)((TabItem)Field("preferencesTab")).Content;rootVisual.UpdateLayout();
                    foreach(var fold in Expanders(page))fold.IsExpanded=false;
                    static bool Contains(object node,object target){if(ReferenceEquals(node,target))return true;if(node is DependencyObject d)foreach(var child in LogicalTreeHelper.GetChildren(d))if(Contains(child,target))return true;return false;}
                    var alertFold=Expanders(page).First(e=>e.Content!=null&&Contains(e.Content,toggle));alertFold.IsExpanded=true;rootVisual.UpdateLayout();
                    toggle.BringIntoView();rootVisual.UpdateLayout();Capture("settings-autocapture");
                    Console.WriteLine("UI: 骤降抓现场 toggle rendered (default off).");
                }
                // 变化明细：用合成的两份文件级明细渲染（不扫描真实磁盘）。
                {
                    const long M=1L<<20; var t0=new DateTime(2026,11,13,1,0,0,DateTimeKind.Utc); var t1=t0.AddDays(1);
                    var before=new FileIndex{Root=@"C:\"}; var after=new FileIndex{Root=@"C:\"};
                    void Both(string d,string n,long b,long a,bool touched=true){before.Add(d,n,b,b,t0);after.Add(d,n,a,a,touched?t1:t0);}
                    for(int i=0;i<40;i++)Both(@"C:\Windows\System32",$"api-ms-win-core-{i}.dll",64*1024,64*1024,false);
                    Both(@"C:\Windows\System32","ntoskrnl.exe",11*M,12*M);Both(@"C:\Windows\System32\DriverStore\FileRepository\nvlt.inf_amd64","nvlddmkm.sys",80*M,96*M);
                    Both(@"C:\Windows\SoftwareDistribution\Download","a1b2c3.cab",20*M,20*M,false);after.Add(@"C:\Windows\SoftwareDistribution\Download","KB5046617.cab",210*M,210*M,t1);
                    Both(@"C:\Users\u\AppData\Local\Temp","setup.log",2*M,9*M);for(int i=0;i<30;i++)after.Add(@"C:\Users\u\AppData\Local\Temp\WinGet",$"cache-{i:000}.msix",4*M,4*M,t1);before.Add(@"C:\Users\u\AppData\Local\Temp","old.tmp",300*M,300*M,t0);
                    Both(@"C:\Users\u\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data","data_1",40*M,95*M);
                    after.Add(@"C:\Users\u\Videos\Captures","录屏 2026-11-14.mp4",1350*M,1350*M,t1);Both(@"C:\Users\u\Videos","旧视频.mp4",500*M,500*M,false);
                    before.Add(@"C:\Users\u\Downloads","WeChatSetup.exe",250*M,250*M,t0);before.Add(@"C:\Users\u\Downloads","old-report.pdf",3*M,3*M,t0);after.Add(@"C:\Users\u\Downloads","report-final.pdf",3*M,3*M,t1);
                    Both(@"C:\","pagefile.sys",8192*M,12288*M);Both(@"C:\","hiberfil.sys",6400*M,6400*M,false);
                    // Codex 审查修复后，对比要求两份明细带覆盖证明；合成数据补上同一份上下文。
                    before.Context=after.Context=new FileComparisonContext("ui-check-volume",@"C:\","r1",ScanCoverage.Version+":ui",true);
                    var changes=FileDiff.Compare(before,after);var tree=ChangeTree.Build(@"C:\",before,after,changes);
                    string? Lock(string p)=>Cleanup.NotDeletableReason(p,p.StartsWith(@"C:\Windows\",StringComparison.OrdinalIgnoreCase)?Category.Windows:p.EndsWith(".sys",StringComparison.OrdinalIgnoreCase)&&p.LastIndexOf('\\')==2?Category.SpecialFiles:Category.Other);
                    var view=new ChangeDetailsView("对比：上次 2026-11-13 09:00（合成数据）→ 当前 2026-11-14 09:00，两次都是目录遍历、同一套规则。大小按“占用空间”（分配空间）计。",tree,new GrowthThresholds(),Lock){Margin=new Thickness(18)};
                    var host=new Border{Background=Brushes.White,Child=view};
                    async Task Shot(string name){host.Measure(new Size(1180,760));host.Arrange(new Rect(0,0,1180,760));host.UpdateLayout();await app.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);host.UpdateLayout();
                        var bmp=new RenderTargetBitmap(1180,760,96,96,PixelFormats.Pbgra32);bmp.Render(host);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,name+".png")))png.Save(f);}
                    await Shot("change-details");
                    view.Navigate(ChangeTree.Find(tree,@"C:\Users\u\AppData\Local\Temp")!);await Shot("change-details-drill");
                    view.Navigate(ChangeTree.Find(tree,@"C:\Windows\System32")!);await Shot("change-details-locked");
                    var sum=ChangeTree.Summarize(tree,new GrowthThresholds());
                    if(sum.Added==0||sum.Removed==0||sum.SignificantDirectories==0||sum.RapidDirectories==0)throw new Exception("change summary unexpected: "+sum.Text);
                    Console.WriteLine("UI: change details rendered. "+sum.Text);
                }
                Console.WriteLine("UI: disk switching, nested disclosure, chart and 900/1160px layout checks passed.");
            }
            catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}
            finally
            {
                if(window!=null)
                {
                    ((System.Windows.Forms.NotifyIcon)typeof(MainWindow).GetField("tray",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!).Dispose();
                    ((System.Drawing.Icon)typeof(MainWindow).GetField("trayIcon",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!).Dispose();
                }
                app.Shutdown();
            }
        });
        app.Run();
    }
}
