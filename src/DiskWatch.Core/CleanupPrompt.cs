using System.Globalization;
using System.Text;

namespace DiskWatch.Core;

public static partial class Cleanup
{
    /// <summary>通用不可触碰规则，写进 AI 提示词的禁止清单。</summary>
    public static readonly string[] GenericDenyRules =
    [
        @"任何盘的 Windows 目录（如 C:\Windows，含 WinSxS、System32、DriverStore、Installer）；唯一例外是本清单“可以删除”里明确列出的 Windows\Temp、SoftwareDistribution\Download 等条目",
        "卷根目录的 pagefile.sys、hiberfil.sys、swapfile.sys，以及 bootmgr、BOOTNXT 等启动文件",
        "System Volume Information（系统还原点 / 卷影副本）",
        @"卷根目录下以 $ 开头的目录和文件（如 $Extend、$WinREAgent、$Windows.~BT），回收站 $Recycle.Bin 只能用“清空回收站”的方式处理",
        "Boot、EFI、Recovery 目录",
        @"Program Files、Program Files (x86)、WindowsApps 中的任何文件（软件只能通过“设置 → 应用”卸载）",
        @"C:\ProgramData\Package Cache、C:\ProgramData\Microsoft",
        "注册表、系统服务、计划任务、启动项、电源 / 休眠 / 虚拟内存设置",
    ];

    private static string MovePath(string path)
    {
        var p = Parts(path);
        var rest = string.Join('\\', p.Skip(1));
        var drive = p.Length > 0 && I.Equals(p[0], "C:") ? "D:" : "<目标盘>:";
        return $@"{drive}\DiskWatch-Moved\{p.FirstOrDefault()?.TrimEnd(':')}\{rest}".TrimEnd('\\');
    }

    /// <summary>针对一条“可以转移”建议，给出推荐目标位置和迁移方法。</summary>
    public static (string Target, string Method) MoveAdvice(SlimSuggestion item)
    {
        var p = Parts(item.Path);
        var ext = item.IsFile ? Extension(item.Path) : "";
        if ((item.IsFile && VmExtensions.Contains(ext)) || Has(p, "Virtual Machines", "VirtualBox VMs", "Hyper-V"))
            return (MovePath(item.Path), "用对应软件自带的迁移方式：WSL 用 wsl --export / --import，VirtualBox / Hyper-V 用其“移动”功能；不要直接剪切镜像文件");
        if (Has(p, ChatNames))
            return ("由用户在微信 / QQ 设置里选择（建议非系统盘）", "不要直接移动文件：请用户在微信 / QQ 的“设置 → 文件管理 / 存储位置”里更改目录，由软件自行迁移；AI 只负责说明步骤和事后核对大小");
        if (item.Category == Category.Games)
            return ("由游戏平台指定（如 D:\\SteamLibrary）", "使用游戏平台自带功能（Steam：设置 → 存储空间 → 移动；其他平台同理），不要直接剪切游戏目录");
        if (item.Category == Category.Development)
            return (MovePath(item.Path), "先关闭使用该目录的程序（如 Codex、编辑器、终端），复制到目标并核对文件数和大小一致后，删除原目录（进回收站），再在原位置创建目录联接：cmd /c mklink /J \"原路径\" \"目标路径\"（联接不需要管理员权限）；最后验证相关程序能正常打开");
        return (MovePath(item.Path), "直接移动（Move-Item）到目标位置；移动前确认没有程序正在使用，移动后核对大小；桌面 / 文档 / 下载本身是系统文件夹，只移动其中的文件和子文件夹，不要移动文件夹本身或修改其“位置”属性；其中看起来是游戏存档、软件配置或聊天记录的子文件夹（如 Klei、Paradox Interactive、WeChat Files）先列出来问我，不要直接移动");
    }

