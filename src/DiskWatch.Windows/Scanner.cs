using DiskWatch.Core;

namespace DiskWatch.Windows;

public sealed record ScanProgress(string Root, string CurrentDirectory, long Files, long Errors, long AllocatedBytes);
public sealed class Scanner(Classifier classifier, Settings settings, string ruleVersion)
{
    private sealed class Counter { public string Path = ""; public Category Category; public long Allocated, Logical, Files; }
    public Task<Snapshot> ScanAsync(string root, IProgress<ScanProgress>? progress, CancellationToken cancellationToken) => Task.Run(() => Scan(root, progress, cancellationToken), CancellationToken.None);
    private Snapshot Scan(string root, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        string? fastNote = null;
        if (settings.FastScanEnabled(MftScanner.IsElevated()))
        {
            // MFT 快速扫描只在已是管理员、NTFS 本地卷时使用；否则或失败时自动改用下面的目录遍历。
            var fast = MftScanner.TryScan(root, classifier, settings, ruleVersion, progress, token, out fastNote);
            if (fast != null) return fast;
        }
        var snapshot = new Snapshot { Root = root, Started = DateTimeOffset.UtcNow, RuleVersion = ruleVersion, CoverageKey = ScanCoverage.Key(settings), ScanMethod = fastNote == null ? "目录遍历" : $"目录遍历（未使用 MFT 快速扫描：{fastNote}）" };
        var buckets = new Dictionary<string, Counter>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var largest = new PriorityQueue<LargeFile, long>();
        long allocated = 0;
        // 文件级明细（变化明细用）：目录遍历时顺带记录，几乎不增加耗时；名称 / 大小 / 修改时间都来自已枚举的数据。
        var details = settings.FileDetailRetention > 0 ? new FileIndex { Root = root } : null;
        // 应用排行：按文件所在目录归属到应用 / 来源（同一目录只算一次规则匹配）。
        var apps = new AppUsageCollector(settings.AppRules);
        void Issue(string path, string reason) { snapshot.Errors++; if (snapshot.Issues.Count < 100) snapshot.Issues.Add(new(path, reason)); }
        try
        {
            snapshot.Before = NativeMetadata.Sample(root); snapshot.VolumeId = snapshot.Before.VolumeId;
            snapshot.IsWholeVolume = Classifier.Normalize(root).Equals(Classifier.Normalize(snapshot.Before.Root), StringComparison.OrdinalIgnoreCase);
            // Do not traverse a reparse root or a reparse ancestor of a selected subfolder.
            for (var ancestor = new DirectoryInfo(root); ancestor != null; ancestor = ancestor.Parent)
                if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("扫描根目录或其父目录为重解析点，请选择真实目标目录。");
            Stack<string> pending = new(); pending.Push(root);
            var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false };
            while (pending.TryPop(out var directory))
            {
                token.ThrowIfCancellationRequested();
                if (settings.ExcludedPaths.Any(x => Classifier.Under(directory, x))) { Issue(directory, "用户排除范围"); continue; }
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { snapshot.ReparsePointsSkipped++; continue; }
                    foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            if (settings.ExcludedPaths.Any(x => Classifier.Under(entry.FullName, x))) { Issue(entry.FullName, "用户排除范围"); continue; }
                            var attributes = entry.Attributes;
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { snapshot.ReparsePointsSkipped++; continue; }
                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                if (pending.Count >= 250_000) throw new ScanLimitException();
                                pending.Push(entry.FullName); continue;
                            }
                            if ((attributes & FileAttributes.Offline) != 0) { Issue(entry.FullName, "离线文件未读取，避免触发下载"); continue; }
                            var meta = NativeMetadata.Read(entry.FullName, attributes);
                            // Keep hardlink identities only: single-link files cannot duplicate within a no-reparse traversal.
                            if (meta.Links > 1 && !seen.Add(meta.Identity)) { snapshot.HardLinksSkipped++; continue; }
                            if (seen.Count > settings.MaxTrackedFileIds || buckets.Count > 250_000) throw new ScanLimitException();
                            var decision = classifier.Classify(entry.FullName);
                            var bucketPath = BucketPath(root, directory);
                            var key = $"{(int)decision.Category}|{bucketPath}";
                            if (!buckets.TryGetValue(key, out var counter)) buckets[key] = counter = new() { Path = bucketPath, Category = decision.Category };
                            counter.Allocated += meta.AllocatedBytes; counter.Logical += meta.LogicalBytes; counter.Files++;
                            snapshot.Files++; allocated += meta.AllocatedBytes;
                            details?.Add(directory, entry.Name, meta.LogicalBytes, meta.AllocatedBytes, entry.LastWriteTimeUtc);
                            apps.Add(directory, entry.Name, meta.AllocatedBytes, decision.Application);
                            if (meta.LogicalBytes >= 50L * 1024 * 1024)
                            {
                                largest.Enqueue(new(entry.FullName, meta.LogicalBytes, meta.AllocatedBytes, decision.Category, decision.Confidence, decision.Evidence), meta.AllocatedBytes);
                                if (largest.Count > 100) largest.Dequeue();
                            }
                            if (snapshot.Files % settings.ScanDelayEveryFiles == 0)
                            {
                                progress?.Report(new(root, directory, snapshot.Files, snapshot.Errors, allocated));
                                if (settings.ScanDelayMs > 0 && token.WaitHandle.WaitOne(settings.ScanDelayMs)) token.ThrowIfCancellationRequested();
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Issue(entry.FullName, ex.Message); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Issue(directory, ex.Message); }
            }
            snapshot.Status = "Complete";
        }
        catch (OperationCanceledException) { snapshot.Status = "Cancelled"; }
        catch (ScanLimitException) { snapshot.Status = "ResourceLimit"; Issue(root, "达到索引 / 目录资源上限，已保存部分结果。请缩小扫描范围。"); }
        catch (Exception ex) { snapshot.Status = "Failed"; Issue(root, ex.Message); }
        try { snapshot.After = NativeMetadata.Sample(root); }
        catch (Exception ex) { Issue(root, "结束容量采样失败：" + ex.Message); }
        if (snapshot.After != null && snapshot.Before != null && snapshot.After.VolumeId != snapshot.Before.VolumeId) { snapshot.Status = "Failed"; Issue(root, "扫描期间卷标识发生变化"); }
        ScanCoverage.Finish(snapshot);
        snapshot.Finished = DateTimeOffset.UtcNow;
        snapshot.Buckets = buckets.Values.Select(x => new Bucket(x.Path, x.Category, x.Allocated, x.Logical, x.Files)).OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Category).ToList();
        snapshot.LargestFiles = largest.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.AllocatedBytes).ToList();
        snapshot.FileDetails = snapshot.Status == "Complete" ? details : null;
        snapshot.Apps = apps.ToList(); snapshot.AppRulesKey = AppAttribution.RulesKey(settings);
        return snapshot;
    }
    /// <summary>统计桶路径：扫描根目录下最多 4 层目录。目录遍历和 MFT 快速扫描共用，保证口径一致。</summary>
    public static string BucketPath(string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return relative.Length == 1 && relative[0] == "." ? root : Path.Combine(new[] { root }.Concat(relative.Take(4)).ToArray());
    }
    private sealed class ScanLimitException : Exception;
}
