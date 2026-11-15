namespace DiskWatch.Core;

/// <summary>瘦身建议的两种动作。仅为建议文字，程序不提供任何自动删除或移动功能。</summary>
public enum SlimAction { Move = 1, Delete }

public sealed record SlimSuggestion(string Path, long AllocatedBytes, SlimAction Action, string Reason, bool IsFile, Category Category);

public sealed record SlimPlan(IReadOnlyList<SlimSuggestion> Move, IReadOnlyList<SlimSuggestion> Delete)
{
    public long MoveBytes => Move.Sum(x => x.AllocatedBytes);
    public long DeleteBytes => Delete.Sum(x => x.AllocatedBytes);
}

/// <summary>
/// 纯规则判断：哪些路径不可删除、哪些可以转移或删除。只依据快照里已有的路径和 11 类分类结果，不访问磁盘。
/// </summary>
public static partial class Cleanup
{
    /// <summary>目录统计桶至少达到此分配空间才进入瘦身建议。</summary>
    public const long MinBucketBytes = 256L * 1024 * 1024;
    private const long MinRemainderBytes = 50L * 1024 * 1024;
    private static readonly StringComparer I = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] VmExtensions = [".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow2", ".avhdx"];
    private static readonly string[] InstallerExtensions = [".exe", ".msi", ".msix", ".iso", ".zip", ".7z", ".rar", ".cab"];
    private static readonly string[] VendorNames = ["Lenovo", "LenovoPCManager", "ASUS", "ASUSTeK", "Armoury Crate", "ArmouryCrate", "MyASUS"];
    private static readonly string[] VendorUpdateNames = ["Cache", "Download", "Downloads", "Repository", "Packages", "Updates", "Update", "SystemUpdate", "sessionSE"];
    private static readonly string[] CacheNames = ["Cache", "Caches", "Code Cache", "GPUCache", "INetCache", "D3DSCache", "ShaderCache", "DXCache", "GLCache", "NV_Cache", "cache2", "CacheStorage", "CachedExtensionVSIXs"];
    private static readonly string[] PackageCacheNames = ["npm-cache", ".pnpm-store", "pnpm-cache", "Yarn"];
    private static readonly string[] BinaryExtensions = [".exe", ".dll", ".sys", ".ocx", ".drv", ".efi"];
    private static readonly string[] ChatNames = ["WeChat Files", "xwechat_files", "Tencent Files", "QQ Files", "nt_qq"];
    private static readonly string[] PersonalNames = ["Downloads", "Desktop", "Documents", "Videos", "Pictures", "Music", "下载", "桌面", "文档"];

    private static string[] Parts(string path) => Classifier.Normalize(path).Split('\\', StringSplitOptions.RemoveEmptyEntries);
    private static bool Has(string[] parts, params string[] names) => parts.Any(p => names.Contains(p, I));
    private static bool At(string[] parts, int index, params string[] names) => parts.Length > index && names.Contains(parts[index], I);
    private static bool Sequence(string[] parts, string first, string second)
    {
        for (var i = 0; i + 1 < parts.Length; i++) if (I.Equals(parts[i], first) && I.Equals(parts[i + 1], second)) return true;
        return false;
    }
    private static string Extension(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant();

    /// <summary>Windows 目录中允许清理的位置：Temp、更新下载缓存、崩溃转储。</summary>
    private static bool WindowsCleanable(string[] p) =>
        At(p, 1, "Windows") && (At(p, 2, "Temp", "Minidump", "LiveKernelReports") || (p.Length == 3 && I.Equals(p[2], "MEMORY.DMP")) || (At(p, 2, "SoftwareDistribution") && At(p, 3, "Download")));

    /// <summary>
    /// 返回“不可删除”的原因；可以考虑处理的路径返回 null。
    /// 依据：卷根特殊文件、系统特殊文件分类、Windows 目录、还原点、NTFS 元数据、启动/恢复文件、安装缓存、已安装软件本体。
    /// </summary>
    public static string? NotDeletableReason(string path, Category category)
    {
        var p = Parts(path);
        if (p.Length == 0) return null;
        var name = p[^1];
        if (p.Length == 2)
        {
            switch (name.ToLowerInvariant())
            {
                case "pagefile.sys": return "系统页面文件（虚拟内存），由 Windows 管理";
                case "hiberfil.sys": return "休眠文件，由 Windows 管理，不能手动删除";
                case "swapfile.sys": return "系统交换文件，由 Windows 管理";
                case "bootmgr": case "bootnxt": case "bootsect.bak": return "启动文件，删除可能导致无法开机";
            }
        }
        if (category == Category.SpecialFiles) return "系统特殊文件，由 Windows 管理";
        if (Has(p, "System Volume Information")) return "系统还原点 / 卷影副本存放处，由 Windows 管理";
        if (p.Length > 1 && p[1].StartsWith('$') && !I.Equals(p[1], "$Recycle.Bin"))
            return At(p, 1, "$Windows.~BT", "$Windows.~WS", "$WinREAgent", "$GetCurrent", "$SysReset")
                ? "Windows 升级 / 恢复用文件，请用“磁盘清理”处理，不要手动删除"
                : "NTFS 元数据或系统维护文件，由 Windows 管理";
        if (At(p, 1, "Boot", "EFI", "Recovery")) return "启动 / 恢复环境文件，删除可能导致无法开机或无法重置系统";
        if ((category == Category.Windows || At(p, 1, "Windows")) && !WindowsCleanable(p))
        {
            if (Has(p, "WinSxS")) return "Windows 组件存储，只能用“磁盘清理”或 DISM 清理";
            if (Has(p, "DriverStore")) return "驱动程序仓库，删除会导致设备驱动无法重装";
            if (Sequence(p, "Windows", "Installer")) return "软件安装缓存，删除后相关软件无法修复或卸载";
            if (Has(p, "System32", "SysWOW64")) return "Windows 核心系统文件";
            return "Windows 系统目录中的文件，由 Windows 管理";
        }
        if (Sequence(p, "ProgramData", "Package Cache")) return "软件安装缓存，删除后相关软件无法修复或卸载";
        if (Sequence(p, "ProgramData", "Microsoft")) return "Windows 组件数据（如 Defender、搜索索引），由 Windows 管理";
        if (Has(p, "WindowsApps")) return "Microsoft Store 应用程序文件，请在“设置 → 应用”中卸载";
        // 已安装软件：Program Files 下的一律保护；其他位置只保护程序本体（exe/dll 等），避免把被误登记为安装目录的下载、视频也标红。
        if ((category is Category.Applications or Category.Vendor && Has(p, "Program Files", "Program Files (x86)"))
            || (category == Category.Applications && BinaryExtensions.Contains(Extension(path))))
            return "已安装软件的程序文件，请在“设置 → 应用”中卸载，不要直接删除";
        return null;
    }

    public static bool IsDeletable(string path, Category category) => NotDeletableReason(path, category) == null;

    /// <summary>单个路径的瘦身建议；不可删除的路径永远返回 null。</summary>
    public static (SlimAction Action, string Reason)? Suggest(string path, Category category, bool isFile)
    {
        if (NotDeletableReason(path, category) != null) return null;
        var p = Parts(path);
        var ext = isFile ? Extension(path) : "";
        var target = p.Length > 0 && I.Equals(p[0], "C:") ? " D: 等其他盘" : "空间更充裕的其他盘";
        // AppData、Saved Games 里的程序数据和存档不能直接搬走，只保留聊天数据和虚拟磁盘这类有迁移办法的项目。
        var fixedPlace = Has(p, "AppData", "Saved Games");
        if ((isFile && VmExtensions.Contains(ext)) || Has(p, "Virtual Machines", "VirtualBox VMs", "Hyper-V"))
            return (SlimAction.Move, $"虚拟机 / WSL 磁盘镜像，可用对应软件的迁移功能移到{target}，不要直接拖动");
        if (Has(p, ChatNames) && !BinaryExtensions.Contains(ext))
            return (SlimAction.Move, $"微信 / QQ 聊天记录和收到的文件，可在软件设置里把存储位置改到{target}");
        if (Has(p, "$Recycle.Bin")) return (SlimAction.Delete, "回收站里的内容，清空回收站即可释放空间");
        if (category == Category.Temporary) return (SlimAction.Delete, "临时文件，关闭相关程序后可删除，正在使用的文件会自动跳过");
        if (At(p, 1, "Windows") && At(p, 2, "SoftwareDistribution"))
            return (SlimAction.Delete, "Windows 更新下载缓存，可用“磁盘清理”里的“Windows 更新清理”删除");
        if (Has(p, "CrashDumps", "Minidump", "LiveKernelReports") || ext == ".dmp" || (isFile && I.Equals(p[^1], "MEMORY.DMP")))
            return (SlimAction.Delete, "程序 / 系统崩溃转储，问题排查完后可删除");
        if (Has(p, VendorNames) && Has(p, VendorUpdateNames))
            return (SlimAction.Delete, "厂商软件（联想 / 华硕等）的更新包和下载缓存，装完后可在厂商软件里清理");
        if (Has(p, PackageCacheNames)) return (SlimAction.Delete, "包管理器缓存，删除后需要时会重新下载");
        if (Has(p, CacheNames)) return (SlimAction.Delete, "浏览器 / 应用缓存，删除后会自动重建，首次打开可能稍慢");
        if (isFile && InstallerExtensions.Contains(ext) && Has(p, "Downloads", "下载"))
            return (SlimAction.Delete, "下载目录里的安装包 / 镜像 / 压缩包，装完或解压后通常可删除");
        if (isFile && InstallerExtensions.Contains(ext) && Has(p, "AppData") && Has(p, "updates", "update", "Updater"))
            return (SlimAction.Delete, "软件自动下载的更新安装包，更新装完后通常可删除");
        if (fixedPlace) return null;
        if (category == Category.Games) return (SlimAction.Move, $"游戏库，可在 Steam 等平台里把游戏库移动到{target}");
        if (category == Category.Development) return (SlimAction.Move, $"开发环境 / 工作目录（如 .codex、依赖包），可迁到{target}后改相应配置");
        if (category == Category.Personal || (At(p, 1, "Users") && Has(p, PersonalNames)))
            return (SlimAction.Move, $"个人文件，可移到{target}");
        return null;
    }

    /// <summary>
    /// 根据快照里已有的目录统计桶和大文件生成瘦身建议，不做新的扫描。
    /// 大文件若与所在目录桶建议相同则不重复列出；动作不同时单独列出并从目录桶中扣除，避免重复计数。
    /// </summary>
    public static SlimPlan Plan(Snapshot snapshot)
    {
        // 统计桶互不重叠；子目录桶并入同分类、同建议的上级桶，得到上级目录的合计，再按阈值筛选。
        var kept = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<(Bucket Bucket, SlimAction Action, string Reason, long Bytes)>();
        foreach (var b in snapshot.Buckets.OrderBy(b => Classifier.Normalize(b.Path).Length))
        {
            if (Suggest(b.Path, b.Category, false) is not { } s) continue;
            int parent = -1;
            for (var dir = Classifier.Normalize(b.Path); parent < 0 && dir.Contains('\\'); dir = dir[..dir.LastIndexOf('\\')])
                if (kept.TryGetValue($"{(int)b.Category}|{(int)s.Action}|{dir[..dir.LastIndexOf('\\')]}", out var found)) parent = found;
            if (parent >= 0) { merged[parent] = merged[parent] with { Bytes = merged[parent].Bytes + b.AllocatedBytes }; continue; }
            kept.TryAdd($"{(int)b.Category}|{(int)s.Action}|{Classifier.Normalize(b.Path)}", merged.Count);
            merged.Add((b, s.Action, s.Reason, b.AllocatedBytes));
        }
        var buckets = merged.Where(b => b.Bytes >= MinBucketBytes).ToList();
        var files = new List<SlimSuggestion>();
        foreach (var f in snapshot.LargestFiles.OrderByDescending(f => f.AllocatedBytes))
        {
            if (Suggest(f.Path, f.Category, true) is not { } s) continue;
            var index = -1;
            for (var i = 0; i < buckets.Count; i++)
                if (buckets[i].Bucket.Category == f.Category && Classifier.Under(f.Path, buckets[i].Bucket.Path) && (index < 0 || buckets[i].Bucket.Path.Length > buckets[index].Bucket.Path.Length)) index = i;
            if (index >= 0 && buckets[index].Action == s.Action) continue;
            if (index >= 0) buckets[index] = buckets[index] with { Bytes = buckets[index].Bytes - f.AllocatedBytes };
            files.Add(new(f.Path, f.AllocatedBytes, s.Action, s.Reason, true, f.Category));
        }
        var all = buckets.Where(b => b.Bytes >= MinRemainderBytes)
            .Select(b => new SlimSuggestion(b.Bucket.Path, b.Bytes, b.Action, b.Reason, false, b.Bucket.Category))
            .Concat(files).OrderByDescending(x => x.AllocatedBytes).ToList();
        return new(all.Where(x => x.Action == SlimAction.Move).ToList(), all.Where(x => x.Action == SlimAction.Delete).ToList());
    }
}