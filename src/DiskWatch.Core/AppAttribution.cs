using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskWatch.Core;

/// <summary>
/// 应用归属规则（规则表的一行）：Pattern 是一段路径片段，按“整段目录名”匹配、不区分大小写，段内可用 * 通配；
/// 出现在路径任意位置即命中，以 \ 开头表示必须从卷根开始（例如 \Windows\SoftwareDistribution）。
/// Fallback = true 的规则（个人文件夹等）只在其他规则都没命中时使用，例如“文档\WeChat Files”仍算微信。
/// </summary>
public sealed record AppRule(string Pattern, string App, bool Fallback = false);
/// <summary>一次扫描里某个应用 / 来源占用的分配空间。Known = 命中规则表或安装记录；否则为按目录归组的“未识别”来源。</summary>
public sealed record AppUsage(string App, bool Known, long AllocatedBytes, long Files);
public readonly record struct AppSource(string App, bool Known);

/// <summary>“谁在吃 C 盘”：把路径归到应用 / 来源。用户规则（Settings.AppRules）优先，其次内置规则表、卸载注册表里的安装目录，最后按目录归组。</summary>
public sealed class AppMatcher
{
    private sealed record Compiled(string[] Segments, Regex?[] Globs, bool Anchored, string App, int Rank, int Order, string? Literal);
    private readonly List<Compiled> user, builtIn;
    private readonly Dictionary<string, List<Compiled>> userByLiteral = new(StringComparer.OrdinalIgnoreCase), builtInByLiteral = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Compiled> userAlways = [], builtInAlways = [];

    public AppMatcher(IEnumerable<AppRule>? userRules = null)
    {
        user = Compile(userRules ?? [], userByLiteral, userAlways);
        builtIn = Compile(AppAttribution.BuiltIn, builtInByLiteral, builtInAlways);
    }