    private static string DeleteMethod(SlimSuggestion item)
    {
        var p = Parts(item.Path);
        if (Has(p, "$Recycle.Bin")) return "这是回收站内容：先列出回收站概况请用户确认，再用 Clear-RecycleBin -DriveLetter <盘符>（只清该盘），不要逐个删除 $Recycle.Bin 里的文件";
        if (At(p, 1, "Windows") && At(p, 2, "SoftwareDistribution")) return "优先让用户用“磁盘清理 → 清理系统文件 → Windows 更新清理”；如需管理员权限，跳过并告诉用户手动操作";
        if (item.IsFile) return "确认用途后移入回收站（不要永久删除）";
        if (Has(p, ".pnpm-store", "npm-cache", "pnpm-cache", "Yarn")) return "包管理器缓存：优先用对应命令清理（pnpm store prune、npm cache clean --force、yarn cache clean），不要直接删除目录本身";
        if (item.Category == Category.Temporary) return "这是统计目录，只处理其中的临时文件（Temp、tmp 等子目录里的内容），绝不删除整个目录；逐项移入回收站，被占用的文件跳过";
        return "清空该缓存目录里的内容（移入回收站），保留目录本身；被占用的文件跳过";
    }

    private static string Esc(string text) => text.Replace("|", "\\|");

