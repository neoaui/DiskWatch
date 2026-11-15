namespace DiskWatch.Core;

public sealed class Classifier(ClassificationContext context)
{
    public static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');
    public static bool Under(string path, string root)
    {
        var p = Normalize(path); var r = Normalize(root);
        return r.Length > 0 && (p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase));
    }
    private static bool Segment(string path, params string[] names) => Normalize(path).Split('\\').Any(s => names.Contains(s, StringComparer.OrdinalIgnoreCase));
    public Classification Classify(string path)
    {
        var p = Normalize(path);
        var rule = context.Overrides.OrderByDescending(x => Normalize(x.Path).Length).FirstOrDefault(x =>
            x.IncludeChildren ? Under(p, x.Path) : p.Equals(Normalize(x.Path), StringComparison.OrdinalIgnoreCase));
        if (rule != null) return new(rule.Category, 100, "用户确认：" + rule.Note);
        var name = p.Split('\\').Last();
        if (p.Count(c => c == '\\') == 1 && new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            return new(Category.SpecialFiles, 98, "卷根目录系统特殊文件名");
        if (Segment(p, "$Recycle.Bin")) return new(Category.Other, 95, "回收站（删除到回收站尚未释放空间）");
        if (Segment(p, "Temp", "tmp", "$Windows.~BT", "$Windows.~WS")) return new(Category.Temporary, 80, "临时目录规则");
        if (Under(p, context.WindowsDirectory)) return new(Category.Windows, 90, "Windows 系统目录");
        if (Segment(p, ".codex", ".pnpm-store", ".local", ".nuget", ".npm", ".cargo", ".rustup", "node_modules", "NuGet", "npm-cache", "npm", "pnpm", ".gradle", ".m2", ".venv", "venv"))
            return new(Category.Development, 85, "开发环境目录规则");
        if (context.GameRoots.Any(x => Under(p, x)) || Segment(p, "steamapps", "Saved Games"))
            return new(Category.Games, 90, "游戏库 / 已知存档目录");
        var app = context.Applications.Where(x => Under(p, x.Directory)).OrderByDescending(x => x.Directory.Length).FirstOrDefault();
        var vendor = Segment(p, "Lenovo", "LenovoPCManager", "ASUS", "ASUSTeK", "Armoury Crate", "ArmouryCrate", "MyASUS", "天选姬");
        if (Segment(p, "Cache", "Caches", "Code Cache", "GPUCache", "Logs", "Log", "CrashDumps", "D3DSCache", "INetCache", "WebCache", "WeChat Files", "WeChat", "Weixin", "xwechat_files", "QQ", "QQ Files"))
            return new(Category.AppData, 75, "应用缓存 / 日志 / 数据目录规则", vendor ? "厂商软件" : app?.Name);
        if (vendor) return new(Category.Vendor, 80, "联想 / 华硕等厂商目录规则", app?.Name);
        if (app != null) return new(Category.Applications, 90, "卸载注册表安装位置：" + app.Name, app.Name);
        if (Segment(p, "Program Files", "Program Files (x86)")) return new(Category.Applications, 70, "应用安装父目录（未确认具体应用）");
        if (context.PersonalRoots.Any(x => Under(p, x))) return new(Category.Personal, 75, "用户已知文档 / 桌面 / 下载等目录");
        return new(Category.Other, 30, "未命中明确规则；可由用户纠正");
    }
}