    private static List<Compiled> Compile(IEnumerable<AppRule> rules, Dictionary<string, List<Compiled>> byLiteral, List<Compiled> always)
    {
        var list = new List<Compiled>(); int order = 0;
        foreach (var rule in rules)
        {
            var pattern = rule.Pattern.Replace('/', '\\').Trim();
            bool anchored = pattern.StartsWith('\\');
            var segments = pattern.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || string.IsNullOrWhiteSpace(rule.App)) continue;
            var globs = segments.Select(s => s.Contains('*') ? new Regex("^" + Regex.Escape(s).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) : null).ToArray();
            // 越具体越优先：段数多的先比，其次是不含通配的段数。
            int rank = segments.Length * 16 + globs.Count(g => g == null) - (rule.Fallback ? 10_000 : 0);
            var literal = segments.Where(s => !s.Contains('*')).OrderByDescending(s => s.Length).FirstOrDefault();
            var c = new Compiled(segments, globs, anchored, rule.App.Trim(), rank, order++, literal);
            list.Add(c);
            if (literal == null) always.Add(c);
            else { if (!byLiteral.TryGetValue(literal, out var bucket)) byLiteral[literal] = bucket = []; bucket.Add(c); }
        }
        return list;
    }

    /// <summary>路径去掉盘符后的目录段（C:\Users\a → Users, a）。</summary>
    public static string[] Segments(string path)
    {
        var parts = Classifier.Normalize(path).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts[0].EndsWith(':') ? parts[1..] : parts;
    }
    private static string Drive(string path)
    {
        var p = Classifier.Normalize(path); int i = p.IndexOf('\\');
        var first = i < 0 ? p : p[..i];
        return first.EndsWith(':') ? first + "\\" : "";
    }

    private static bool MatchAt(Compiled c, string[] segs, int start)
    {
        if (start + c.Segments.Length > segs.Length) return false;
        for (int k = 0; k < c.Segments.Length; k++)
        {
            var s = segs[start + k];
            if (c.Globs[k] is { } g ? !g.IsMatch(s) : !s.Equals(c.Segments[k], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
    private static Compiled? Best(string[] segs, Dictionary<string, List<Compiled>> byLiteral, List<Compiled> always)
    {
        Compiled? best = null;
        void Try(Compiled c)
        {
            if (best != null && (c.Rank < best.Rank || (c.Rank == best.Rank && c.Order > best.Order))) return;
            bool hit = c.Anchored ? MatchAt(c, segs, 0) : Enumerable.Range(0, Math.Max(0, segs.Length - c.Segments.Length + 1)).Any(i => MatchAt(c, segs, i));
            if (hit) best = c;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in segs)
            if (seen.Add(s) && byLiteral.TryGetValue(s, out var bucket)) foreach (var c in bucket) Try(c);
        foreach (var c in always) Try(c);
        return best;
    }

    /// <summary>
    /// 归属一个路径。isFile = true 时 path 是文件（用于卷根目录下的 pagefile.sys 等）；否则 path 是目录。
    /// installedApp：卸载注册表里登记的安装目录命中的应用名（来自现有分类器），在规则表之后使用。
    /// </summary>
    public AppSource Attribute(string path, string? installedApp = null, bool isFile = false)
    {
        var segs = Segments(path);
        var hit = Best(segs, userByLiteral, userAlways) ?? Best(segs, builtInByLiteral, builtInAlways);
        if (hit != null) return new(hit.App, true);
        if (!string.IsNullOrWhiteSpace(installedApp) && installedApp != "厂商软件") return new(installedApp, true);
        var dir = isFile ? segs[..Math.Max(0, segs.Length - 1)] : segs;
        return new(Unknown(Drive(path), dir), false);
    }

    /// <summary>未命中规则：AppData\Local|Roaming|LocalLow 下取第一层，Program Files / ProgramData 下取第一层，用户目录下取第一层，其余取卷根下第一层。</summary>
    private static string Unknown(string drive, string[] d)
    {
        if (d.Length == 0) return drive + "（根目录文件）";
        for (int i = 0; i + 2 < d.Length; i++)
            if (d[i].Equals("AppData", StringComparison.OrdinalIgnoreCase) && (d[i + 1].Equals("Local", StringComparison.OrdinalIgnoreCase) || d[i + 1].Equals("Roaming", StringComparison.OrdinalIgnoreCase) || d[i + 1].Equals("LocalLow", StringComparison.OrdinalIgnoreCase)))
            {
                int take = d[i + 2].Equals("Packages", StringComparison.OrdinalIgnoreCase) && i + 3 < d.Length ? i + 4 : i + 3;
                return drive + string.Join('\\', d[..take]);
            }
        if ((d[0].Equals("Program Files", StringComparison.OrdinalIgnoreCase) || d[0].Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) || d[0].Equals("ProgramData", StringComparison.OrdinalIgnoreCase)) && d.Length >= 2)
            return drive + d[0] + "\\" + d[1];
        if (d[0].Equals("Users", StringComparison.OrdinalIgnoreCase) && d.Length >= 2)
            return drive + string.Join('\\', d[..Math.Min(3, d.Length)]);
        return drive + d[0];
    }
}

public static class AppAttribution
{
    public const string Version = "apps-v1";

    /// <summary>
    /// 内置规则表（可扩展：用户可在 settings.json 的 AppRules 里追加同格式的规则，用户规则优先）。
    /// 越具体（段数越多）的规则越优先；同样具体时按表中先后。
    /// </summary>
    public static readonly IReadOnlyList<AppRule> BuiltIn =
    [
        // 临时文件放在前面：同样具体时，Temp 里的东西算临时文件。
        new(@"\Windows\Temp", "Temp 临时文件"), new("Temp", "Temp 临时文件"), new("tmp", "Temp 临时文件"),
        // Windows 目录放在厂商 / 聊天规则之前：同样具体时，Windows 目录里的驱动等算 Windows。
        new(@"\Windows\SoftwareDistribution", "Windows 更新"), new(@"\Windows\WinSxS", "Windows 组件存储（WinSxS）"), new(@"\Windows\Installer", "Windows 安装缓存"),
        new(@"\Windows", "Windows 系统"), new(@"\$Windows.~BT", "Windows 升级文件"), new(@"\$Windows.~WS", "Windows 升级文件"), new(@"\$WinREAgent", "Windows 升级文件"),
        new(@"\Windows.old", "Windows 旧版本"), new(@"\System Volume Information", "还原点 / 卷影副本"), new(@"\$Recycle.Bin", "回收站"),
        new(@"\pagefile.sys", "页面 / 休眠文件"), new(@"\hiberfil.sys", "页面 / 休眠文件"), new(@"\swapfile.sys", "页面 / 休眠文件"),
        // 浏览器
        new(@"Microsoft\Edge\User Data\*\Cache", "Edge 缓存"), new(@"Microsoft\Edge\User Data\*\Code Cache", "Edge 缓存"),
        new(@"Microsoft\Edge\User Data\*\GPUCache", "Edge 缓存"), new(@"Microsoft\Edge\User Data\*\Service Worker", "Edge 缓存"),
        new(@"Microsoft\Edge\User Data", "Edge 浏览器数据"), new(@"Microsoft\Edge", "Edge 浏览器"),
        new(@"Microsoft\EdgeUpdate", "Edge 更新"), new(@"Microsoft\EdgeCore", "Edge 更新"), new(@"Microsoft\EdgeWebView", "Edge WebView"),
        new(@"Google\Chrome\User Data\*\Cache", "Chrome 缓存"), new(@"Google\Chrome\User Data\*\Code Cache", "Chrome 缓存"), new(@"Google\Chrome", "Chrome 浏览器"),
        // 聊天
        new("WeChat Files", "微信"), new("xwechat_files", "微信"), new(@"Tencent\WeChat", "微信"), new(@"Tencent\xwechat", "微信"), new(@"Tencent\Weixin", "微信"),
        new("Weixin", "微信"), new("WeChat", "微信"),
        new("Tencent Files", "QQ"), new(@"Tencent\QQ", "QQ"), new(@"Tencent\QQNT", "QQ"),
        // 厂商软件
        new(@"Packages\E046963F.LenovoCompanion_*", "联想 Vantage"), new(@"Lenovo\Vantage", "联想 Vantage"), new(@"Lenovo\ImController", "联想 Vantage"),
        new("LenovoPCManager", "联想电脑管家"), new(@"Lenovo\PCManager", "联想电脑管家"), new("Lenovo", "联想软件"),
        new("ASUS", "华硕软件"), new("ASUSTeK", "华硕软件"), new("ArmouryCrate", "华硕软件"), new("Armoury Crate", "华硕软件"),
        // 其他系统 / 常见软件
        new(@"\ProgramData\Package Cache", "安装包缓存"), new(@"Microsoft\Windows\INetCache", "系统网页缓存"), new(@"Microsoft\Windows\WER", "Windows 错误报告"),
        new("CrashDumps", "崩溃转储"), new("D3DSCache", "显卡着色器缓存"), new(@"NVIDIA\DXCache", "显卡着色器缓存"), new(@"NVIDIA\GLCache", "显卡着色器缓存"),
        new(@"Packages\Microsoft.Copilot_*", "Copilot"), new(@"Packages\MSTeams_*", "Teams"), new(@"Microsoft\Teams", "Teams"),
        new("OneDrive", "OneDrive"), new(@"Microsoft\Office", "Office"), new("Microsoft Office", "Office"),
        // 开发
        new(".codex", "Codex"), new(@"OpenAI\Codex", "Codex"),
        new(".nuget", "NuGet 包缓存"), new("NuGet", "NuGet 包缓存"), new("npm-cache", "npm 缓存"), new(".npm", "npm 缓存"), new(".pnpm-store", "pnpm 缓存"),
        new("node_modules", "node_modules 依赖"), new(".cargo", "Rust 工具链"), new(".rustup", "Rust 工具链"), new(".gradle", "Gradle 缓存"), new(".m2", "Maven 缓存"),
        new(@"pip\Cache", "pip 缓存"), new(".venv", "Python 虚拟环境"), new("venv", "Python 虚拟环境"), new("Docker", "Docker"),
        new(@"Packages\CanonicalGroupLimited.*", "WSL Linux"), new(".vscode", "VS Code"), new(@"Programs\Microsoft VS Code", "VS Code"),
        // 游戏
        new("steamapps", "Steam 游戏"), new("Steam", "Steam"), new("Epic Games", "Epic 游戏"),
        // 个人文件夹：兜底规则，里面的微信、游戏等仍按上面的规则归属。
        new(@"\Users\*\Downloads", "下载文件夹", true), new(@"\Users\*\Desktop", "桌面", true), new(@"\Users\*\Documents", "文档", true),
        new(@"\Users\*\Videos", "视频", true), new(@"\Users\*\Pictures", "图片", true), new(@"\Users\*\Music", "音乐", true),
    ];

    /// <summary>规则表版本：内置表版本 + 用户规则。只对比同一规则表下的两次结果。</summary>
    public static string RulesKey(Settings settings) => Version + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", settings.AppRules.Select(r => r.Pattern.Trim() + "=>" + r.App.Trim() + (r.Fallback ? "?" : ""))))))[..12];

    public static AppSource Attribute(string path, IEnumerable<AppRule>? userRules = null, string? installedApp = null, bool isFile = false) =>
        new AppMatcher(userRules).Attribute(path, installedApp, isFile);
}

/// <summary>扫描时累加每个应用 / 来源的分配空间。同一目录只归属一次（缓存），卷根目录下的文件按文件名归属。</summary>
public sealed class AppUsageCollector
{
    private readonly AppMatcher matcher;
    private readonly Dictionary<string, AppSource> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AppSource, (long Bytes, long Files)> totals = [];
    private string? lastDirectory; private AppSource lastSource;
    public AppUsageCollector(IEnumerable<AppRule>? userRules = null) => matcher = new AppMatcher(userRules);

    public void Add(string directory, string fileName, long allocatedBytes, string? installedApp = null)
    {
        AppSource source;
        var normalized = Classifier.Normalize(directory);
        if (normalized.Length == 2 && normalized[1] == ':') source = matcher.Attribute(normalized + "\\" + fileName, installedApp, true);
        else if (lastDirectory != null && string.Equals(lastDirectory, directory, StringComparison.Ordinal)) source = lastSource;
        else
        {
            if (!cache.TryGetValue(directory, out source))
            {
                if (cache.Count > 300_000) cache.Clear();
                cache[directory] = source = matcher.Attribute(directory, installedApp);
            }
            lastDirectory = directory; lastSource = source;
        }
        totals.TryGetValue(source, out var t);
        totals[source] = (t.Bytes + allocatedBytes, t.Files + 1);
    }

    /// <summary>按占用从大到小；超过 max 项时，较小的未识别来源合并为一项，总量不变。</summary>
    public List<AppUsage> ToList(int max = 2000)
    {
        var list = totals.Select(x => new AppUsage(x.Key.App, x.Key.Known, x.Value.Bytes, x.Value.Files)).OrderByDescending(x => x.AllocatedBytes).ThenBy(x => x.App, StringComparer.Ordinal).ToList();
        if (list.Count <= max) return list;
        var keep = list.Take(max - 1).ToList(); var rest = list.Skip(max - 1).ToList();
        keep.Add(new("其他较小来源（合并）", false, rest.Sum(x => x.AllocatedBytes), rest.Sum(x => x.Files)));
        return keep;
    }
}
