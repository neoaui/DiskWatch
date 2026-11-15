using System.IO.Compression;
using DiskWatch.Core;
using DiskWatch.Storage;
using DiskWatch.Windows;

namespace DiskWatch.Tests;

internal static partial class Program
{
    private static FileComparisonContext TestContext() => new("test-volume", @"C:\", "test", ScanCoverage.Key(new Settings()), true);
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("应拒绝无效数据 / 不可比较快照");
    }

    private static async Task SafetyTests()
    {
        await Test("安全启动器：相对运行时路径、独立临时目录与参数白名单", () => Run(() =>
        {
            var start = SecureLaunchPolicy.CreateAppStart(SecureLaunchPolicy.InstallRoot, @"C:\Windows", ["--smoke-ui", "--evil", "--no-elevate"], true);
            Assert(start.FileName == Path.Combine(SecureLaunchPolicy.InstallRoot, "runtime", "dotnet.exe"));
            Assert(start.Arguments == "\"" + Path.Combine(SecureLaunchPolicy.InstallRoot, "app", "DiskWatch.App.dll") + "\" --smoke-ui --elevation-declined");
            Assert(start.CreateNoWindow && !start.UseShellExecute && start.WorkingDirectory == SecureLaunchPolicy.InstallRoot);
            Assert(start.EnvironmentVariables["TEMP"] == Path.Combine(SecureLaunchPolicy.WorkRoot, "temp"));
            Assert(start.EnvironmentVariables["DOTNET_ROOT"] == Path.Combine(SecureLaunchPolicy.InstallRoot, "runtime"));
            Assert(SecureLaunchPolicy.IsDeploymentLocation(SecureLaunchPolicy.InstallRoot + Path.DirectorySeparatorChar));
            Assert(!SecureLaunchPolicy.IsDeploymentLocation(Path.Combine(Path.GetTempPath(), "DiskWatch-stage")));
            Assert(!SecureLaunchPolicy.IsDeploymentLocation(SecureLaunchPolicy.InstallRoot + "-other"));
        }));
        await Test("安全启动器：清除 hooks/deps/store/profiler/兼容层与非预期 PATH", () => Run(() =>
        {
            var environment = new System.Collections.Specialized.StringDictionary();
            string[] injected = ["DOTNET_STARTUP_HOOKS", "dotnet_additional_deps", "DOTNET_SHARED_STORE", "DOTNET_ROOT_X86", "DOTNET_DiagnosticPorts", "COMPLUS_ReadyToRun", "CORECLR_PROFILER_PATH_64", "COR_PROFILER", "DEVPATH", "__COMPAT_LAYER"];
            foreach (var key in injected) environment[key] = @"D:\untrusted";
            environment["PATH"] = @"D:\untrusted"; environment["USERPROFILE"] = @"C:\Users\u";
            SecureLaunchPolicy.ConfigureEnvironment(environment, SecureLaunchPolicy.InstallRoot, @"C:\Windows");
            foreach (var key in injected) Assert(!environment.ContainsKey(key), key + " 未清除");
            Assert(environment["PATH"] == Path.Combine(SecureLaunchPolicy.InstallRoot, "runtime") + @";C:\Windows\System32;C:\Windows");
            Assert(environment["CORECLR_ENABLE_PROFILING"] == "0" && environment["COR_ENABLE_PROFILING"] == "0" && environment["DOTNET_EnableDiagnostics"] == "0");
            Assert(environment["USERPROFILE"] == @"C:\Users\u");
        }));
        await Test("安全：USA 全部校验后才修改，拒绝重复还原和 4Kn 步长猜测", () => Run(() =>
        {
            var record = MftRecord(1, false, Attr(0x80, true, 0, 10));
            record[1022] = 42; var unchanged = record.ToArray();
            Reject(() => MftRecordParser.ApplyFixups(record, record.Length)); Assert(record.SequenceEqual(unchanged));
            record = MftRecord(1, false, Attr(0x80, true, 0, 10));
            Reject(() => MftRecordParser.ApplyFixups(record, record.Length, 4096));
            MftRecordParser.ApplyFixups(record, record.Length);
            Reject(() => MftRecordParser.ApplyFixups(record, record.Length));
            var bad = MftRecord(1, false); bad[6] = 4; Reject(() => MftRecordParser.ApplyFixups(bad, bad.Length));
        }));
        await Test("安全：MFT 属性边界、负大小、序列号和属性列表", () => Run(() =>
        {
            var r = MftRecord(1, false, Attr(0x80, true, 0, 10));
            BitConverter.GetBytes((ushort)17).CopyTo(r, 0x10);
            Assert(MftRecordParser.Parse(r, r.Length).SequenceNumber == 17);
            BitConverter.GetBytes(16u).CopyTo(r, 0x38 + 4); Reject(() => MftRecordParser.Parse(r, r.Length));
            r = MftRecord(1, false, Attr(0x80, true, 0, 10)); r[0x38 + 0x14] = 2;
            Reject(() => MftRecordParser.Parse(r, r.Length));
            Reject(() => MftRecordParser.Parse(new byte[8], 1024));
            Reject(() => MftRecordParser.Parse(MftRecord(1, false, NonResident(4096, -1, 0, 0)), 1024));
            var list = MftRecord(1, false, Attr(0x20, true, 0, 8), NonResident(4096, 10, 0, 0));
            Assert(MftRecordParser.Parse(list, list.Length).NeedsFallback);
            var unsupported = MftRecord(1, false, NonResident(4096, 10, 1, 2));
            Assert(MftRecordParser.Parse(unsupported, unsupported.Length).NeedsFallback);
        }));
        await Test("安全：USN 未知版本、截断和异常名称必须拒绝", () => Run(() =>
        {
            var b = new byte[256]; int length = UsnRecord(b, 8, 3, 30, 5, 0, "x");
            Reject(() => UsnRecordParser.Parse(b, length, []));
            length = UsnRecord(b, 8, 2, 30, 5, 0, "x");
            Reject(() => UsnRecordParser.Parse(b, length - 1, []));
            b[8 + 58] = 1; Reject(() => UsnRecordParser.Parse(b, length, []));
            length = UsnRecord(b, 8, 2, 30, 5, 0, "../x"); Reject(() => UsnRecordParser.Parse(b, length, []));
            length = UsnRecord(b, 8, 2, 0x1000000000005, 0x1000000000005, FileAttributes.Directory, ".");
            var root = new List<UsnEntry>(); UsnRecordParser.Parse(b, length, root); Assert(root.Single().Name == ".");
        }));
        await Test("安全：完整 FRN 防槽复用、父引用序列不符不得串路径", () => Run(() =>
        {
            MftScanner.ValidateIdentity(0x1000000000030, 0x1000000000030);
            Reject(() => MftScanner.ValidateIdentity(0x1000000000030, 0x2000000000030));
            List<UsnEntry> entries = [new(0x1000000000005, 0x1000000000005, FileAttributes.Directory, "."),
                new(0x2000000000030, 0x1000000000005, FileAttributes.Directory, "reused"), new(60, 0x1000000000030, 0, "old.bin")];
            var s = new Snapshot { Root = @"X:\", Status = "Complete" };
            MftAggregator.Aggregate(s, s.Root, s.Root, entries, (_, _) => throw new Exception("不得读取旧路径"), new Classifier(new()), new Settings(), null, default);
            ScanCoverage.Finish(s); Assert(s.Files == 0 && s.Errors == 1 && s.Status == "Partial");
        }));
        await Test("安全：MFT 取消保留自洽部分总量", () => Run(() =>
        {
            using var cts = new CancellationTokenSource();
            List<UsnEntry> entries = [new(5, 5, FileAttributes.Directory, "."), new(30, 5, 0, "a"), new(31, 5, 0, "b")];
            var s = new Snapshot { Root = @"X:\" };
            try { MftAggregator.Aggregate(s, s.Root, s.Root, entries, (_, _) => { cts.Cancel(); return new("id", 1, 4096, 1); }, new Classifier(new()), new Settings(), null, cts.Token); }
            catch (OperationCanceledException) { }
            Assert(s.Files == 1 && s.Buckets.Sum(b => b.Files) == 1 && s.AllocatedBytes == 4096);
        }));
        await Test("安全：不完整覆盖、换卷、规则变化、旧快照不判删除", () => Run(() =>
        {
            var a = new FileIndex { Root = @"C:\", Context = TestContext() }; a.Add(@"C:\", "keep", 1, 1, default);
            var b = new FileIndex { Root = @"C:\", Context = TestContext() };
            Assert(FileDiff.Compare(a, b).Single().Kind == ChangeKind.Removed);
            b.Context = TestContext() with { Complete = false }; Reject(() => FileDiff.Compare(a, b));
            b.Context = TestContext() with { VolumeId = "other" }; Reject(() => FileDiff.Compare(a, b));
            b.Context = TestContext() with { RuleVersion = "test+mft" }; Reject(() => FileDiff.Compare(a, b));
            b.Context = null; Reject(() => FileDiff.Compare(a, b));
            var old = new Snapshot { Status = "Complete", Errors = 1, VolumeId = "test-volume", CoverageKey = ScanCoverage.Key(new Settings()) };
            Assert(!FileComparisonContext.From(old).Complete);
            old.Errors = 0; old.HardLinksSkipped = 1; Assert(!FileComparisonContext.From(old).Complete);
            old.HardLinksSkipped = 0; old.ReparsePointsSkipped = 1; Assert(!FileComparisonContext.From(old).Complete);
        }));
        await Test("安全：读取失败不淘汰基线、换卷禁止配对、覆盖记录随明细落库", () => Run(() =>
        {
            var repo = new Repository(Path.Combine(work, "coverage-db"), new Settings());
            var index = new FileIndex { Root = @"C:\" }; index.Add(@"C:\", "a", 1, 1, default);
            Snapshot S(int minute) => new() { Root = @"C:\", VolumeId = "v1", RuleVersion = "r1", CoverageKey = ScanCoverage.Key(new Settings()), Status = "Complete", Finished = epoch.AddMinutes(minute) };
            var a = S(1); var b = S(2); repo.SaveFileIndex(a, index); repo.SaveFileIndex(b, index);
            var bad = S(3); bad.Errors = 1; Assert(repo.SaveFileIndex(bad, index) == 0);
            Assert(repo.ComparablePair(index.Root)?.After.SnapshotId == b.Id);
            Assert(FileDiff.Compare(repo.LoadFileIndex(a.Id)!, repo.LoadFileIndex(b.Id)!).Count == 0);
            var other = S(4); other.VolumeId = "v2"; repo.SaveFileIndex(other, index); Assert(repo.ComparablePair(index.Root) == null);
            Assert(repo.IntegrityCheck() == "ok");
        }));
        await Test("安全：大小写冲突不静默覆盖", () => Run(() =>
        {
            var a = new FileIndex { Root = @"C:\", Context = TestContext() }; a.Add(@"C:\", "A", 1, 1, default); a.Add(@"C:\", "a", 2, 2, default);
            Reject(() => FileDiff.Compare(a, new FileIndex { Root = @"C:\", Context = TestContext() }));
        }));
        await Test("安全：压缩明细拒绝超量预分配、坏前缀和超长字符串", () => Run(() =>
        {
            byte[] Pack(Action<BinaryWriter> write)
            {
                using var m = new MemoryStream();
                using (var z = new BrotliStream(m, CompressionLevel.Fastest, true)) using (var w = new BinaryWriter(z)) write(w);
                return m.ToArray();
            }
            Reject(() => FileIndex.Deserialize(Pack(w => { w.Write("DWFI1"); w.Write(@"C:\"); w.Write7BitEncodedInt(0); w.Write7BitEncodedInt(FileIndex.MaxRows + 1); })));
            Reject(() => FileIndex.Deserialize(Pack(w => { w.Write("DWFI1"); w.Write(@"C:\"); w.Write7BitEncodedInt(1); w.Write7BitEncodedInt(5); w.Write("x"); })));
            Reject(() => FileIndex.Deserialize(Pack(w => w.Write7BitEncodedInt(int.MaxValue))));
        }));
        await Test("安全：增长目录去重跨越收缩的中间层", () => Run(() =>
        {
            var a = new FileIndex { Root = @"C:\", Context = TestContext() }; var b = new FileIndex { Root = @"C:\", Context = TestContext() };
            a.Add(@"C:\P\Middle", "shrink", 500L << 20, 500L << 20, default);
            b.Add(@"C:\P\Middle\Deep", "growth", 600L << 20, 600L << 20, default);
            b.Add(@"C:\P", "own", 300L << 20, 300L << 20, default);
            var tree = ChangeTree.Build(a.Root, a, b, FileDiff.Compare(a, b));
            Assert(ChangeTree.Summarize(tree, new()).SignificantDirectories == 1);
        }));
    }
}
