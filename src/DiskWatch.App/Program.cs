using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DiskWatch.Core;
using DiskWatch.Storage;
using DiskWatch.Windows;

namespace DiskWatch.App;

public static class Program
{
    public static readonly string Root = ResolveRoot();
    public static readonly string SettingsPath = Path.Combine(Root, "data", "settings.json");

    private static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable("DISKWATCH_ROOT");
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env.Trim());
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DiskWatch2.sln")) ||
                File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
                return dir.FullName;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskWatch");
    }
    /// <summary>启动器传入：本次启动时用户拒绝了管理员确认（或请求失败），以普通权限运行。</summary>
    public static bool ElevationDeclined { get; private set; }
    private static readonly object LogGate = new();
    public static void Log(string message)
    {
        lock (LogGate)
        {
            try
            {
                var path = Path.Combine(Root, "logs", "app.log"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch { /* Logging must not crash the UI on a full/offline disk. */ }
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        ElevationDeclined = args.Contains("--elevation-declined");
        try
        {
            Mutex mutex;
            bool first;
            try { mutex = new Mutex(true, @"Local\DiskWatch2.CurrentUserSession", out first); }
            catch (UnauthorizedAccessException) { MessageBox.Show("DiskWatch2 已在运行（权限不同），请从托盘打开已有窗口。", "DiskWatch2"); return 2; }
            using var instanceMutex = mutex;
            if (!first) { MessageBox.Show("DiskWatch2 已在运行，请从托盘打开。", "DiskWatch2"); return 2; }
            Directory.CreateDirectory(Path.Combine(Root, "work", "temp"));
            Environment.SetEnvironmentVariable("TEMP", Path.Combine(Root, "work", "temp"));
            Environment.SetEnvironmentVariable("TMP", Path.Combine(Root, "work", "temp"));
            var settings = SettingsFile.Load(SettingsPath);
            if (!File.Exists(SettingsPath)) SettingsFile.Save(SettingsPath, settings);
            var repository = new Repository(Path.Combine(Root, "data"), settings);
            if (args.Length >= 2 && args[0] == "--scan")
            {
                var context = ApplicationDiscovery.Discover(settings);
                var scanner = new Scanner(new Classifier(context), settings, RuleVersion(settings));
                repository.SetState("scan:active", args[1] + " @ " + DateTimeOffset.Now.ToString("O"));
                var lastProgress = DateTimeOffset.MinValue;
                var progress = new CallbackProgress(p =>
                {
                    if (DateTimeOffset.UtcNow - lastProgress < TimeSpan.FromSeconds(10)) return;
                    lastProgress = DateTimeOffset.UtcNow;
                    Log($"CLI 进度 {p.Root}: {p.Files:N0} 文件, {Labels.Size(p.AllocatedBytes)}, {p.Errors} 错误");
                });
                var result = scanner.ScanAsync(args[1], progress, CancellationToken.None).GetAwaiter().GetResult();
                repository.SaveSnapshot(result);
                if (result.FileDetails is { } details && ScanCoverage.Verified(result))
                {
                    try { var bytes = repository.SaveFileIndex(result, details); Log($"CLI 文件级明细 {result.Root}: {details.Files.Count:N0} 个文件, 压缩后 {Labels.SmartSize(bytes)}"); }
                    catch (Exception ex) { Log("CLI 文件级明细保存失败：" + ex.Message); }
                    result.FileDetails = null;
                }
                Log($"CLI 扫描 {result.Root}: {result.Coverage}, 文件 {result.Files}");
                repository.SetState("scan:active", "");
                return result.Status == "Complete" ? 0 : 1;
            }
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var window = new MainWindow(settings, repository, args.Contains("--smoke-ui"), args.Contains("--ui-review"));
            application.DispatcherUnhandledException += (_, e) => { Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "DiskWatch2 错误"); e.Handled = true; };
            application.Run(window);
            return 0;
        }
        catch (Exception ex) { Log(ex.ToString()); MessageBox.Show(ex.Message, "DiskWatch2 启动失败"); return 1; }
    }
    public static string RuleVersion(Settings settings) => "builtin-1:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { settings.Overrides, settings.ExcludedPaths, settings.ReadInstallationMetadata }))))[..12];
    private sealed class CallbackProgress(Action<ScanProgress> callback) : IProgress<ScanProgress> { public void Report(ScanProgress value) => callback(value); }
}