    /// <summary>
    /// 生成可直接交给 AI 代理的瘦身操作提示词（纯文本 Markdown）。只生成文字，不执行任何操作。
    /// </summary>
    public static string BuildAiPrompt(Snapshot snapshot, SlimPlan plan, DateTimeOffset? now = null)
    {
        var t = new StringBuilder();
        var time = snapshot.Finished.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        t.AppendLine("# 磁盘瘦身任务（由 DiskWatch 导出）");
        t.AppendLine();
        t.AppendLine($"> 导出时间：{(now ?? DateTimeOffset.Now).ToLocalTime():yyyy-MM-dd HH:mm}。本提示词根据最近一次扫描结果生成，仅作建议清单。请按下面的安全规则逐步执行，每个会删除或移动文件的步骤都必须先得到我的确认。");
        t.AppendLine();
        t.AppendLine("## 一、背景");
        t.AppendLine("- 系统：Windows。可使用 PowerShell（普通用户权限）。");
        t.AppendLine($"- 扫描范围：{snapshot.Root}（快照完成于 {time}，共 {snapshot.Files:N0} 个文件；之后文件可能已有变化）。");
        if (snapshot.After is { } v)
            t.AppendLine($"- 扫描结束时该卷：总容量 {Labels.Size(v.TotalBytes)}，已用 {Labels.Size(v.UsedBytes)}，剩余 {Labels.Size(v.FreeBytes)}。");
        t.AppendLine($"- 建议转移合计约 {Labels.Size(plan.MoveBytes)}（{plan.Move.Count} 项），建议删除合计约 {Labels.Size(plan.DeleteBytes)}（{plan.Delete.Count} 项）。");
        t.AppendLine("- 大小均为“分配空间”，单位 GiB（1 GiB = 1,073,741,824 字节）。“目录”项统计的是该目录中归入对应分类的部分，不一定等于整个目录大小。");
        t.AppendLine();
        t.AppendLine("## 二、可以转移");
        if (plan.Move.Count == 0) t.AppendLine("（本次没有转移建议）");
        else
        {
            t.AppendLine("| # | 路径 | 大小 | 类型 | 原因 | 建议目标 | 推荐方法 |");
            t.AppendLine("|---|---|---|---|---|---|---|");
            var i = 0;
            foreach (var item in plan.Move)
            {
                var (target, method) = MoveAdvice(item);
                t.AppendLine($"| M{++i} | `{Esc(item.Path)}` | {Labels.Size(item.AllocatedBytes)} | {(item.IsFile ? "文件" : "目录")} | {Esc(item.Reason)} | `{Esc(target)}` | {Esc(method)} |");
            }
        }
        t.AppendLine();
        t.AppendLine("## 三、可以删除");
        if (plan.Delete.Count == 0) t.AppendLine("（本次没有删除建议）");
        else
        {
            t.AppendLine("| # | 路径 | 大小 | 类型 | 原因 | 处理方式 |");
            t.AppendLine("|---|---|---|---|---|---|");
            var i = 0;
            foreach (var item in plan.Delete)
                t.AppendLine($"| D{++i} | `{Esc(item.Path)}` | {Labels.Size(item.AllocatedBytes)} | {(item.IsFile ? "文件" : "目录")} | {Esc(item.Reason)} | {Esc(DeleteMethod(item))} |");
        }
        t.AppendLine();
        t.AppendLine("## 四、不可删除清单（一律不碰）");
        t.AppendLine("以下路径和规则范围内的任何文件，都不得删除、移动、重命名或修改权限与属性，即使占用很大也不行。如果某项操作会波及这些文件，立即停止并说明情况。");
        t.AppendLine();
        t.AppendLine("通用规则：");
        foreach (var rule in GenericDenyRules) t.AppendLine($"- {rule}");
        var locked = snapshot.LargestFiles.Select(f => (f.Path, Why: NotDeletableReason(f.Path, f.Category))).Where(x => x.Why != null).ToList();
        t.AppendLine();
        t.AppendLine("本次快照中标记为「不可删除」的大文件：");
        if (locked.Count == 0) t.AppendLine("- （本次快照没有标记为不可删除的大文件，仍须遵守上面的通用规则）");
        foreach (var (path, why) in locked) t.AppendLine($"- `{path}`：{why}");
        t.AppendLine();
        t.AppendLine("## 五、安全规则（必须遵守）");
        string[] rules =
        [
            "先只读核实：逐项确认路径仍然存在，并重新测量当前大小（Get-ChildItem -Recurse -Force 求和）；不存在或大小明显不同的条目标出来，不要处理。",
            "先给计划、再等确认：先输出完整计划表，然后停下等我确认。每一个会删除或移动文件的步骤（按单项或按我同意的批次）执行前，都必须得到我明确的“确认”回复，不能自行默认继续。",
            "删除优先进回收站（例如 [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile / DeleteDirectory 配合 SendToRecycleBin）；只有我明确同意时才允许永久删除。",
            "移动前检查目标盘剩余空间，至少比要移动的数据多 10%；空间不够就停止并告诉我。",
            "操作前提醒我关闭相关程序（游戏平台、微信 / QQ、Codex、编辑器、浏览器等）；遇到被占用、被锁定或无权限的文件直接跳过并记录，不要强制结束进程。",
            "禁止使用管理员提权（runas、Start-Process -Verb RunAs、关闭 UAC 等），禁止停用或修改系统服务，禁止修改注册表、计划任务、启动项和系统设置，禁止修改文件权限或所有者。",
            "只处理本提示词“可以转移 / 可以删除”里列出的条目；不要自行扩大范围，不要按通配符批量删除上级目录。",
            "把每一步操作（时间、命令、路径、结果、释放或移动的大小）追加记录到程序目录 exports\\ai-cleanup-log-<日期时间>.txt。",
            "任何命令报错或结果和预期不符，立即停止后续步骤，报告错误并等待我的指示。",
            "全部完成后给出最终报告：每项的结果、实际释放的空间、转移后的新位置，以及跳过的条目和原因。",
        ];
        for (var i = 0; i < rules.Length; i++) t.AppendLine($"{i + 1}. {rules[i]}");
        t.AppendLine();
        t.AppendLine("## 六、请按以下格式回复");
        t.AppendLine("1. **计划表**：编号（M1… / D1…）、路径、核实后的当前大小、动作（转移 / 删除 / 跳过）、目标位置、方法、风险说明。");
        t.AppendLine("2. **需要我先做的事**：要关闭的程序、需要我在软件设置里手动操作的步骤（如微信 / QQ 存储位置、Steam 移动游戏库）。");
        t.AppendLine("3. **分批执行步骤**：每批列出将要运行的 PowerShell 命令，然后停下等待我回复“确认”。");
        t.AppendLine("4. **最终报告**：执行完成后再输出。");
        t.AppendLine();
        t.AppendLine("现在请先只做第 1 步只读核实，并给出计划表，不要执行任何删除或移动。");
        return t.ToString();
    }
}
