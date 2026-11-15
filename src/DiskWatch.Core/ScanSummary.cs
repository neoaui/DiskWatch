using System.Text;

namespace DiskWatch.Core;

public static class ScanSummary
{
    public static string Build(IEnumerable<(Snapshot Current, Snapshot? Previous)> items)
    {
        var text = new StringBuilder("磁盘扫描总结\n说明：以下是各范围最近一次扫描结果，具体时间见各节。不是同一时刻的整盘快照。\n");
        foreach (var (s, previous) in items)
        {
            text.AppendLine($"\n━━━━━━━━  {s.Root}  ━━━━━━━━");
            text.AppendLine($"完成于 {s.Finished.ToLocalTime():yyyy-MM-dd HH:mm:ss}，用时 {(s.Finished-s.Started).TotalSeconds:N0} 秒，{s.Files:N0} 个文件");
            text.AppendLine(s.Coverage);
            if (s.IsWholeVolume && s.After != null)
            {
                text.AppendLine($"总容量 {Labels.Size(s.After.TotalBytes)}；已用 {Labels.Size(s.After.UsedBytes)}；剩余 {Labels.Size(s.After.FreeBytes)}（{(double)s.After.FreeBytes / Math.Max(1,s.After.TotalBytes):P1}）。");
                if (s.After.FreeBytes <= 20L*1073741824) text.AppendLine("注意：剩余空间已不高于 20 GiB，请优先查看占用来源。");
                if (previous is { IsWholeVolume: true, After: not null } && previous.VolumeId == s.VolumeId)
                    text.AppendLine($"相比 {previous.Finished.ToLocalTime():MM-dd HH:mm}：已用空间净变化 {(s.After.UsedBytes-previous.After.UsedBytes)/1073741824d:+0.000;-0.000;0.000} GiB（整盘容量变化，不等于文件增减合计）。");
                else text.AppendLine("无同卷历史基线，暂不计算卷变化。");
            }
            text.AppendLine($"已解释分配空间 {Labels.Size(s.AllocatedBytes)}；逻辑大小 {Labels.Size(s.LogicalBytes)}。");
            text.AppendLine(s.Residual is long residual ? $"未解释差额 {Labels.Size(residual)}。可能包括未覆盖区域、文件系统开销和扫描时差，不等于还原点或可清理空间。" : "当前为文件夹扫描，不计算整盘未解释差额。");
            if (s.Residual < 0) text.AppendLine("注意：差额为负，可能存在扫描时差或计量误差，需要核对。");
            text.AppendLine("\n分类占用（分配空间）：");
            foreach (var c in Enum.GetValues<Category>().Where(c=>c!=Category.Unexplained).OrderByDescending(c=>s.Buckets.Where(b=>b.Category==c).Sum(b=>b.AllocatedBytes)))
                text.AppendLine($"  {Labels.CategoryName(c)}：{Labels.Size(s.Buckets.Where(b=>b.Category==c).Sum(b=>b.AllocatedBytes))}");
            text.AppendLine("\n主要占用目录（前 5 项，互不重复）：");
            foreach (var b in s.Buckets.OrderByDescending(b=>b.AllocatedBytes).Take(5)) text.AppendLine($"  {Labels.Size(b.AllocatedBytes)}  {b.Path}");
            var changes = Reports.Compare(s, previous);
            text.AppendLine("\n主要增长 / 释放：");
            if (previous == null || !s.IsComparable || !previous.IsComparable || s.RuleVersion!=previous.RuleVersion || s.VolumeId!=previous.VolumeId || s.Root!=previous.Root)
                text.AppendLine("  缺少可对比的完整扫描，或不具备对比条件：不将目录差值视为确定的增长或删除。请结合容量曲线和占用列表核对。");
            else
            {
                var changed=changes.Where(c=>c.ChangeGiB!=0).Take(5).ToList();
                if(changed.Count==0) text.AppendLine("  可比较目录未发现分配空间变化。");
                foreach(var c in changed) text.AppendLine($"  {c.ChangeGiB:+0.000;-0.000;0.000} GiB  {c.Path}（{c.Category}；扫描窗口差值）");
            }
            text.AppendLine("\n占用较大的文件（前 5 项，不代表可以删除）：");
            foreach(var f in s.LargestFiles.OrderByDescending(f=>f.AllocatedBytes).Take(5)) text.AppendLine($"  {Labels.Size(f.AllocatedBytes)}  {f.Path}（{Labels.CategoryName(f.Category)}{(Cleanup.NotDeletableReason(f.Path,f.Category) is string why?"；不可删除："+why:"")}）");
            if(s.LargestFiles.Count==0) text.AppendLine("  没有达到记录条件的大文件。");
            text.AppendLine($"\n覆盖说明：读取失败 {s.Errors:N0} 项；跳过链接点 {s.ReparsePointsSkipped:N0}；硬链接已去重 {s.HardLinksSkipped:N0}。");
            foreach(var issue in s.Issues.Take(3)) text.AppendLine($"  {issue.Path}：{issue.Reason}");
            text.AppendLine("完整问题清单请查看“扫描覆盖”。分类来自路径和安装信息规则，不能据此确定写入进程。\n");
        }
        return text.ToString();
    }
}
