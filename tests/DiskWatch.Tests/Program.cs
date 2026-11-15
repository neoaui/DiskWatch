using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskWatch.Core;
using DiskWatch.Storage;
using DiskWatch.Windows;
using Microsoft.Win32.SafeHandles;

namespace DiskWatch.Tests;

internal static partial class Program
{
    private static int passed, failed, skipped;
    private static void Skip(string name, string why) { skipped++; output.Add("SKIP " + name + "：" + why); }
    private static readonly List<string> output = [];
    private static readonly string repoRoot = FindRepoRoot();
    private static readonly string work = Path.Combine(repoRoot, "work", "tests", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
    private static readonly DateTimeOffset epoch = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    private static VolumeSample Sample(double free, int minutes = 0) => new("volume-test", @"C:\", epoch.AddMinutes(minutes), 200L * 1073741824, (long)(free * 1073741824));
    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static async Task Test(string name, Func<Task> action)
    {
        try { await action(); passed++; output.Add("PASS " + name); }
        catch (Exception ex) { failed++; output.Add("FAIL " + name + ": " + ex); }
        Console.WriteLine(output[^1]);
    }
    private static Task Run(Action action) { action(); return Task.CompletedTask; }
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DiskWatch2.sln"))) return dir.FullName;
        throw new InvalidOperationException("找不到仓库根目录（DiskWatch2.sln）。");
    }
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--inspect-live"))
        {
            var repo = new Repository(Path.Combine(repoRoot, "data"), new Settings());
            var rows = new List<object>();
            foreach (var root in new[] { @"C:\", @"D:\", @"E:\", @"F:\" })
            {
                var sample = NativeMetadata.Sample(root);
                var snapshot = repo.LatestSnapshots(root, 1).FirstOrDefault();
                rows.Add(new { sample.Root, TotalGiB = sample.TotalBytes / 1073741824d, FreeGiB = sample.FreeBytes / 1073741824d,
                    Snapshot = snapshot == null ? null : new { snapshot.Status, snapshot.Files, snapshot.Errors, snapshot.ReparsePointsSkipped, snapshot.HardLinksSkipped,
                        Seconds = (snapshot.Finished - snapshot.Started).TotalSeconds, AllocatedGiB = snapshot.AllocatedBytes / 1073741824d, ResidualGiB = snapshot.Residual / 1073741824d,
                        Categories = Enum.GetValues<Category>().Select(c => new { Name = Labels.CategoryName(c), GiB = c == Category.Unexplained ? snapshot.Residual / 1073741824d : snapshot.Buckets.Where(b => b.Category == c).Sum(b => b.AllocatedBytes) / 1073741824d }) } });
            }
            var report = System.Text.Json.JsonSerializer.Serialize(new { Time = DateTimeOffset.Now, Integrity = repo.IntegrityCheck(), Volumes = rows }, Repository.JsonOptions);
            Directory.CreateDirectory(Path.Combine(repoRoot, "work")); File.WriteAllText(Path.Combine(repoRoot, "work", "LIVE-VALIDATION.json"), report);
            Console.WriteLine(report); return 0;
        }
        if (args.Contains("--inspect-slim"))
        {
            // 只读：用最近快照预览“不可删除”和“瘦身建议”，不扫描、不改动任何文件。
            var repo = new Repository(Path.Combine(repoRoot, "data"), new Settings());
            foreach (var root in new[] { @"C:\", @"D:\", @"E:\", @"F:\" })
            {
                var snapshot = repo.LatestSnapshots(root, 1).FirstOrDefault(); if (snapshot == null) continue;
                var plan = Cleanup.Plan(snapshot);
                Console.WriteLine($"== {root} large={snapshot.LargestFiles.Count} locked={snapshot.LargestFiles.Count(f => !Cleanup.IsDeletable(f.Path, f.Category))} move={plan.Move.Count}/{Labels.Size(plan.MoveBytes)} delete={plan.Delete.Count}/{Labels.Size(plan.DeleteBytes)}");
                foreach (var f in snapshot.LargestFiles.Take(20)) Console.WriteLine($"  L {Labels.Size(f.AllocatedBytes)} {f.Path} [{f.Category}] {Cleanup.NotDeletableReason(f.Path, f.Category) ?? "-"}");
                foreach (var x in plan.Move.Take(12).Concat(plan.Delete.Take(12))) Console.WriteLine($"  {x.Action} {Labels.Size(x.AllocatedBytes)} {x.Path} [{x.Category}{(x.IsFile ? ",file" : "")}] {x.Reason}");
                if (root == @"C:\" && args.Contains("--export-prompt"))
                {
                    var file = Path.Combine(repoRoot, "exports", $"ai-cleanup-prompt-sample-C-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, Cleanup.BuildAiPrompt(snapshot, plan), new System.Text.UTF8Encoding(true));
                    Console.WriteLine("PROMPT " + file);
                }
            }
            return 0;
        }
        Directory.CreateDirectory(work);
        var settings = new Settings { ScanDelayMs = 0, FastScan = false };
        var context = new ClassificationContext { PersonalRoots = [@"D:\Documents"], GameRoots = [@"E:\STEAM\steamapps"], Applications = [new("Editor", @"C:\Tools\Editor")] };
        var classifier = new Classifier(context);
        await Test("总结保留负残差并拒绝不完整目录归因", () => Run(() => {
            var before = new Snapshot { Root="C", VolumeId="v", IsWholeVolume=true, Status="Complete", Started=epoch, Finished=epoch, After=Sample(50), Buckets=[new("a",Category.Personal,100,100,1)] };
            var current = new Snapshot { Root="C", VolumeId="v", IsWholeVolume=true, Status="Complete", Errors=1, Started=epoch, Finished=epoch.AddMinutes(1), After=Sample(49), Buckets=[new("a",Category.Personal,300L*1073741824,300L*1073741824,1)] };
            var text=ScanSummary.Build([(current,before)]);Assert(text.Contains("+1.000") && text.Contains("差额为负") && text.Contains("不将目录差值视为确定的增长"));
        }));
        await Test("子目录总结不伪造全卷残差且标注取消", () => Run(() => {var text=ScanSummary.Build([(new Snapshot {Root="fixture",Status="Cancelled",Started=epoch,Finished=epoch},null)]);Assert(text.Contains("已取消") && text.Contains("不计算整盘未解释差额"));}));
        await Test("11 个分类 ID 完整且文案集中", () => Run(() => Assert(Enum.GetValues<Category>().Length == 11 && Enum.GetValues<Category>().All(x => Labels.CategoryName(x).Length > 0))));
        await Test("路径边界不把 WindowsOld 判为 Windows", () => Run(() => Assert(classifier.Classify(@"C:\WindowsOld\x").Category == Category.Other)));
        await Test("关闭安装元数据时不加载应用清单", () => Run(() => { var discovered = ApplicationDiscovery.Discover(new Settings { ReadInstallationMetadata = false }); Assert(discovered.Applications.Count == 0 && discovered.GameRoots.Count == 0); }));
        await Test("系统本体 / 特殊文件 / 页面文件重定位", () => Run(() => { Assert(classifier.Classify(@"C:\Windows\System32\x.dll").Category == Category.Windows); Assert(classifier.Classify(@"F:\pagefile.sys").Category == Category.SpecialFiles); Assert(classifier.Classify(@"C:\Downloads\pagefile.sys").Category != Category.SpecialFiles); }));
        await Test("联想与华硕规则合成样本", () => Run(() => { Assert(classifier.Classify(@"C:\Program Files\Lenovo\x.exe").Category == Category.Vendor); Assert(classifier.Classify(@"C:\Program Files\ASUS\Armoury Crate\x.exe").Category == Category.Vendor); Assert(classifier.Classify(@"C:\ProgramData\Lenovo\Cache\x").Category == Category.AppData); }));
        await Test("开发目录优先于个人目录", () => Run(() => Assert(classifier.Classify(@"D:\Documents\project\node_modules\x").Category == Category.Development)));
        await Test("自定义游戏库和已知个人目录", () => Run(() => { Assert(classifier.Classify(@"E:\STEAM\steamapps\common\x").Category == Category.Games); Assert(classifier.Classify(@"D:\Documents\report.docx").Category == Category.Personal); }));
        await Test("安装记录 / 缓存 / 临时 / 其他", () => Run(() => { Assert(classifier.Classify(@"C:\Tools\Editor\x").Category == Category.Applications); Assert(classifier.Classify(@"C:\Users\u\AppData\Local\App\GPUCache\x").Category == Category.AppData); Assert(classifier.Classify(@"C:\Windows\Temp\x").Category == Category.Temporary); Assert(classifier.Classify(@"C:\$Recycle.Bin\x").Category == Category.Other); }));
        await Test("用户纠正按最具体规则覆盖且可撤销", () => Run(() => { context.Overrides.Add(new(@"C:\Tools", Category.Personal)); context.Overrides.Add(new(@"C:\Tools\Editor", Category.Development)); Assert(classifier.Classify(@"C:\Tools\Editor\x").Category == Category.Development); context.Overrides.Clear(); Assert(classifier.Classify(@"C:\Tools\Editor\x").Category == Category.Applications); }));
        await Test("首次低于两档只合并提醒一次", () => Run(() => { var state = new AlertState(); var a = AlertEngine.Evaluate(Sample(9.13), state, settings); Assert(a.Count == 1 && state.FiredThresholds.SetEquals([20d, 11d])); Assert(AlertEngine.Evaluate(Sample(9.13, 1), state, settings).Count == 0); }));
        await Test("三档下穿及滞回后重新布防", () => Run(() => { var state = new AlertState(); AlertEngine.Evaluate(Sample(19), state, settings); AlertEngine.Evaluate(Sample(20.5, 1), state, settings); Assert(state.FiredThresholds.Contains(20)); AlertEngine.Evaluate(Sample(21, 2), state, settings); Assert(!state.FiredThresholds.Contains(20)); Assert(AlertEngine.Evaluate(Sample(19, 3), state, settings).Any(x => x.Kind == "LowSpace")); }));
        await Test("骤降触发且冷却去重", () => Run(() => { var state = new AlertState(); AlertEngine.Evaluate(Sample(50), state, settings); Assert(AlertEngine.Evaluate(Sample(47, 1), state, settings).Single().Kind == "SuddenDrop"); Assert(AlertEngine.Evaluate(Sample(44, 2), state, settings).Count == 0); }));
        await Test("零滞回阈值相等不重复提醒", () => Run(() => { var state = new AlertState(); var zero = new Settings { HysteresisGiB = 0 }; Assert(AlertEngine.Evaluate(Sample(20), state, zero).Count == 1); Assert(AlertEngine.Evaluate(Sample(20, 1), state, zero).Count == 0); }));
        await Test("睡眠间断不会虚构十分钟骤降", () => Run(() => { var state = new AlertState(); AlertEngine.Evaluate(Sample(50), state, settings); Assert(AlertEngine.Evaluate(Sample(30, 120), state, settings).Count == 0); }));
        await Test("时钟回拨安全重建窗口", () => Run(() => { var state = new AlertState(); AlertEngine.Evaluate(Sample(50, 10), state, settings); Assert(AlertEngine.Evaluate(Sample(40, 1), state, settings).Count == 0); }));
        await Test("CSV 转义与公式注入防护", () => Run(() => { Assert(Reports.CsvCell("=cmd") == "\"'=cmd\""); Assert(Reports.CsvCell("a,\"b\"") == "\"a,\"\"b\"\"\""); Assert(Reports.CsvCell(-2) == "\"-2\""); }));
        await Test("跨日报表净变化只计一次且增长减释放一致", () => Run(() => { var points = new[] { Sample(50), Sample(48, 1), Sample(51, 2), Sample(45, 1441) }; var rows = Reports.Build(points, "日", TimeZoneInfo.Utc); Assert(Math.Abs(rows.Sum(x => x.NetGiB) - 5) < 0.0001); Assert(rows.All(x => Math.Abs(x.ObservedGrowthGiB - x.ObservedReleaseGiB - x.NetGiB) < 0.0001)); Assert(rows.Any(x => x.Coverage.Contains("间断"))); }));
        await Test("周一为周界 / 自然月聚合", () => Run(() => { var points = new[] { Sample(50), Sample(48, 1) }; Assert(Reports.Build(points, "周", TimeZoneInfo.Utc).Single().Period == "2026-11-09"); Assert(Reports.Build(points, "月", TimeZoneInfo.Utc).Single().Period == "2026-11-01"); }));
        await Test("扫描失败不报告假删除", () => Run(() => { var a = new Snapshot { Root = "C", VolumeId = "v", Status = "Complete", Buckets = [new("a", Category.Other, 100, 100, 1)] }; var b = new Snapshot { Root = "C", VolumeId = "v", Status = "Complete", Errors = 1, Buckets = [new("b", Category.Other, 20, 20, 1)] }; Assert(Reports.Compare(b, a).All(x => x.ChangeGiB == null)); }));
        await Test("负残差保持可见，不凑零", () => Run(() => { var s = new Snapshot { IsWholeVolume = true, After = new("v", "C", epoch, 100, 10), Buckets = [new("a", Category.Other, 110, 110, 1)] }; Assert(s.Residual == -20); }));
        await Test("设置原子保存及校验", () => Run(() => { var path = Path.Combine(work, "settings.json"); SettingsFile.Save(path, settings); Assert(SettingsFile.Load(path).ThresholdsGiB.SequenceEqual([20d, 11d, 3d])); var bad = new Settings { ThresholdsGiB = [double.NaN] }; bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; } Assert(rejected); }));
        await Test("SQLite 提醒状态重启持久化及完整性", () => Run(() => { var folder = Path.Combine(work, "db"); var repo = new Repository(folder, settings); Assert(repo.RecordSample(Sample(9.13)).Count == 1); var reopened = new Repository(folder, settings); Assert(reopened.RecordSample(Sample(9.13, 1)).Count == 0); Assert(reopened.RecentAlerts().Count == 1 && reopened.Samples(epoch.AddDays(-1)).Count == 2); Assert(reopened.IntegrityCheck() == "ok"); }));
        await Test("SQLite 实际加载引擎已修复 CVE-2025-6965", () => Run(() => { var repo = new Repository(Path.Combine(work, "engine-db"), settings); var version = repo.EngineVersion(); Assert(Version.Parse(version) >= new Version(3,50,2), "Unexpected old SQLite: " + version); Console.WriteLine("SQLite engine: " + version); }));
        await Test("相同采样幂等，不重复推进提醒状态", () => Run(() => { var repo = new Repository(Path.Combine(work, "duplicate-db"), settings); repo.RecordSample(Sample(50)); Assert(repo.RecordSample(Sample(2)).Count == 0); Assert(repo.RecentAlerts().Count == 0 && repo.Samples(epoch.AddDays(-1)).Count == 1); Assert(repo.RecordSample(Sample(2, 1)).Any(x => x.Kind == "LowSpace")); }));
        await Test("快照保留上限与历史状态存取", () => Run(() => { var repo = new Repository(Path.Combine(work, "retention-db"), new Settings { SnapshotRetention = 2 }); for (var i = 0; i < 3; i++) repo.SaveSnapshot(new Snapshot { Root = "fixture", VolumeId = "v", Finished = epoch.AddMinutes(i), Status = "Complete" }); Assert(repo.LatestSnapshots("fixture", 10).Count == 2); repo.SetState("scan:active", "interrupted-root"); Assert(new Repository(Path.Combine(work, "retention-db"), new Settings()).GetState<string>("scan:active") == "interrupted-root"); }));

        await Test("不可删除：页面文件 / Windows / 还原点 / 元数据 / 软件本体逐个判定", () => Run(() => {
            string? Why(string path) => Cleanup.NotDeletableReason(path, classifier.Classify(path).Category);
            Assert(Why(@"C:\pagefile.sys")!.Contains("页面文件")); Assert(Why(@"F:\hiberfil.sys") != null && Why(@"C:\swapfile.sys") != null);
            foreach (var p in new[] { @"C:\Windows\WinSxS\amd64_x\a.dll", @"C:\Windows\System32\DriverStore\FileRepository\n.sys", @"C:\Windows\Installer\1a2b.msi", @"C:\System Volume Information\{3808876b}", @"C:\$Extend\$UsnJrnl", @"C:\Recovery\WindowsRE\Winre.wim", @"C:\Tools\Editor\editor.exe", @"C:\ProgramData\Package Cache\x\setup.exe" })
                Assert(Why(p) != null, "应不可删除: " + p);
            foreach (var p in new[] { @"C:\Windows\Temp\x.tmp", @"C:\Windows\SoftwareDistribution\Download\abc\x.cab", @"D:\Documents\report.docx", @"C:\$Recycle.Bin\S-1\x.iso", @"C:\Downloads\pagefile.sys", @"C:\Tools\Editor\recordings\movie.mp4" })
                Assert(Why(p) == null, "不应标为不可删除: " + p);
        }));
        await Test("瘦身建议：单项分组且不建议不可删除文件", () => Run(() => {
            SlimAction? Act(string path, bool file = true) => Cleanup.Suggest(path, classifier.Classify(path).Category, file)?.Action;
            Assert(Act(@"C:\pagefile.sys") == null && Act(@"C:\Windows\WinSxS\x.dll") == null && Act(@"C:\Tools\Editor\editor.exe") == null);
            Assert(Act(@"C:\Users\u\AppData\Local\Temp\x.tmp") == SlimAction.Delete); Assert(Act(@"C:\Windows\SoftwareDistribution\Download\x.cab") == SlimAction.Delete);
            Assert(Act(@"E:\STEAM\steamapps\common\Game\data.pak") == SlimAction.Move); Assert(Act(@"C:\Program Files (x86)\Steam\steamapps\common\G\a.pak") == SlimAction.Move);
            Assert(Act(@"C:\Users\u\Downloads\setup.exe") == SlimAction.Delete && Act(@"C:\Users\u\Downloads\movie.mp4") == SlimAction.Move);
            Assert(Act(@"C:\Users\u\Documents\WeChat Files\wxid\FileStorage\a.mp4") == SlimAction.Move); Assert(Act(@"C:\Users\u\VMs\ubuntu.vhdx") == SlimAction.Move);
            Assert(Act(@"C:\Users\u\.codex\sessions\s.jsonl") == SlimAction.Move); Assert(Act(@"C:\ProgramData\Lenovo\Vantage\Cache\pkg.exe") == SlimAction.Delete);
            Assert(Act(@"C:\Users\u\AppData\Local\Google\Chrome\User Data\Default\Cache\f_1") == SlimAction.Delete); Assert(Act(@"C:\$Recycle.Bin\S-1\x.iso") == SlimAction.Delete);
            Assert(Act(@"C:\Users\u\AppData\Local\CrashDumps\app.dmp") == SlimAction.Delete); Assert(Act(@"C:\Users\u\AppData\Local\pnpm\store\x", false) != SlimAction.Move && Act(@"C:\Users\u\Saved Games\G\save.dat") == null);
        }));
        await Test("瘦身建议：按快照目录桶与大文件汇总、不重复计数", () => Run(() => {
            const long G = 1073741824;
            var s = new Snapshot { Root = @"C:\", Status = "Complete",
                Buckets = [new(@"C:\", Category.SpecialFiles, 16 * G, 16 * G, 2), new(@"C:\Users\u\AppData\Local", Category.Temporary, 3 * G, 3 * G, 900),
                    new(@"E:\STEAM\steamapps\common", Category.Games, 80 * G, 80 * G, 5000), new(@"C:\Users\u\Downloads", Category.Other, 5 * G, 5 * G, 40),
                    new(@"C:\Windows\WinSxS", Category.Windows, 9 * G, 9 * G, 9000), new(@"C:\Users\u\AppData\Local\Temp\small", Category.Temporary, 10L << 20, 10L << 20, 3)],
                LargestFiles = [new(@"C:\pagefile.sys", 12 * G, 12 * G, Category.SpecialFiles, 98, "x"), new(@"C:\Users\u\Downloads\win11.iso", 2 * G, 2 * G, Category.Other, 30, "x"),
                    new(@"C:\Users\u\Downloads\movie.mkv", G, G, Category.Other, 30, "x")] };
            var plan = Cleanup.Plan(s); var all = plan.Move.Concat(plan.Delete).ToList();
            Assert(!all.Any(x => x.Path.EndsWith("pagefile.sys") || x.Path == @"C:\" || x.Path.Contains("WinSxS")), "不可删除项进入了建议");
            Assert(plan.Move.Any(x => x.Path == @"E:\STEAM\steamapps\common" && x.AllocatedBytes == 80 * G));
            Assert(plan.Delete.Any(x => x.Path == @"C:\Users\u\AppData\Local" && x.Category == Category.Temporary));
            Assert(plan.Delete.Any(x => x.Path.EndsWith("win11.iso") && x.IsFile));
            Assert(plan.Move.Single(x => x.Path == @"C:\Users\u\Downloads").AllocatedBytes == 3 * G, "下载目录应扣除单独列出的安装镜像");
            Assert(!plan.Move.Any(x => x.Path.EndsWith("movie.mkv")), "与所在目录建议相同的大文件不应重复列出");
            Assert(!all.Any(x => x.Path.EndsWith("small")) && plan.MoveBytes == 83 * G && plan.DeleteBytes == 5 * G + (10L << 20), "子目录桶应并入同类上级桶");
        }));
        await Test("AI 提示词：覆盖全部建议、含禁止清单与确认规则、不列不可删除项", () => Run(() => {
            const long G = 1073741824;
            var s = new Snapshot { Root = @"C:\", Status = "Complete", Files = 1234, Finished = epoch, After = Sample(18),
                Buckets = [new(@"C:\", Category.SpecialFiles, 16 * G, 16 * G, 2), new(@"C:\Users\u\AppData\Local", Category.Temporary, 3 * G, 3 * G, 900),
                    new(@"C:\Users\u\.codex", Category.Development, 2 * G, 2 * G, 50), new(@"C:\Users\u\Documents\WeChat Files\wxid", Category.AppData, G, G, 30),
                    new(@"C:\Program Files (x86)\Steam\steamapps\common", Category.Games, 40 * G, 40 * G, 900), new(@"C:\Windows\Installer", Category.Windows, 5 * G, 5 * G, 90)],
                LargestFiles = [new(@"C:\pagefile.sys", 12 * G, 12 * G, Category.SpecialFiles, 98, "x"), new(@"C:\Windows\Installer\1a.msi", G, G, Category.Windows, 90, "x"),
                    new(@"C:\Users\u\Downloads\win11.iso", 2 * G, 2 * G, Category.Other, 30, "x")] };
            var plan = Cleanup.Plan(s); var prompt = Cleanup.BuildAiPrompt(s, plan, epoch);
            Assert(plan.Move.Count >= 3 && plan.Delete.Count >= 2, "样本计划应同时有转移和删除");
            foreach (var x in plan.Move.Concat(plan.Delete)) Assert(prompt.Contains($"`{x.Path}`"), "提示词缺少建议项: " + x.Path);
            var lists = prompt[prompt.IndexOf("## 二、")..prompt.IndexOf("## 四、")]; var deny = prompt[prompt.IndexOf("## 四、")..];
            foreach (var f in s.LargestFiles.Where(f => !Cleanup.IsDeletable(f.Path, f.Category))) { Assert(!lists.Contains(f.Path), "不可删除项出现在清单: " + f.Path); Assert(deny.Contains($"`{f.Path}`"), "禁止清单缺少: " + f.Path); }
            Assert(!lists.Contains(@"C:\Windows\Installer"), "Windows\\Installer 不应出现在转移/删除清单");
            Assert(deny.Contains("pagefile.sys") && deny.Contains("System Volume Information") && deny.Contains("Program Files"));
            Assert(prompt.Contains("必须得到我明确的“确认”") && prompt.Contains("回收站") && prompt.Contains("管理员提权"));
            Assert(prompt.Contains("Steam：设置 → 存储空间 → 移动") && prompt.Contains("mklink /J") && prompt.Contains("存储位置"), "转移方法应按类型自适应");
            Assert(prompt.Contains(@"D:\DiskWatch-Moved\C\Users\u\.codex"));
            var empty = Cleanup.BuildAiPrompt(new Snapshot { Root = @"F:\" }, new SlimPlan([], [])); Assert(empty.Contains("本次没有转移建议") && empty.Contains("本次没有删除建议"));
        }));

        var scanRoot = Path.Combine(work, "fixture"); Directory.CreateDirectory(Path.Combine(scanRoot, "project", "node_modules"));
        var original = Path.Combine(scanRoot, "original.bin"); File.WriteAllBytes(original, new byte[8192]);
        File.WriteAllBytes(Path.Combine(scanRoot, "project", "node_modules", "dependency.bin"), new byte[5000]);
        await Test("NTFS 元数据大小与分配空间", () => Run(() => { var m = NativeMetadata.Read(original, File.GetAttributes(original)); Assert(m.LogicalBytes == 8192 && m.AllocatedBytes >= 8192); Assert(NativeMetadata.Sample(scanRoot).FreeBytes > 0); }));
        await Test("硬链接去重且分类合计一致", async () =>
        {
            Assert(CreateHardLinkW(Path.Combine(scanRoot, "alias.bin"), original, 0), "CreateHardLink: " + Marshal.GetLastWin32Error());
            var s = await new Scanner(classifier, settings, "test").ScanAsync(scanRoot, null, CancellationToken.None);
            Assert(s.Status == "Complete" && s.Errors == 0, s.Coverage); Assert(s.Files == 2 && s.HardLinksSkipped == 1); Assert(s.LogicalBytes == 13192); Assert(s.Residual == null);
            var repo = new Repository(Path.Combine(work, "snapshots-db"), settings); repo.SaveSnapshot(s); Assert(repo.LatestSnapshots(scanRoot).Single().AllocatedBytes == s.AllocatedBytes);
        });
        await Test("目录 junction 不跟随、不重复计数", async () =>
        {
            var junction = Path.Combine(scanRoot, "linked"); var destination = Path.Combine(scanRoot, "project");
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Arguments = $"/c mklink /J \"{junction}\" \"{destination}\"";
            using var process = Process.Start(start)!; await process.WaitForExitAsync(); Assert(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            var s = await new Scanner(classifier, settings, "test").ScanAsync(scanRoot, null, CancellationToken.None);
            Assert(s.ReparsePointsSkipped == 1 && s.Files == 2 && s.LogicalBytes == 13192);
        });
        await Test("稀疏文件记录逻辑大小与分配空间差异", async () =>
        {
            var sparse = Path.Combine(scanRoot, "sparse.bin");
            using (var stream = new FileStream(sparse, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Assert(DeviceIoControl(stream.SafeFileHandle, 0x900C4, 0, 0, 0, 0, out _, 0), "SetSparse failed"); stream.SetLength(100L * 1024 * 1024);
            }
            var s = await new Scanner(classifier, settings, "test").ScanAsync(scanRoot, null, CancellationToken.None);
            Assert(s.LargestFiles.Single().LogicalBytes == 100L * 1024 * 1024 && s.LargestFiles.Single().AllocatedBytes < s.LargestFiles.Single().LogicalBytes);
        });
        await Test("取消扫描保存可识别的部分状态", async () => { using var cts = new CancellationTokenSource(); cts.Cancel(); var s = await new Scanner(classifier, settings, "test").ScanAsync(scanRoot, null, cts.Token); Assert(s.Status == "Cancelled" && !s.IsComparable); });
        await Test("排除目录显式标记覆盖不足", async () => { var excluded = new Settings { ExcludedPaths = [Path.Combine(scanRoot, "project")], ScanDelayMs = 0 }; var s = await new Scanner(classifier, excluded, "test").ScanAsync(scanRoot, null, CancellationToken.None); Assert(s.Errors > 0 && !s.IsComparable && s.Issues.Any(x => x.Reason.Contains("排除"))); });
        await Test("MFT 记录：fixup 还原、非常驻/稀疏/常驻大小、忽略备用数据流", () => Run(() =>
        {
            var named = Attr(0x80, resident: true, nameLength: 1, value: 99);
            var data = NonResident(allocated: 8192, logical: 5000, compressed: 4096, flags: 0);
            var r = MftRecord(links: 2, directory: false, named, data);
            Assert(r[510] == 0x01 && r[511] == 0x00, "样本应带更新序列号");
            MftRecordParser.ApplyFixups(r, r.Length);
            Assert(r[510] == 0xAB && r[511] == 0xCD && r[1022] == 0xEF && r[1023] == 0x12, "fixup 未正确还原或重复调用不安全");
            var p = MftRecordParser.Parse(r, r.Length);
            Assert(p.InUse && !p.IsDirectory && p.IsBaseRecord && p.Links == 2 && !p.NeedsFallback);
            Assert(p.LogicalBytes == 5000 && p.AllocatedBytes == 8192, $"非常驻大小错误 {p.LogicalBytes}/{p.AllocatedBytes}");
            var sparse = MftRecordParser.Parse(MftRecord(1, false, NonResident(8192, 5000, 4096, 0x8000)), 1024);
            Assert(sparse.LogicalBytes == 5000 && sparse.AllocatedBytes == 4096, "稀疏文件应取 CompressedSize");
            var small = MftRecordParser.Parse(MftRecord(1, false, Attr(0x80, true, 0, 123)), 1024);
            Assert(small.LogicalBytes == 123 && small.AllocatedBytes == 0 && !small.NeedsFallback, "常驻数据大小错误");
            var listOnly = MftRecordParser.Parse(MftRecord(1, false, Attr(0x20, true, 0, 32)), 1024);
            Assert(listOnly.NeedsFallback, "主数据流在扩展记录时应回退到常规 API");
            var dir = MftRecordParser.Parse(MftRecord(1, true), 1024);
            Assert(dir.IsDirectory && !dir.NeedsFallback);
            bool rejected = false; try { MftRecordParser.Parse(new byte[1024], 1024); } catch (InvalidDataException) { rejected = true; } Assert(rejected, "非 FILE 记录应拒绝");
        }));
        await Test("USN 枚举缓冲区解析（V2）", () => Run(() =>
        {
            var buffer = new byte[4096]; BitConverter.GetBytes(0x1234UL).CopyTo(buffer, 0); int at = 8;
            at = UsnRecord(buffer, at, 2, 0x0001000000000030UL, 0x0005000000000005UL, FileAttributes.Directory, "Users");
            at = UsnRecord(buffer, at, 2, 0x0002000000000040UL, 0x0001000000000030UL, FileAttributes.Archive, "数据.bin");
            var list = new List<UsnEntry>(); var next = UsnRecordParser.Parse(buffer, at, list);
            Assert(next == 0x1234 && list.Count == 2, $"解析数量 {list.Count}");
            Assert(list[0].Name == "Users" && (list[0].Attributes & FileAttributes.Directory) != 0 && (list[0].ParentReference & MftAggregator.IndexMask) == 5);
            Assert(list[1].Name == "数据.bin" && (list[1].FileReference & MftAggregator.IndexMask) == 0x40);
        }));
        await Test("MFT 汇总口径与目录遍历一致（合成条目）", () => Run(() =>
        {
            const long M = 1L << 20; var D = FileAttributes.Directory;
            var entries = new List<UsnEntry>
            {
                new(5, 5, D, "."),
                new(3, 5, FileAttributes.Hidden | FileAttributes.System, "$Volume"),
                new(30, 5, D, "Users"), new(31, 30, D, "u"), new(32, 31, D, "a"), new(33, 32, D, "b"), new(34, 33, D, "c"), new(35, 34, D, "d"),
                new(40, 35, FileAttributes.Archive, "big.bin"), new(41, 30, FileAttributes.Archive, "link.bin"), new(42, 5, FileAttributes.Archive, "root.txt"),
                new(50, 5, D | FileAttributes.ReparsePoint, "junction"), new(51, 50, FileAttributes.Archive, "hidden.bin"),
                new(60, 5, FileAttributes.Offline, "cloud.txt"), new(70, 999, FileAttributes.Archive, "orphan.bin"),
            };
            var sizes = new Dictionary<ulong, FileMetadata> { [40] = new("40", 60 * M, 60 * M, 1), [41] = new("41", 1000, 4096, 3), [42] = new("42", 10, 0, 1), [51] = new("51", 7 * M, 7 * M, 1), [70] = new("70", 1, 1, 1) };
            FileMetadata? Size(UsnEntry e, string path) => sizes[e.FileReference & MftAggregator.IndexMask];
            var s = new Snapshot { Root = @"X:\" };
            MftAggregator.Aggregate(s, @"X:\", @"X:\", entries, Size, classifier, new Settings(), null, CancellationToken.None);
            Assert(s.Files == 3, "文件数 " + s.Files); Assert(s.HardLinksSkipped == 2 && s.ReparsePointsSkipped == 1);
            Assert(s.Errors == 3 && s.Issues.Any(x => x.Path == @"X:\cloud.txt"));
            Assert(s.Buckets.Any(b => b.Path == @"X:\Users\u\a\b" && b.AllocatedBytes == 60 * M), "应按 4 层目录归桶");
            Assert(s.Buckets.Any(b => b.Path == @"X:\Users" && b.Files == 1) && s.Buckets.Any(b => b.Path == @"X:\" && b.Files == 1));
            Assert(s.LargestFiles.Single().Path == @"X:\Users\u\a\b\c\d\big.bin");
            Assert(!s.Buckets.Any(b => b.Path.Contains("junction")) && s.Buckets.Sum(b => b.AllocatedBytes) == 60 * M + 4096);
            var sub = new Snapshot { Root = @"X:\Users" };
            MftAggregator.Aggregate(sub, @"X:\", @"X:\Users", entries, Size, classifier, new Settings { ExcludedPaths = [@"X:\Users\u\a\b"] }, null, CancellationToken.None);
            Assert(sub.Files == 1 && sub.Issues.Any(x => x.Reason.Contains("排除")), "子目录与排除目录口径错误");
        }));
        if (MftScanner.IsElevated()) Skip("未提权时 MFT 快速扫描自动回退目录遍历", "当前已是管理员，无法验证未提权回退");
        else await Test("未提权时 MFT 快速扫描自动回退目录遍历", async () =>
        {
            Assert(MftScanner.TryScan(scanRoot, classifier, settings, "test", null, CancellationToken.None, out var note) == null && note!.Contains("管理员"), "未提权应返回原因: " + note);
            var s = await new Scanner(classifier, new Settings { FastScan = true, ScanDelayMs = 0 }, "test").ScanAsync(scanRoot, null, CancellationToken.None);
            Assert(s.Status == "Complete" && s.ScanMethod.Contains("目录遍历") && s.ScanMethod.Contains("未使用") && s.RuleVersion == "test", s.ScanMethod);
            var plain = await new Scanner(classifier, settings, "test").ScanAsync(scanRoot, null, CancellationToken.None);
            Assert(plain.ScanMethod == "目录遍历" && new Settings().FastScan == null && !new Settings().FastScanEnabled(false) && new Settings().FastScanEnabled(true), "未提权默认目录遍历；未设置时管理员默认开启快速扫描");
        });
        await Test("MFT：枚举不含根目录记录时按传入的完整根引用归属（真实 NTFS 行为）", () => Run(() =>
        {
            var D = FileAttributes.Directory; const ulong R = 0x0005000000000005UL;
            var entries = new List<UsnEntry>
            {
                new(0x000100000000001EUL, R, D, "Users"), new(0x0001000000000020UL, 0x000100000000001EUL, FileAttributes.Archive, "a.txt"),
                new(0x0001000000000021UL, R, FileAttributes.Archive, "root.txt"),
                new(0x0001000000000022UL, 0x0004000000000005UL, FileAttributes.Archive, "stale.txt"),
            };
            FileMetadata? Size(UsnEntry e, string path) => new(e.FileReference.ToString("X"), 10, 4096, 1);
            var s = new Snapshot { Root = @"X:\" };
            MftAggregator.Aggregate(s, @"X:\", @"X:\", entries, Size, classifier, new Settings(), null, CancellationToken.None, null, R);
            Assert(s.Files == 2, "文件数 " + s.Files);
            Assert(s.Issues.Count == 1 && s.Issues[0].Reason.Contains("0001000000000022"), "序列号不符的父引用应计为问题");
            var noRoot = new Snapshot { Root = @"X:\" };
            MftAggregator.Aggregate(noRoot, @"X:\", @"X:\", entries, Size, classifier, new Settings(), null, CancellationToken.None);
            Assert(noRoot.Files == 0, "未传根引用且无根记录时不应归属");
        }));
        if (!MftScanner.IsElevated() || Environment.GetEnvironmentVariable("DISKWATCH_MFT_TEST") != "1")
            Skip("MFT 快速扫描与目录遍历在夹具上等价", "需要以管理员身份运行测试并设置 DISKWATCH_MFT_TEST=1（会只读读取夹具所在卷的全部 MFT 索引）");
        else await Test("MFT 快速扫描与目录遍历在夹具上等价", async () =>
        {
            var traversal = await new Scanner(classifier, settings, "test").ScanAsync(scanRoot, null, CancellationToken.None);
            var mft = MftScanner.TryScan(scanRoot, classifier, settings, "test", null, CancellationToken.None, out var note);
            Assert(mft != null && mft.Status == "Complete", "MFT 扫描失败: " + note);
            Assert(mft!.Files == traversal.Files && mft.LogicalBytes == traversal.LogicalBytes, $"文件数/逻辑大小不一致 {mft.Files}/{traversal.Files} {mft.LogicalBytes}/{traversal.LogicalBytes}");
            Assert(mft.HardLinksSkipped == traversal.HardLinksSkipped && mft.ReparsePointsSkipped == traversal.ReparsePointsSkipped);
            Assert(Math.Abs(mft.AllocatedBytes - traversal.AllocatedBytes) <= 64 * 1024, $"分配空间差异过大 {mft.AllocatedBytes}/{traversal.AllocatedBytes}");
            Assert(mft.Buckets.Select(b => $"{b.Category}|{b.Path}|{b.Files}").Order().SequenceEqual(traversal.Buckets.Select(b => $"{b.Category}|{b.Path}|{b.Files}").Order()), "统计桶不一致");
            Assert(mft.LargestFiles.Select(f => f.Path).SequenceEqual(traversal.LargestFiles.Select(f => f.Path)));
        });
        await Test("扫描进度估算：百分比封顶、剩余时间稳定后才显示", () => Run(() =>
        {
            Assert(ScanEstimator.Fraction(50, 100) == 0.5 && ScanEstimator.Fraction(200, 100) == ScanEstimator.Cap && ScanEstimator.Fraction(-5, 100) == 0);
            Assert(ScanEstimator.Fraction(50, null) == null && ScanEstimator.Fraction(50, 0) == null, "没有总量时不应给百分比");
            Assert(ScanEstimator.RawRemaining(0.04, 0, TimeSpan.FromMinutes(1)) == null && ScanEstimator.RawRemaining(0.5, 0, TimeSpan.FromSeconds(3)) == null);
            Assert(Math.Abs(ScanEstimator.RawRemaining(0.5, 0, TimeSpan.FromSeconds(60))!.Value.TotalSeconds - 60) < 1e-9);
            var t0 = epoch; var e = new ScanEstimator(1000, t0);
            var idx = e.Update(0, true, t0.AddSeconds(1)); Assert(idx.Indeterminate && idx.Fraction == null && idx.Remaining == null && idx.Phase.Contains("文件目录"), "读取 MFT 目录阶段应为不确定进度");
            ScanEstimate last = idx;
            for (int t = 2; t <= 30; t += 2)
            {
                last = e.Update(10 * t, false, t0.AddSeconds(t));
                Assert(!last.Indeterminate && Math.Abs(last.Fraction!.Value - 0.01 * t) < 1e-9);
                if (last.Fraction <= ScanEstimator.MinFractionForEta) Assert(last.Remaining == null, "进度不足 5% 不应显示剩余时间");
            }
            Assert(last.Remaining is TimeSpan r && Math.Abs(r.TotalSeconds - 70) < 8, "匀速扫描剩余时间应约 70 秒：" + last.Remaining);
            Assert(last.Elapsed == TimeSpan.FromSeconds(30));
            var jumped = e.Update(900, false, t0.AddSeconds(32)); Assert(jumped.Remaining == null, "估算突变时应回到“估算中”");
            Assert(e.Update(5000, false, t0.AddSeconds(34)).Fraction == ScanEstimator.Cap, "未扫完前最多显示 99%");
            var done = e.Complete(t0.AddSeconds(40)); Assert(done.Fraction == 1 && done.Remaining == TimeSpan.Zero);
            var folder = new ScanEstimator(null, t0).Update(123, false, t0.AddSeconds(9)); Assert(folder.Indeterminate && folder.Fraction == null && folder.Remaining == null);
            Assert(ScanEstimator.Duration(TimeSpan.FromSeconds(65)) == "1:05" && ScanEstimator.Duration(TimeSpan.FromSeconds(3723)) == "1:02:03" && ScanEstimator.Duration(TimeSpan.FromSeconds(-3)) == "0:00");
            var longPath = @"C:\Users\u\AppData\Local\" + string.Concat(Enumerable.Repeat(@"very-long-folder\", 10)) + "target";
            var shortPath = ScanEstimator.Shorten(longPath, 60); Assert(shortPath.Length == 60 && shortPath.StartsWith(@"C:\Users") && shortPath.EndsWith("target") && shortPath.Contains('…'));
            Assert(ScanEstimator.Shorten(@"C:\x", 60) == @"C:\x");
        }));
        await Test("扫描范围勾选列表与设置互转、未找到项保留、空选择拦截", () => Run(() =>
        {
            const long G = 1L << 30;
            var drives = new[] { new DriveEntry(@"D:\", "数据", "NTFS", 600 * G, 100 * G, false), new DriveEntry(@"c:\", "", "NTFS", 200 * G, 11 * G, false), new DriveEntry(@"G:\", "U盘", "exFAT", 64 * G, 60 * G, true) };
            var existing = Path.Combine(work, "scope-folder"); Directory.CreateDirectory(existing);
            var missing = Path.Combine(work, "scope-missing");
            var saved = new List<string> { @"C:\", @"F:\", missing + "\\", existing, @"c:" };
            var items = ScanScope.Merge(drives, saved, Directory.Exists);
            Assert(items.Select(i => i.Path).SequenceEqual(new[] { @"C:\", @"D:\", @"F:\", @"G:\", missing, existing }), "顺序或规范化错误：" + string.Join("|", items.Select(i => i.Path)));
            Assert(items[0].Checked && items[0].Exists && !items[0].Removable && items[0].Description.Contains("本地磁盘") && items[0].Description.Contains("剩余"));
            Assert(!items[1].Checked && items[1].Description.StartsWith("数据 · NTFS"), "未保存的固定盘应列出但不勾选");
            Assert(items[2].Checked && !items[2].Exists && items[2].Removable && items[2].Description.Contains("未找到"), "已保存但不存在的盘应保留勾选并标“未找到”");
            Assert(!items[3].Checked && items[3].Description.Contains("可移动"), "可移动盘默认不勾选");
            Assert(items[4].Checked && !items[4].Exists && items[4].Description.Contains("未找到") && items[5].Checked && items[5].Exists && items[5].Removable);
            var roots = ScanScope.ToRoots(items);
            Assert(roots.SequenceEqual(new[] { @"C:\", @"F:\", missing, existing }), "转回 Roots 错误：" + string.Join("|", roots));
            Assert(ScanScope.ToRoots(ScanScope.Merge(drives, roots, Directory.Exists)).SequenceEqual(roots), "往返不一致");
            var path = Path.Combine(work, "scope-settings.json"); SettingsFile.Save(path, new Settings { Roots = roots, ExcludedPaths = ScanScope.NormalizeList([existing + "\\", existing, "  "]) });
            var loaded = SettingsFile.Load(path); Assert(loaded.Roots.SequenceEqual(roots) && loaded.ExcludedPaths.SequenceEqual(new[] { existing }), "settings.json 格式应保持为绝对路径列表");
            ScanScope.SelectAllFixed(items, drives);
            Assert(items[0].Checked && items[1].Checked && items[2].Checked && !items[3].Checked && items[4].Checked, "全选磁盘：固定盘勾选、可移动盘不勾、未找到项与文件夹不变");
            ScanScope.OnlySystemDrive(items);
            Assert(ScanScope.ToRoots(items).SequenceEqual(new[] { @"C:\" }), "只扫 C 盘应只剩 C:\\");
            Assert(!ScanScope.Add(items, @"c:", Directory.Exists) && ScanScope.Add(items, Path.Combine(work, "added"), Directory.Exists) && items[^1].Checked && items[^1].Description == "未找到");
            foreach (var i in items) i.Checked = false;
            Assert(ScanScope.Validate(ScanScope.ToRoots(items)) == ScanScope.EmptySelection && ScanScope.Validate([@"C:\"]) == null, "空选择应被拦截");
            Assert(ScanScope.Normalize("d:") == @"D:\" && ScanScope.Normalize(@"D:\a\b\") == @"D:\a\b" && ScanScope.IsDriveRoot("e:\\") && !ScanScope.IsDriveRoot(@"E:\x"));
        }));
        await Test("工具栏扫描范围：默认全部磁盘、单盘只扫该盘、扫描中不可改", () => Run(() =>
        {
            const long G = 1L << 30;
            var drives = new[] { new DriveEntry(@"D:\", "数据", "NTFS", 600 * G, 100 * G, false), new DriveEntry(@"c:\", "", "NTFS", 200 * G, 11 * G, false), new DriveEntry(@"E:\", "资料", "NTFS", 400 * G, 80 * G, false) };
            var folder = Path.Combine(work, "toolbar-scope-folder"); Directory.CreateDirectory(folder);
            var saved = new List<string> { @"C:\", @"D:\", folder };
            var options = ScanRangeChoice.Options(drives);
            Assert(ScanRangeChoice.DefaultKey == ScanRangeChoice.AllKey && options[0].Key == ScanRangeChoice.AllKey && options[0].Label == "全部磁盘" && options[0].ToString() == "全部磁盘" && options[1].ToString() == @"C:\", "第一项应是全部磁盘，显示文本应为标签而不是记录原文");
            Assert(options.Select(o => o.Key).SequenceEqual(new[] { ScanRangeChoice.AllKey, @"C:\", @"D:\", @"E:\" }), "磁盘应按盘符排序：" + string.Join("|", options.Select(o => o.Key)));
            Assert(ScanRangeChoice.Resolve(null, saved, drives).SequenceEqual(new[] { @"C:\", @"D:\", folder }) && ScanRangeChoice.Resolve(ScanRangeChoice.AllKey, saved, drives).SequenceEqual(ScanRangeChoice.Resolve("", saved, drives)), "默认/全部磁盘应按已保存 Roots 扫描，含文件夹");
            Assert(ScanRangeChoice.Resolve(@"d:\", saved, drives).SequenceEqual(new[] { @"D:\" }), "选单个盘只扫该盘");
            Assert(ScanRangeChoice.Resolve(@"E:\", saved, drives).SequenceEqual(new[] { @"E:\" }), "未勾进设置但在线的盘也可以单扫");
            var original = saved.ToList();
            ScanRangeChoice.Resolve(@"C:\", saved, drives);
            Assert(saved.SequenceEqual(original), "Resolve 不应改传入的已保存列表");
            Assert(ScanRangeChoice.Resolve(@"Z:\", saved, drives).SequenceEqual(ScanRangeChoice.Resolve(ScanRangeChoice.AllKey, saved, drives)), "不在线的盘回退为全部磁盘");
            Assert(ScanRangeChoice.CanChange(false) && !ScanRangeChoice.CanChange(true), "扫描中不可改范围");
            Assert(ScanRangeChoice.Retain(null, options) == ScanRangeChoice.AllKey && ScanRangeChoice.Retain(@"D:\", options) == @"D:\" && ScanRangeChoice.Retain(@"Z:\", options) == ScanRangeChoice.AllKey, "刷新后应保留原选择，盘不在了则回到全部磁盘");
            // 已经开始的扫描使用点击时解析好的列表；之后再改下拉不会改这份列表。
            var started = ScanRangeChoice.Resolve(@"C:\", saved, drives);
            Assert(started.SequenceEqual(new[] { @"C:\" }) && !ScanRangeChoice.CanChange(true) && ScanRangeChoice.Resolve(ScanRangeChoice.AllKey, saved, drives).Count == 3, "扫描中即使下拉值被改掉，已开始的目标列表保持不变");
        }));
        await Test("启动提权：每次请求一次、拒绝或失败时普通启动、不重复弹窗", () => Run(() =>
        {
            Assert(LaunchPolicy.Decide(false, false, []) == LaunchAction.RequestElevation, "普通权限首次启动应请求管理员权限");
            Assert(LaunchPolicy.Decide(true, false, []) == LaunchAction.StartDirect, "已是管理员不应再弹窗");
            Assert(LaunchPolicy.Decide(false, true, []) == LaunchAction.StartDirect, "已有实例运行时不弹窗，交给单实例提示");
            Assert(LaunchPolicy.Decide(false, false, [LaunchPolicy.ElevatedFlag]) == LaunchAction.StartDirect, "提权后的启动器不能再次请求（防循环）");
            Assert(LaunchPolicy.Decide(false, false, [LaunchPolicy.NoElevateFlag]) == LaunchAction.StartDirect);
            Assert(LaunchPolicy.FallbackReason(LaunchPolicy.ErrorCancelled).Contains("否") && LaunchPolicy.FallbackReason(5).Contains("5"));
            Assert(LaunchPolicy.ElevatedLauncherArguments(["--ui-review", "--evil"]) == "--elevated-launch --ui-review", "只透传白名单参数");
            Assert(LaunchPolicy.AppArguments(["--smoke-ui", LaunchPolicy.ElevatedFlag], true) == "--smoke-ui --elevation-declined" && LaunchPolicy.AppArguments([], false) == "");
            var legacy = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Roots\":[\"C:\\\\\"]}")!;
            Assert(legacy.FastScan == null && legacy.FastScanEnabled(true) && !legacy.FastScanEnabled(false), "旧 settings.json 没有 FastScan：管理员默认开启，普通权限不用");
            Assert(!new Settings { FastScan = false }.FastScanEnabled(true) && new Settings { FastScan = true }.FastScanEnabled(false), "用户明确设置优先");
            Assert(Labels.ElevationStatus(true, true).StartsWith("已获得管理员权限") && Labels.ElevationStatus(false, true).StartsWith("未获得管理员权限"));
        }));
        await Test("占用地图布局：面积成比例、不越界、不重叠、接近正方形", () => Run(() =>
        {
            var rng = new Random(7);
            foreach (var weights in new[] { new double[] { 6, 6, 4, 3, 2, 2, 1 }, Enumerable.Range(0, 50).Select(_ => rng.NextDouble() * 100 + 0.01).ToArray(), new double[] { 1000, 1, 0.5 } })
            {
                var rects = Treemap.Squarify(weights, 10, 20, 600, 400); double total = weights.Sum();
                Assert(rects.Count == weights.Length && rects.Select(r => r.Index).Distinct().Count() == weights.Length);
                foreach (var r in rects)
                {
                    Assert(Math.Abs(r.Area - weights[r.Index] / total * 240000) < 1e-6 * 240000, "面积不成比例");
                    Assert(r.X >= 10 - 1e-9 && r.Y >= 20 - 1e-9 && r.X + r.Width <= 610 + 1e-6 && r.Y + r.Height <= 420 + 1e-6, "越界");
                }
                for (int i = 0; i < rects.Count; i++) for (int j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i]; var b = rects[j];
                    var overlap = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
                    Assert(overlap < 1e-6, "方块重叠");
                }
            }
            var squares = Treemap.Squarify([1, 1, 1, 1], 0, 0, 100, 100);
            Assert(squares.All(r => Math.Abs(r.Width - 50) < 1e-9 && Math.Abs(r.Height - 50) < 1e-9), "等权重应为正方形");
            var classic = Treemap.Squarify([6, 6, 4, 3, 2, 2, 1], 0, 0, 6, 4);
            Assert(classic.Max(r => Math.Max(r.Width / r.Height, r.Height / r.Width)) < 3, "长宽比过大");
            Assert(Treemap.Squarify([0, -1, double.NaN], 0, 0, 10, 10).Count == 0 && Treemap.Squarify([1], 0, 0, 0, 10).Count == 0);
        }));
        await Test("占用地图树：总量守恒、主要分类与不可删除标记", () => Run(() =>
        {
            const long G = 1L << 30;
            var s = new Snapshot { Root = @"C:\", Buckets = [new(@"C:\", Category.SpecialFiles, 8 * G, 8 * G, 2), new(@"C:\Windows", Category.Windows, 30 * G, 30 * G, 9), new(@"C:\Windows\System32", Category.Windows, 10 * G, 10 * G, 9),
                new(@"C:\Users\u\Downloads", Category.Personal, 5 * G, 5 * G, 3), new(@"C:\Users\u\AppData\Local\Temp", Category.Temporary, 2 * G, 2 * G, 3), new(@"C:\Empty", Category.Other, 0, 0, 1)] };
            var root = OccupancyMap.Build(s);
            Assert(root.Bytes == s.Buckets.Sum(b => b.AllocatedBytes) && root.ProtectedReason == null, "根目录总量或标记错误");
            var windows = root.Children.Single(c => c.Name == "Windows"); Assert(windows.IsFolder && windows.Bytes == 40 * G && windows.ProtectedReason != null && windows.Category == Category.Windows);
            var rootFiles = root.Children.Single(c => !c.IsFolder); Assert(rootFiles.Name.Contains(Labels.CategoryName(Category.SpecialFiles)) && rootFiles.ProtectedReason != null);
            var users = root.Children.Single(c => c.Name == "Users"); Assert(users.Bytes == 7 * G && users.Category == Category.Personal && users.ProtectedReason == null);
            var downloads = users.Children.Single().Children.Single(c => c.Name == "Downloads"); Assert(downloads.ProtectedReason == null && downloads.Parent!.Parent == users);
            Assert(root.Children.Select(c => c.Bytes).SequenceEqual(root.Children.Select(c => c.Bytes).OrderByDescending(x => x)), "子项应按大小排序");
            Assert(!root.Children.Any(c => c.Name == "Empty"));
        }));
        await Test("文件级快照：序列化往返（中文 / 表情 / 稀疏 / 无时间）", () => Run(() =>
        {
            var index = new FileIndex { Root = @"C:\", Context = TestContext() };
            index.Add(@"C:\", "pagefile.sys", 8L << 30, 8L << 30, new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));
            index.Add(@"C:\Users\u\文档", "报告😀.docx", 12345, 16384, new DateTime(2026, 11, 13, 9, 30, 5, DateTimeKind.Utc));
            index.Add(@"C:\Users\u\文档", "报告😁.docx", 1, 0, default);
            index.Add(@"C:\Users\u\Videos", "sparse.vhdx", 50L << 30, 3L << 30, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            index.Add(@"C:\Windows\System32", "a.dll", 4095, 4096, new DateTime(2024, 5, 5, 5, 5, 5, DateTimeKind.Utc));
            var copy = FileIndex.Deserialize(index.Serialize());
            Assert(copy.Root == @"C:\" && copy.Files.Count == 5 && copy.Directories.Count == 4, "数量不一致");
            var a = index.Files.Select(f => (index.PathOf(f), f.LogicalBytes, f.AllocatedBytes, f.ModifiedSeconds)).OrderBy(x => x.Item1, StringComparer.Ordinal);
            var b = copy.Files.Select(f => (copy.PathOf(f), f.LogicalBytes, f.AllocatedBytes, f.ModifiedSeconds)).OrderBy(x => x.Item1, StringComparer.Ordinal);
            Assert(a.SequenceEqual(b), "往返后内容不一致");
            bool rejected = false; try { FileIndex.Deserialize([1, 2, 3, 4]); } catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException) { rejected = true; } Assert(rejected, "损坏数据应拒绝");
        }));
        if (args.Contains("--quick")) Skip("68 万文件基准", "本轮仅短测试，未运行大型基准");
        else await Test("文件级快照：68 万文件合成数据的体积与耗时", () => Run(() =>
        {
            var rnd = new Random(20261114);
            string[] words = ["Microsoft", "Windows", "System32", "WinSxS", "amd64", "Program Files", "Common Files", "AppData", "Local", "Roaming", "Packages", "Cache", "Temp", "drivers", "en-US", "zh-CN", "Assets", "Resources", "lib", "bin", "x64", "node_modules", "Steam", "steamapps", "common", "Logs", "Data", "config", "Google", "Chrome", "User Data", "Default", "Code Cache", "js", "wasm"];
            string[] exts = [".dll", ".exe", ".mui", ".cat", ".manifest", ".json", ".js", ".png", ".log", ".dat", ".pak", ".sys", ".xml", ".txt", ".etl", ".tmp"];
            FileIndex Build(bool hashy)
            {
                var index = new FileIndex { Root = @"C:\", Context = TestContext() };
                var dirs = new List<string> { @"C:\" };
                while (dirs.Count < 120_000)
                {
                    var parent = dirs[rnd.Next(dirs.Count)]; if (parent.Count(c => c == '\\') > 12) continue;
                    var name = hashy && rnd.Next(3) == 0 ? $"{words[rnd.Next(words.Length)]}_{rnd.Next():x8}{rnd.Next():x8}" : rnd.Next(4) == 0 ? $"{words[rnd.Next(words.Length)]}.{rnd.Next(1, 20)}.{rnd.Next(100, 30000)}.{rnd.Next(1000)}" : words[rnd.Next(words.Length)] + (rnd.Next(5) == 0 ? rnd.Next(100).ToString() : "");
                    dirs.Add(FileIndex.Join(parent, name));
                }
                var t0 = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                for (int i = 0; i < 680_000; i++)
                {
                    var stem = hashy && rnd.Next(3) == 0 ? $"{rnd.Next():x8}{rnd.Next():x8}{rnd.Next():x4}" : $"{words[rnd.Next(words.Length)].ToLowerInvariant()}-{words[rnd.Next(words.Length)].ToLowerInvariant()}-l{rnd.Next(1, 3)}-{rnd.Next(0, 3)}-{rnd.Next(0, 9)}";
                    long logical = rnd.Next(10) == 0 ? (long)(rnd.NextDouble() * 200_000_000) : rnd.Next(0, 300_000);
                    long allocated = logical <= 700 && rnd.Next(2) == 0 ? 0 : (logical + 4095) / 4096 * 4096;
                    index.Add(dirs[(int)Math.Min(dirs.Count - 1, Math.Abs(rnd.NextDouble() * rnd.NextDouble()) * dirs.Count)], stem + exts[rnd.Next(exts.Length)], logical, allocated, t0.AddSeconds(rnd.Next(0, 120_000_000)));
                }
                return index;
            }
            foreach (var hashy in new[] { false, true })
            {
                var index = Build(hashy);
                var sw = Stopwatch.StartNew(); var body = index.Serialize(); var write = sw.Elapsed; sw.Restart();
                var back = FileIndex.Deserialize(body); var read = sw.Elapsed;
                var raw = index.Files.Sum(f => (long)System.Text.Encoding.UTF8.GetByteCount(index.PathOf(f)) + 24);
                var line = $"SIZE 合成 C: 680,000 文件 / {index.Directories.Count:N0} 目录（{(hashy ? "含 1/3 随机哈希名，偏悲观" : "常见系统文件名，偏乐观")}）：压缩后 {body.Length / 1048576d:N1} MiB（{body.Length / 680_000d:N1} 字节/文件），未压缩路径+数值约 {raw / 1048576d:N0} MiB；写 {write.TotalSeconds:N1}s，读 {read.TotalSeconds:N1}s";
                output.Add(line); Console.WriteLine(line);
                Assert(back.Files.Count == 680_000 && back.AllocatedBytes == index.AllocatedBytes, "大数据往返不一致");
                Assert(body.Length < 40L << 20, "单份明细应小于 40 MiB");
            }
        }));
        FileIndex Idx(params (string Dir, string Name, long Bytes, int Day)[] rows)
        {
            var index = new FileIndex { Root = @"C:\", Context = TestContext() };
            foreach (var r in rows) index.Add(r.Dir, r.Name, r.Bytes, r.Bytes, new DateTime(2026, 11, r.Day, 0, 0, 0, DateTimeKind.Utc));
            return index;
        }
        await Test("变化对比：新增 / 删除 / 变更、改名算删除+新增、大小写不敏感", () => Run(() =>
        {
            const long M = 1L << 20;
            var before = Idx((@"C:\A", "keep.txt", 4096, 1), (@"C:\A", "grow.bin", 10 * M, 1), (@"C:\A", "touch.txt", 4096, 1), (@"C:\A", "old-name.log", 8192, 1), (@"C:\B", "gone.iso", 700 * M, 1), (@"C:\Case", "Mixed.TXT", 4096, 1));
            var after = Idx((@"C:\A", "keep.txt", 4096, 1), (@"C:\A", "grow.bin", 90 * M, 2), (@"C:\A", "touch.txt", 4096, 3), (@"C:\A", "new-name.log", 8192, 1), (@"C:\C", "new.vhdx", 400 * M, 2), (@"C:\case", "mixed.txt", 4096, 1));
            var changes = FileDiff.Compare(before, after);
            string K(FileChange c) => $"{c.Kind}:{c.Path}";
            var keys = changes.Select(K).Order().ToList();
            var expected = new[] { @"Added:C:\A\new-name.log", @"Added:C:\C\new.vhdx", @"Changed:C:\A\grow.bin", @"Changed:C:\A\touch.txt", @"Removed:C:\A\old-name.log", @"Removed:C:\B\gone.iso" }.Order().ToList();
            Assert(keys.SequenceEqual(expected), "对比结果：" + string.Join(", ", keys));
            Assert(changes.Single(c => c.Name == "grow.bin").Delta == 80 * M && changes.Single(c => c.Name == "touch.txt").Delta == 0, "变化量错误");
            Assert(changes.Sum(c => c.Delta) == after.AllocatedBytes - before.AllocatedBytes, "文件变化之和应等于总量差");
            Assert(FileDiff.Compare(after, after).Count == 0, "相同快照不应有变化");
        }));
        await Test("增长分级边界：显著 / 快速 / 轻微 / 新增、阈值可调", () => Run(() =>
        {
            const long M = 1L << 20; var t = new GrowthThresholds();
            Assert(Growth.Grade(0, 300 * M + 1, t) == GrowthGrade.Significant && Growth.Grade(10_000 * M, 10_300 * M + 1, t) == GrowthGrade.Significant, "超过 300 MiB 应为显著增长");
            Assert(Growth.Grade(1000 * M, 1300 * M, t) == GrowthGrade.Rapid, "恰好 300 MiB 不算显著，30% 算快速");
            Assert(Growth.Grade(4000 * M, 4300 * M, t) == GrowthGrade.Minor, "300 MiB 但只增长 7.5% 为轻微");
            Assert(Growth.Grade(100 * M, 150 * M, t) == GrowthGrade.Minor, "恰好 50 MiB 不算快速");
            Assert(Growth.Grade(100 * M, 150 * M + 1, t) == GrowthGrade.Rapid, "超过 50 MiB 且 50% 为快速");
            Assert(Growth.Grade(1000 * M, 1100 * M, t) == GrowthGrade.Minor, "恰好 10% 不算快速");
            Assert(Growth.Grade(1000 * M, 1100 * M + 2, t) == GrowthGrade.Rapid, "超过 10% 且超过 50 MiB 为快速");
            Assert(Growth.Grade(0, 60 * M, t) == GrowthGrade.Rapid, "原来为 0 视为无穷增长率");
            Assert(Growth.Grade(5, 6, t) == GrowthGrade.Minor && Growth.Grade(6, 5, t) == GrowthGrade.Shrink && Growth.Grade(5, 5, t) == GrowthGrade.None);
            Assert(Growth.Grade(new FileChange(@"C:\", "x", ChangeKind.Added, 0, 400 * M), t) == GrowthGrade.New && Growth.Grade(new FileChange(@"C:\", "x", ChangeKind.Removed, 9, 0), t) == GrowthGrade.Removed, "新增 / 删除优先");
            var custom = GrowthThresholds.From(new Settings { GrowthSignificantMiB = 100, GrowthRapidMiB = 10, GrowthRapidPercent = 50 });
            Assert(Growth.Grade(0, 100 * M + 1, custom) == GrowthGrade.Significant && Growth.Grade(100 * M, 120 * M, custom) == GrowthGrade.Minor && Growth.Grade(20 * M, 40 * M, custom) == GrowthGrade.Rapid, "自定义阈值");
            var bad = new Settings { GrowthSignificantMiB = 10, GrowthRapidMiB = 50 }; bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; } Assert(rejected, "显著阈值不大于快速阈值应拒绝");
            Assert(new Settings().FileDetailRetention == 2 && new Settings().GrowthSignificantMiB == 300 && new Settings().GrowthRapidMiB == 50 && new Settings().GrowthRapidPercent == 10, "默认值");
        }));
        await Test("变化目录树：任意层下钻总量守恒、汇总只计最深层", () => Run(() =>
        {
            const long M = 1L << 20; var rnd = new Random(7);
            var dirs = new[] { @"C:\", @"C:\Users", @"C:\Users\u", @"C:\Users\u\AppData\Local\Temp\a\b\c\d\e", @"C:\Users\u\Videos", @"C:\Windows\Temp", @"C:\Data\x", @"C:\Data\y" };
            var before = new FileIndex { Root = @"C:\", Context = TestContext() }; var after = new FileIndex { Root = @"C:\", Context = TestContext() };
            for (int i = 0; i < 3000; i++)
            {
                var d = dirs[rnd.Next(dirs.Length)]; var name = $"f{i}.bin"; long size = rnd.Next(0, 4) * 4096L + (rnd.Next(50) == 0 ? rnd.Next(1, 40) * M : 0);
                int kind = rnd.Next(10);
                if (kind != 0) before.Add(d, name, size, size, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
                if (kind != 1) { long s2 = kind == 2 ? size + rnd.Next(1, 30) * 4096L : kind == 3 ? size / 2 : size; after.Add(kind == 4 ? dirs[rnd.Next(dirs.Length)] : d, kind == 4 ? "moved-" + name : name, s2, s2, new DateTime(2026, 11, kind is 2 or 3 ? 2 : 1, 0, 0, 0, DateTimeKind.Utc)); }
            }
            after.Add(@"C:\Users\u\Videos", "movie.mkv", 900 * M, 900 * M, DateTime.UtcNow); after.Add(@"C:\Data\x", "dump.bin", 80 * M, 80 * M, DateTime.UtcNow);
            var changes = FileDiff.Compare(before, after);
            var tree = ChangeTree.Build(@"C:\", before, after, changes);
            Assert(tree.BeforeBytes == before.AllocatedBytes && tree.AfterBytes == after.AllocatedBytes, "根目录总量与快照不一致");
            Assert(tree.Added == changes.Count(c => c.Kind == ChangeKind.Added) && tree.Removed == changes.Count(c => c.Kind == ChangeKind.Removed) && tree.Changed == changes.Count(c => c.Kind == ChangeKind.Changed), "计数不一致");
            int visited = 0; int maxDepth = 0;
            void Check(ChangeNode n, int depth)
            {
                visited++; maxDepth = Math.Max(maxDepth, depth);
                Assert(n.BeforeBytes == n.OwnBefore + n.Children.Sum(c => c.BeforeBytes) && n.AfterBytes == n.OwnAfter + n.Children.Sum(c => c.AfterBytes), "父目录不等于本层 + 子目录：" + n.Path);
                Assert(n.Delta == n.Files.Sum(f => f.Delta) + n.Children.Sum(c => c.Delta), "下钻后变化量不守恒：" + n.Path);
                Assert(n.Added + n.Removed + n.Changed == n.Files.Count + n.Children.Sum(c => c.Added + c.Removed + c.Changed));
                Assert(n.Children.Select(c => c.Delta).SequenceEqual(n.Children.Select(c => c.Delta).OrderByDescending(x => x)), "子目录应按变化量排序");
                foreach (var c in n.Children) { Assert(c.Parent == n); Check(c, depth + 1); }
            }
            Check(tree, 0);
            Assert(maxDepth >= 10, "应支持超过 4 层的下钻，实际 " + maxDepth);
            Assert(ChangeTree.Find(tree, @"C:\Users\u\AppData\Local\Temp\a\b\c\d\e")?.Name == "e", "按路径定位");
            var summary = ChangeTree.Summarize(tree, new GrowthThresholds());
            Assert(summary.SignificantDirectories == 1, "显著增长目录应只计最深的 Videos：" + summary.SignificantDirectories);
            Assert(summary.Text.StartsWith($"新增 {tree.Added:N0}，变更 {tree.Changed:N0}，删除 {tree.Removed:N0}，发现 1 个显著增长目录，") && summary.Text.Contains("个快速增长目录"), summary.Text);
        }));
        await Test("文件级明细存储：保留最近 N 份、只对比同口径、0 份时清除", () => Run(() =>
        {
            var dir = Path.Combine(work, "fileindex-db"); var s = new Settings { FileDetailRetention = 2 };
            var repo = new Repository(dir, s);
            Snapshot Snap(int minute, string rule) => new() { VolumeId = "test-volume", CoverageKey = ScanCoverage.Key(new Settings()), Root = @"C:\", RuleVersion = rule, Finished = epoch.AddMinutes(minute), Status = "Complete" };
            var i1 = Idx((@"C:\A", "a.txt", 4096, 1)); var i2 = Idx((@"C:\A", "a.txt", 8192, 2)); var i3 = Idx((@"C:\A", "a.txt", 8192, 2), (@"C:\A", "b.txt", 4096, 3));
            var s1 = Snap(1, "r1"); var s2 = Snap(2, "r1"); var s3 = Snap(3, "r1");
            Assert(repo.SaveFileIndex(s1, i1) > 0); repo.SaveFileIndex(s2, i2); repo.SaveFileIndex(s3, i3);
            var list = repo.FileIndexes(@"C:\");
            Assert(list.Count == 2 && list[0].SnapshotId == s3.Id && list[1].SnapshotId == s2.Id && list[0].Files == 2, "应只保留最近 2 份");
            var pair = repo.ComparablePair(@"C:\"); Assert(pair is { } p && p.Before.SnapshotId == s2.Id && p.After.SnapshotId == s3.Id, "对比对错误");
            Assert(repo.LoadFileIndex(s3.Id)!.Files.Count == 2 && repo.LoadFileIndex(s1.Id) == null, "读取明细");
            repo.SaveFileIndex(Snap(4, "r1+mft"), i3);
            Assert(repo.ComparablePair(@"C:\") == null, "不同口径（MFT 与目录遍历）不应对比");
            s.FileDetailRetention = 0; Assert(repo.SaveFileIndex(Snap(5, "r1"), i3) == 0 && repo.FileIndexes(@"C:\").Count == 0, "0 份时应清除");
            Assert(repo.IntegrityCheck() == "ok");
        }));
        await Test("目录遍历收集文件级明细，与快照总量一致", async () =>
        {
            var s = await new Scanner(classifier, settings, "test").ScanAsync(scanRoot, null, CancellationToken.None);
            Assert(s.Status == "Complete" && s.FileDetails != null, "应收集明细");
            Assert(s.FileDetails!.Files.Count == s.Files && s.FileDetails.AllocatedBytes == s.AllocatedBytes, $"明细 {s.FileDetails.Files.Count}/{s.Files} 与快照不一致");
            Assert(s.FileDetails.Files.All(f => f.ModifiedSeconds > 0), "应记录修改时间");
            var off = await new Scanner(classifier, new Settings { ScanDelayMs = 0, FileDetailRetention = 0 }, "test").ScanAsync(scanRoot, null, CancellationToken.None);
            Assert(off.FileDetails == null, "保留份数为 0 时不收集");
            Assert(!System.Text.Json.JsonSerializer.Serialize(s).Contains("FileDetails"), "明细不应写入快照 JSON");
        });
        await Test("MFT：解析修改时间并收集文件级明细", () => Run(() =>
        {
            var si = new byte[0x60]; BitConverter.GetBytes(0x10u).CopyTo(si, 0); BitConverter.GetBytes(0x60u).CopyTo(si, 4); BitConverter.GetBytes(0x48u).CopyTo(si, 0x10); BitConverter.GetBytes((ushort)0x18).CopyTo(si, 0x14);
            var when = new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(); BitConverter.GetBytes(when).CopyTo(si, 0x18 + 8);
            var parsed = MftRecordParser.Parse(MftRecord(1, false, si, NonResident(8192, 5000, 0, 0)), 1024);
            Assert(parsed.ModifiedFileTime == when && parsed.LogicalBytes == 5000, "修改时间解析错误");
            var D = FileAttributes.Directory;
            var entries = new List<UsnEntry> { new(5, 5, D, "."), new(30, 5, D, "Users"), new(31, 30, D, "u"), new(40, 31, FileAttributes.Archive, "a.bin"), new(41, 5, FileAttributes.Archive, "b.txt") };
            FileMetadata? Size(UsnEntry e, string path) => new(e.Name, 100, 4096, 1, when);
            var snap = new Snapshot { Root = @"X:\" }; var details = new FileIndex { Root = @"X:\" };
            MftAggregator.Aggregate(snap, @"X:\", @"X:\", entries, Size, classifier, new Settings(), null, CancellationToken.None, details);
            Assert(details.Files.Count == 2 && details.AllocatedBytes == snap.AllocatedBytes && details.Files.All(f => f.ModifiedSeconds == when / 10_000_000), "MFT 明细不一致");
            Assert(details.Files.Select(details.PathOf).Order().SequenceEqual(new[] { @"X:\b.txt", @"X:\Users\u\a.bin" }), "MFT 明细路径");
        }));
        await FeatureTests();
        await SafetyTests();
        output.Add($"RESULT: {passed} passed, {failed} failed, {skipped} skipped; fixtures: {work}"); Console.WriteLine(output[^1]);
        File.WriteAllLines(Path.Combine(work, "TEST-RESULTS.txt"), output);
        return failed == 0 ? 0 : 1;
    }
    // 合成 1 KB MFT 文件记录：两个扇区末尾写入更新序列号 0x0001，原值 ABCD / EF12 存在更新序列数组中。
    private static byte[] MftRecord(int links, bool directory, params byte[][] attributes)
    {
        var r = new byte[1024]; "FILE"u8.CopyTo(r); BitConverter.GetBytes((ushort)0x30).CopyTo(r, 4); BitConverter.GetBytes((ushort)3).CopyTo(r, 6);
        BitConverter.GetBytes((ushort)links).CopyTo(r, 0x12); BitConverter.GetBytes((ushort)0x38).CopyTo(r, 0x14); BitConverter.GetBytes((ushort)(directory ? 3 : 1)).CopyTo(r, 0x16);
        int at = 0x38; foreach (var a in attributes) { a.CopyTo(r, at); at += a.Length; }
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(r, at); at += 8;
        BitConverter.GetBytes((uint)at).CopyTo(r, 0x18); BitConverter.GetBytes(1024u).CopyTo(r, 0x1C);
        r[0x30] = 0x01; r[0x31] = 0x00; r[0x32] = 0xAB; r[0x33] = 0xCD; r[0x34] = 0xEF; r[0x35] = 0x12;
        r[510] = 0x01; r[511] = 0x00; r[1022] = 0x01; r[1023] = 0x00;
        return r;
    }
    private static byte[] Attr(uint type, bool resident, int nameLength, int value)
    {
        int valueOffset = (24 + nameLength * 2 + 7) / 8 * 8;
        var a = new byte[(valueOffset + value + 7) / 8 * 8];
        BitConverter.GetBytes(type).CopyTo(a, 0); BitConverter.GetBytes((uint)a.Length).CopyTo(a, 4); a[8] = (byte)(resident ? 0 : 1); a[9] = (byte)nameLength;
        if (nameLength != 0) BitConverter.GetBytes((ushort)24).CopyTo(a, 10);
        BitConverter.GetBytes((uint)value).CopyTo(a, 0x10); BitConverter.GetBytes((ushort)valueOffset).CopyTo(a, 0x14); return a;
    }
    private static byte[] NonResident(long allocated, long logical, long compressed, ushort flags)
    {
        var a = new byte[0x48]; BitConverter.GetBytes(0x80u).CopyTo(a, 0); BitConverter.GetBytes(0x48u).CopyTo(a, 4); a[8] = 1; BitConverter.GetBytes(flags).CopyTo(a, 0x0C);
        BitConverter.GetBytes(allocated).CopyTo(a, 0x28); BitConverter.GetBytes(logical).CopyTo(a, 0x30); BitConverter.GetBytes(logical).CopyTo(a, 0x38); BitConverter.GetBytes(compressed).CopyTo(a, 0x40); return a;
    }
    private static int UsnRecord(byte[] buffer, int at, ushort major, ulong frn, ulong parent, FileAttributes attributes, string name)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(name); int length = (60 + bytes.Length + 7) / 8 * 8;
        BitConverter.GetBytes((uint)length).CopyTo(buffer, at); BitConverter.GetBytes(major).CopyTo(buffer, at + 4); BitConverter.GetBytes(frn).CopyTo(buffer, at + 8); BitConverter.GetBytes(parent).CopyTo(buffer, at + 16);
        BitConverter.GetBytes((uint)attributes).CopyTo(buffer, at + 52); BitConverter.GetBytes((ushort)bytes.Length).CopyTo(buffer, at + 56); BitConverter.GetBytes((ushort)60).CopyTo(buffer, at + 58); bytes.CopyTo(buffer, at + 60);
        return at + length;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string newName, string existing, nint security);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle file, uint code, nint input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
}
