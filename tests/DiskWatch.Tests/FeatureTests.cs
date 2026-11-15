using DiskWatch.Core;
using DiskWatch.Storage;
using DiskWatch.Windows;

namespace DiskWatch.Tests;

/// <summary>应用排行、塞满预测、周报摘要、骤降抓现场。全部用合成数据或测试自己的夹具目录 / 数据库。</summary>
internal static partial class Program
{
    private const long GiB = 1073741824L, MiB = 1048576L;
    private static readonly TimeZoneInfo Utc8 = TimeZoneInfo.CreateCustomTimeZone("test+8", TimeSpan.FromHours(8), "test+8", "test+8");

    private static List<VolumeSample> Series(DateTimeOffset start, double days, double stepMinutes, Func<double, double> freeGiB, string volume = "vol-f", string root = @"C:\")
    {
        var list = new List<VolumeSample>();
        for (double m = 0; m <= days * 1440; m += stepMinutes) list.Add(new(volume, root, start.AddMinutes(m), 200 * GiB, (long)(freeGiB(m / 1440) * GiB)));
        return list;
    }
    private static AppUsageRecord Rec(string id, DateTimeOffset finished, string rule = "r1", string status = "Partial", long errors = 3, params (string App, double GiB)[] apps) =>
        new(id, @"C:\", "vol-f", finished, status, errors, rule, "coverage-v2:x", "apps-v1:k", "目录遍历", apps.Select(a => new AppUsage(a.App, !a.App.StartsWith("C:"), (long)(a.GiB * GiB), 10)).ToList());

    private static async Task FeatureTests()
    {
        await Test("应用归属：内置规则（Edge 缓存 / 微信 / 联想 Vantage / Temp / Windows 更新 / Codex）与优先级", () => Run(() =>
        {
            var m = new AppMatcher();
            string A(string p, string? hint = null, bool file = false) => m.Attribute(p, hint, file).App;
            Assert(A(@"C:\Users\u\AppData\Local\Microsoft\Edge\User Data\Default\Cache\Cache_Data") == "Edge 缓存", "Edge 缓存");
            Assert(A(@"C:\Users\u\AppData\Local\Microsoft\Edge\User Data\Profile 1\Code Cache\js") == "Edge 缓存", "Edge 配置文件通配");
            Assert(A(@"C:\Users\u\AppData\Local\Microsoft\Edge\User Data\Default\IndexedDB") == "Edge 浏览器数据", "Edge 数据");
            Assert(A(@"C:\Program Files (x86)\Microsoft\EdgeUpdate\Download") == "Edge 更新", "Edge 更新");
            Assert(A(@"C:\Users\u\Documents\WeChat Files\wxid_1\FileStorage\File") == "微信", "文档里的微信仍算微信（个人文件夹只兜底）");
            Assert(A(@"C:\Users\u\Documents\xwechat_files\wxid_1\msg") == "微信" && A(@"C:\Users\u\AppData\Roaming\Tencent\WeChat\log") == "微信", "微信目录");
            Assert(A(@"C:\ProgramData\Lenovo\Vantage\Addins\x") == "联想 Vantage" && A(@"C:\Users\u\AppData\Local\Packages\E046963F.LenovoCompanion_k1h2ywk1493x8\LocalState") == "联想 Vantage", "联想 Vantage");
            Assert(A(@"C:\Program Files\Lenovo\PCManager\x") == "联想电脑管家" && A(@"C:\Program Files\Lenovo\Other") == "联想软件", "联想其他");
            Assert(A(@"C:\Users\u\AppData\Local\Temp\WinGet") == "Temp 临时文件" && A(@"C:\Windows\Temp") == "Temp 临时文件", "Temp");
            Assert(A(@"c:\USERS\u\appdata\local\TEMP\x") == "Temp 临时文件", "不区分大小写");
            Assert(A(@"C:\Windows\SoftwareDistribution\Download\abc") == "Windows 更新", "Windows 更新");
            Assert(A(@"C:\Windows\System32\DriverStore\FileRepository\lenovo.inf") == "Windows 系统", "Windows 目录里的驱动算 Windows");
            Assert(A(@"D:\Backup\Windows\SoftwareDistribution") == @"D:\Backup", "以 \\ 开头的规则只从卷根匹配");
            Assert(A(@"C:\Users\u\.codex\sessions\2026") == "Codex", "Codex");
            Assert(A(@"C:\Users\u\Downloads\Setup") == "下载文件夹", "个人文件夹兜底");
            Assert(A(@"C:\pagefile.sys", null, true) == "页面 / 休眠文件", "卷根特殊文件");
        }));
        await Test("应用归属：未识别按上层文件夹归组、安装记录、用户规则优先、规则版本", () => Run(() =>
        {
            var m = new AppMatcher();
            var foo = m.Attribute(@"C:\Users\u\AppData\Local\FooApp\cache2\entries");
            Assert(!foo.Known && foo.App == @"C:\Users\u\AppData\Local\FooApp", "AppData 下取第一层：" + foo.App);
            Assert(m.Attribute(@"C:\Users\u\AppData\Local\Packages\Some.App_123\LocalState").App == @"C:\Users\u\AppData\Local\Packages\Some.App_123", "应用包取包名");
            Assert(m.Attribute(@"C:\Program Files\FooSoft\bin").App == @"C:\Program Files\FooSoft", "Program Files 取第一层");
            Assert(m.Attribute(@"C:\Games2\x\y").App == @"C:\Games2", "其余取卷根第一层");
            Assert(m.Attribute(@"C:\Users\u\.foo\x").App == @"C:\Users\u\.foo", "用户目录取第一层");
            Assert(m.Attribute(@"C:\stray.log", null, true) is { Known: false, App: @"C:\（根目录文件）" }, "卷根普通文件");
            Assert(m.Attribute(@"C:\Tools\Editor\bin", "Editor") is { Known: true, App: "Editor" }, "安装记录命中");
            var custom = new AppMatcher([new(@"Tools\Editor", "我的编辑器"), new("Temp", "自定义临时")]);
            Assert(custom.Attribute(@"C:\Tools\Editor\bin", "Editor").App == "我的编辑器", "用户规则优先于安装记录");
            Assert(custom.Attribute(@"C:\Users\u\AppData\Local\Temp").App == "自定义临时", "用户规则优先于内置");
            Assert(custom.Attribute(@"C:\Users\u\AppData\Local\Microsoft\Edge\User Data\Default\Cache").App == "Edge 缓存", "未命中用户规则时用内置表");
            Assert(AppAttribution.RulesKey(new Settings()) != AppAttribution.RulesKey(new Settings { AppRules = [new("X", "Y")] }), "用户规则变化要换版本");
            bool rejected = false; try { new Settings { AppRules = [new("", "Y")] }.Validate(); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "空规则应被拒绝");
            var c = new AppUsageCollector();
            c.Add(@"C:\Users\u\AppData\Local\Temp", "a.tmp", 4096); c.Add(@"C:\Users\u\AppData\Local\Temp", "b.tmp", 8192); c.Add(@"C:\", "pagefile.sys", 1 * GiB); c.Add(@"C:\Odd", "x", 100);
            var list = c.ToList();
            Assert(list.Sum(x => x.AllocatedBytes) == 4096 + 8192 + GiB + 100 && list[0].App == "页面 / 休眠文件" && list.Single(x => x.App == "Temp 临时文件").Files == 2, "累加");
            var merged = c.ToList(2);
            Assert(merged.Count == 2 && merged.Sum(x => x.AllocatedBytes) == list.Sum(x => x.AllocatedBytes), "超过上限合并后总量不变");
        }));
        await Test("应用归属：目录遍历扫描收集应用占用，总量与快照一致并单独保存", async () =>
        {
            var root = Path.Combine(work, "apps-scan");
            void Put(string rel, int bytes) { var p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, new byte[bytes]); }
            Put(@"Users\u\AppData\Local\Microsoft\Edge\User Data\Default\Cache\data_1", 70_000);
            Put(@"Users\u\AppData\Local\Temp\setup.log", 9_000);
            Put(@"Users\u\Documents\WeChat Files\wxid\a.jpg", 30_000);
            Put(@"Misc\readme.txt", 100);
            var cls = new Classifier(new ClassificationContext());
            var s = await new Scanner(cls, new Settings { ScanDelayMs = 0, FastScan = false }, "test").ScanAsync(root, null, CancellationToken.None);
            Assert(s.Status == "Complete" && s.Apps != null, "应收集应用占用");
            Assert(s.Apps!.Sum(a => a.AllocatedBytes) == s.AllocatedBytes && s.Apps.Sum(a => a.Files) == s.Files, "应用合计应等于快照总量");
            Assert(s.Apps.Any(a => a.App == "Edge 缓存") && s.Apps.Any(a => a.App == "Temp 临时文件") && s.Apps.Any(a => a.App == "微信"), "应识别 Edge 缓存 / Temp / 微信");
            Assert(s.AppRulesKey == AppAttribution.RulesKey(new Settings()), "记录规则版本");
            Assert(!System.Text.Json.JsonSerializer.Serialize(s).Contains("\"Apps\""), "应用占用不写入快照 JSON");
            var repo = new Repository(Path.Combine(work, "apps-db"), new Settings { SnapshotRetention = 2 });
            repo.SaveSnapshot(s);
            var history = repo.AppUsageHistory(s.Root);
            Assert(history.Count == 1 && history[0].Total == s.AllocatedBytes && history[0].SnapshotId == s.Id, "应用占用单独保存");
            for (int i = 0; i < 2; i++) { s.Id = Guid.NewGuid().ToString("N"); s.Finished = s.Finished.AddMinutes(1); repo.SaveSnapshot(s); }
            Assert(repo.AppUsageHistory(s.Root).Count == 2, "随快照保留份数裁剪");
            var cancelled = new Snapshot { Root = s.Root, VolumeId = s.VolumeId, Status = "Cancelled", Finished = s.Finished.AddMinutes(1), Apps = [new("X", true, 1, 1)] };
            repo.SaveSnapshot(cancelled); Assert(repo.AppUsageHistory(s.Root).All(h => h.SnapshotId != cancelled.Id), "取消的扫描不进排行");
            Assert(repo.IntegrityCheck() == "ok");
        });
        await Test("应用排行：增长、占比、同口径才对比、按天 / 周 / 月选基线", () => Run(() =>
        {
            var t0 = epoch;
            var r1 = Rec("1", t0, apps: [("微信", 10), ("Temp 临时文件", 1)]);
            var r2 = Rec("2", t0.AddDays(1), apps: [("微信", 12), ("Temp 临时文件", 0.5), ("Edge 缓存", 0.5)]);
            var r3 = Rec("3", t0.AddDays(8), apps: [("微信", 15), ("Temp 临时文件", 0.5), ("Edge 缓存", 1.5), (@"C:\Odd", 0.25)]);
            var mft = Rec("4", t0.AddDays(2), rule: "r1+mft", apps: [("微信", 1)]);
            var cancelled = Rec("5", t0.AddDays(7), status: "Cancelled", apps: [("微信", 1)]);
            var history = new[] { r3, cancelled, mft, r2, r1 };
            var r = AppRanking.Build(r2, AppRanking.Baseline(history, r2, null));
            Assert(r.Compared && r.Baseline!.SnapshotId == "1", "上一次可比扫描");
            Assert(r.Rows[0].App == "微信" && r.Rows[0].GrowthBytes == 2 * GiB && r.Rows[1].App == "Edge 缓存" && r.Rows[1].BeforeBytes == 0, "按增长排序");
            Assert(Math.Abs(r.Rows[0].Share - 2 / 2.5) < 1e-9 && r.GrowthTotal == (long)(2.5 * GiB) && r.NetBytes == 2 * GiB, "占比 = 增长 / 增长合计；净变化");
            Assert(r.Rows.Single(x => x.App == "Temp 临时文件").GrowthBytes == -GiB / 2 && r.Rows.Single(x => x.App == "Temp 临时文件").Share == 0, "减少的不计占比");
            Assert(r.Note.Contains("读取失败"), "有读取失败时注明");
            Assert(AppRanking.Baseline(history, r3, null)!.SnapshotId == "2", "跳过取消的和 MFT 口径的扫描");
            Assert(AppRanking.Baseline(history, r3, TimeSpan.FromDays(7))!.SnapshotId == "2", "近 7 天：至少 7 天前最近的一次");
            Assert(AppRanking.Baseline(history, r3, TimeSpan.FromDays(1))!.SnapshotId == "2", "近 1 天：没有 1 天内的就取更早最近的一次");
            var month = AppRanking.Build(r3, AppRanking.Baseline(history, r3, TimeSpan.FromDays(30)), "近 30 天");
            Assert(month.Baseline!.SnapshotId == "1" && month.Note.Contains("实际只覆盖"), "近 30 天：没有那么早的就取最早一次并注明实际跨度");
            Assert(AppRanking.Incomparable(r3, mft) != null && AppRanking.Incomparable(r3, r3 with { VolumeId = "other" }) != null && AppRanking.Incomparable(r3, r3 with { RulesKey = "apps-v1:other" }) != null, "不同方式 / 卷 / 规则不对比");
            Assert(AppRanking.Baseline(history, mft, null) == null, "MFT 没有同口径基线");
            var first = AppRanking.Build(r1, null);
            Assert(!first.Compared && first.Rows[0].App == "微信" && first.Rows.All(x => x.GrowthBytes == null) && Math.Abs(first.Rows.Sum(x => x.Share) - 1) < 1e-9, "没有基线时按当前占用排序");
            Assert(AppRanking.TopGrowth(r, 2) == "微信 +2.00 GiB、Edge 缓存 +512.0 MiB", AppRanking.TopGrowth(r, 2));
        }));
        await Test("塞满预测：持续增长给出天数和日期", () => Run(() =>
        {
            var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(8));
            var s = Series(start, 10, 10, d => 100 - 2 * d);
            var now = s[^1].Time;
            var f = Forecast.Estimate(s, 7, now, Utc8);
            Assert(f.Kind == ForecastKind.Growing && !f.LowConfidence, "应为持续增长：" + f.Text);
            Assert(Math.Abs(f.GrowthBytesPerDay!.Value - 2 * GiB) < 0.02 * GiB, $"速度 {f.GrowthBytesPerDay / GiB:N3}");
            Assert(Math.Abs(f.DaysToFull!.Value - 40) < 1, $"剩余 80 GiB / 2 GiB 每天 ≈ 40 天，实际 {f.DaysToFull:N2}");
            Assert(f.FullDate == TimeZoneInfo.ConvertTime(now, Utc8).DateTime.AddDays(f.DaysToFull.Value).Date && f.Text.StartsWith("按近 7 天速度约 ") && f.Text.Contains("天塞满（到 2026-12-"), f.Text);
            var m = Forecast.Estimate(s, 30, now, Utc8);
            Assert(m.Kind == ForecastKind.Growing && m.Text.Contains("实际只有 10 天数据"), "30 天窗口注明实际天数：" + m.Text);
            Assert(Forecast.Describe([f, m]).Contains("；按近 30 天速度"), "30 天数据多于 7 天时另列");
        }));
        await Test("塞满预测：平稳 / 用量减少 → 近期没有持续增长", () => Run(() =>
        {
            var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(8));
            var rnd = new Random(7);
            var flat = Series(start, 10, 10, d => 50 + (rnd.NextDouble() - 0.5) * 0.1);
            var f = Forecast.Estimate(flat, 7, flat[^1].Time, Utc8);
            Assert(f.Kind == ForecastKind.NoGrowth && f.Text == "近 7 天：近期没有持续增长" && f.Short == "近期没有持续增长", f.Text);
            var freeing = Series(start, 10, 10, d => 50 + d);
            Assert(Forecast.Estimate(freeing, 7, freeing[^1].Time, Utc8).Kind == ForecastKind.NoGrowth, "剩余空间增加不预测");
            var slow = Series(start, 10, 10, d => 150 - d * 0.2);
            var sf = Forecast.Estimate(slow, 7, slow[^1].Time, Utc8);
            Assert(sf.Kind == ForecastKind.Growing && sf.DaysToFull > 600, "每天 0.2 GiB 仍给出远期日期：" + sf.Text);
        }));
        await Test("塞满预测：数据不足不预测，不足 3 天标低可信度", () => Run(() =>
        {
            var start = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.FromHours(8));
            Assert(Forecast.Estimate([], 7, start, Utc8).Kind == ForecastKind.Insufficient, "没有样本");
            var half = Series(start, 0.5, 1, d => 100 - 5 * d);
            var h = Forecast.Estimate(half, 7, half[^1].Time, Utc8);
            Assert(h.Kind == ForecastKind.Insufficient && h.Text == "近 7 天：记录不足 1 天，暂不预测" && h.DaysToFull == null, h.Text);
            var shortSpan = Series(start, 2.5, 5, d => 100 - 4 * d);
            var s = Forecast.Estimate(shortSpan, 7, shortSpan[^1].Time, Utc8);
            Assert(s.Kind == ForecastKind.Growing && s.LowConfidence && s.Text.Contains("数据不足 3 天") && s.Short.EndsWith("（参考）"), s.Text);
            var old = Series(start.AddDays(-40), 3, 10, d => 100 - 4 * d);
            Assert(Forecast.Estimate(old, 7, start, Utc8).Kind == ForecastKind.Insufficient, "窗口外的旧样本不使用");
        }));
        await Test("塞满预测：噪声、日内波动和临时大文件下仍稳健", () => Run(() =>
        {
            var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(8));
            var rnd = new Random(42);
            var s = Series(start, 14, 5, d =>
            {
                double v = 120 - 1.5 * d - 1.5 * Math.Sin(d * 2 * Math.PI) + (rnd.NextDouble() - 0.5) * 0.6;
                if (d is > 3.3 and < 3.6) v -= 25; // 某天临时解压 25 GiB，随后删除
                if (d is > 9.1 and < 9.2) v -= 18;
                return v;
            });
            var f = Forecast.Estimate(s, 7, s[^1].Time, Utc8);
            var m = Forecast.Estimate(s, 30, s[^1].Time, Utc8);
            Assert(f.Kind == ForecastKind.Growing && Math.Abs(f.GrowthBytesPerDay!.Value / GiB - 1.5) < 0.35, $"7 天速度 {f.GrowthBytesPerDay / GiB:N2}");
            Assert(m.Kind == ForecastKind.Growing && Math.Abs(m.GrowthBytesPerDay!.Value / GiB - 1.5) < 0.35, $"30 天速度 {m.GrowthBytesPerDay / GiB:N2}");
            var byVolume = Forecast.ForVolumes(s.Concat(Series(start, 14, 30, d => 300, "vol-d", @"D:\")), s[^1].Time, Utc8);
            Assert(byVolume.Count == 2 && byVolume[@"C:\"][0].Kind == ForecastKind.Growing && byVolume[@"D:\"][0].Kind == ForecastKind.NoGrowth, "按卷分别预测");
        }));
        await Test("周报摘要：各盘变化、增长最多的应用、本周提醒、塞满预测", () => Run(() =>
        {
            var monday = new DateTime(2026, 11, 9);
            var from = new DateTimeOffset(monday, TimeSpan.FromHours(8));
            Assert(WeeklySummary.WeekStart(new DateTimeOffset(2026, 11, 15, 23, 0, 0, TimeSpan.FromHours(8)), Utc8) == monday && WeeklySummary.WeekStart(from.AddMinutes(30), Utc8) == monday, "周一为周界");
            var c = Series(from.AddDays(-20), 25.5, 30, d => 80 - 0.6 * d);
            var r1 = Rec("a", from.AddDays(1), apps: [("微信", 10), ("Temp 临时文件", 2), (@"C:\Odd", 1)]);
            var r2 = Rec("b", from.AddDays(5), apps: [("微信", 12.5), ("Temp 临时文件", 1), (@"C:\Odd", 1.5)]);
            var apps = AppRanking.Build(r2, r1);
            var alerts = new List<Alert>
            {
                new("1", from.AddDays(2), "vol-f", @"C:\", "SuddenDrop", @"C:\ 在 8.0 分钟内减少 2.50 GiB 可用空间。"),
                new("2", from.AddDays(-3), "vol-f", @"C:\", "LowSpace", "上周的低空间提醒"),
            };
            var generated = from.AddDays(5.5);
            var forecast = new[] { Forecast.Estimate(c, 7, generated, Utc8), Forecast.Estimate(c, 30, generated, Utc8) };
            var input = new WeeklySummaryInput(monday, generated, [new(@"C:\", c, apps, forecast), new(@"D:\", [], null, null)], alerts, Utc8);
            var report = WeeklySummary.Compose(input);
            var text = report.Text;
            foreach (var expect in new[] { "2026-11-09（周一）至 2026-11-15（周日）", "本周还没过完", "数据来源：容量记录、目录扫描和提醒记录。", @"C:\：已用 +", @"D:\：本周没有容量记录",
                "1. 微信 +2.50 GiB", "（未识别目录）", "本周共 1 条：空间骤降 1 条", "减少 2.50 GiB", "## 塞满预测", "天塞满（到 ", @"D:\：没有足够的记录" })
                Assert(text.Contains(expect), "周报缺少：" + expect + "\n" + text);
            Assert(!text.Contains("不会发送到微信") && !text.Contains("只保存在本机") && !text.Contains("其他任何地方") && !text.Contains("不会推送到"), "导出文案不应再提推送或只在本机");
            Assert(!text.Contains("上周的低空间提醒") && !text.Contains("Temp 临时文件 +"), "不含本周以外的提醒和减少项");
            Assert(report.Partial && report.Drives.Count == 2 && report.Drives[1].Period == null && report.Drives[1].Days.Count == 7, "D 盘无采样也应有 7 天空槽");
            Assert(report.Drives[0].Days.Count(day => day.UsedBytes != null) > 0 && report.Drives[1].Days.All(day => day.UsedBytes == null && day.Samples == 0), "没有采样的天不得用 0 充数");
            Assert(report.Kpis.Any(k => k.Id == "net" && k.Value.StartsWith("+")) && report.Kpis.Any(k => k.Id == "alerts" && k.Value.StartsWith("1")), "有数据时应给出净变化和提醒数");
            Assert(report.TopApps.Count > 0 && report.TopApps[0].App == "微信", "增长应用应结构化");
            Assert(report.Alerts.Count == 1, "只计入本周提醒");
            var doneReport = WeeklySummary.Compose(new(monday, from.AddDays(8), [new(@"C:\", [], null, null)], [], Utc8));
            var done = doneReport.Text;
            Assert(!done.Contains("本周还没过完") && done.Contains("本周没有触发提醒") && done.Contains("本周没有可对比的两次扫描"), "空周也能生成");
            Assert(doneReport.Kpis.Single(k => k.Id == "net").Value == "—" && doneReport.Kpis.Single(k => k.Id == "growth").Value == "—" && !WeeklySummary.HasCapacityData(doneReport), "空周指标用破折号，不用 0 充数");
            var thisWeek = new DateTime(2026, 11, 16);
            var now = new DateTimeOffset(thisWeek.AddDays(3), TimeSpan.FromHours(8));
            Assert(WeeklySummary.PreferredWeekStart(now, Utc8, c) == new DateTime(2026, 11, 9), "上周有采样时默认上周");
            Assert(WeeklySummary.PreferredWeekStart(now, Utc8, []) == thisWeek, "上周没有采样时默认本周");
        }));
        await Test("骤降抓现场：默认关闭、冷却 30 分钟、扫描锁、只扫报警的盘", () => Run(() =>
        {
            Assert(!new Settings().AutoCaptureOnSuddenDrop && new Settings().AutoCaptureCooldownMinutes == 30, "默认关闭，间隔 30 分钟");
            var path = Path.Combine(work, "old-settings.json"); File.WriteAllText(path, "{\"Roots\":[\"C:\\\\\"]}");
            var loaded = SettingsFile.Load(path); Assert(!loaded.AutoCaptureOnSuddenDrop && loaded.AppRules.Count == 0, "旧设置文件没有该字段时保持关闭");
            var state = new AlertState(); var on = new Settings { AutoCaptureOnSuddenDrop = true };
            AlertEngine.Evaluate(Sample(50), state, on); AlertEngine.Evaluate(Sample(49, 1), state, on); var alert = AlertEngine.Evaluate(Sample(47, 2), state, on).Single(a => a.Kind == "SuddenDrop");
            var now = alert.Time;
            Assert(AutoCapture.Decide(new Settings(), alert, now, null, false, false) != null, "关闭时不扫");
            Assert(AutoCapture.Decide(on, alert, now, null, false, false) == null, "开启且骤降时扫描");
            Assert(AutoCapture.Decide(on, alert with { Kind = "LowSpace" }, now, null, false, false) != null, "低空间提醒不触发");
            Assert(AutoCapture.Decide(on, alert, now, null, true, false)!.Contains("已有扫描"), "尊重扫描锁");
            Assert(AutoCapture.Decide(on, alert, now, null, false, true) != null, "暂停时不扫");
            Assert(AutoCapture.Decide(on, alert, now, now.AddMinutes(-29), false, false)!.Contains("30 分钟"), "30 分钟内不重复");
            Assert(AutoCapture.Decide(on, alert, now, now.AddMinutes(-30), false, false) == null, "满 30 分钟后可以再扫");
            var roots = AutoCapture.RootsFor(@"C:\", [@"C:\", @"D:\", @"c:\"]);
            Assert(roots.Count == 1 && roots[0] == @"C:\", "只扫报警的盘，去重");
            Assert(AutoCapture.RootsFor(@"E:\", [@"C:\", @"D:\"]).Count == 0, "不在扫描范围的盘不扫");
            bool rejected = false; try { new Settings { AutoCaptureCooldownMinutes = 5 }.Validate(); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "间隔过短应被拒绝");
        }));
        await Test("骤降抓现场：结果说明增长的应用和文件夹，没有基线时如实说明", () => Run(() =>
        {
            Snapshot Snap(string id, int minutes, string status, params (string Path, long Bytes)[] buckets) => new()
            { Id = id, Root = @"C:\", VolumeId = "vol-f", RuleVersion = "r1", CoverageKey = "coverage-v2:x", Status = status, Finished = epoch.AddMinutes(minutes), Buckets = buckets.Select(b => new Bucket(b.Path, Category.Other, b.Bytes, b.Bytes, 1)).ToList() };
            var before = Snap("p", 0, "Partial", (@"C:\Users\u\AppData\Local\Temp", 1 * GiB), (@"C:\Windows", 20 * GiB));
            var after = Snap("c", 60, "Partial", (@"C:\Users\u\AppData\Local\Temp", 3 * GiB), (@"C:\Windows", 20 * GiB), (@"C:\New", 512 * MiB));
            var apps = AppRanking.Build(Rec("c", epoch.AddMinutes(60), apps: [("Temp 临时文件", 3)]), Rec("p", epoch, apps: [("Temp 临时文件", 1)]));
            var text = AutoCapture.Report("C:\\ 在 5 分钟内减少 3 GiB", after, before, apps);
            Assert(text.Contains("Temp 临时文件 +2.00 GiB") && text.Contains(@"C:\Users\u\AppData\Local\Temp +2.00 GiB") && text.Contains(@"C:\New +512.0 MiB") && !text.Contains(@"C:\Windows +"), text);
            Assert(AutoCapture.Report("x", after, null, null).Contains("基线"), "没有上一次扫描");
            Assert(AutoCapture.Report("x", after, Snap("m", 0, "Partial", (@"C:\Users\u\AppData\Local\Temp", 1 * GiB)) is var mft && (mft.RuleVersion = "r1+mft") != null ? mft : null, null).Contains("基线"), "不同扫描方式不对比");
            Assert(AutoCapture.Report("x", Snap("z", 90, "Cancelled"), before, null).Contains("没有正常结束"), "取消的扫描");
            Assert(AutoCapture.Report("x", Snap("s", 90, "Partial", (@"C:\Windows", 20 * GiB)), before, null).Contains("没有明显增长"), "没有增长时说明可能原因");
        }));
    }
}
