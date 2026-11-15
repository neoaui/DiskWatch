using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

internal static class Launcher
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string root = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string host = FindDotNetHost(root);
            string dll = FindAppDll(root);
            if (host == null || dll == null) throw new FileNotFoundException("找不到 .NET 运行时或 DiskWatch 程序文件。请先安装 .NET 10 运行时，或运行 build.ps1 -Publish。");
            bool declined = false;
            if (LaunchPolicy.Decide(IsAdministrator(), InstanceRunning(), args) == LaunchAction.RequestElevation)
            {
                // 每次启动请求一次管理员权限。点「否」或失败时改用普通权限启动，程序会自动使用普通扫描。
                var self = new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, LaunchPolicy.ElevatedLauncherArguments(args)) { UseShellExecute = true, Verb = "runas", WorkingDirectory = root };
                try { if (Process.Start(self) == null) throw new InvalidOperationException("未创建提权进程。"); return; }
                catch (Win32Exception e) { declined = true; Trace.WriteLine(LaunchPolicy.FallbackReason(e.NativeErrorCode)); }
                catch (InvalidOperationException) { declined = true; }
            }
            var start = new ProcessStartInfo(host, "\"" + dll + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
            string extra = LaunchPolicy.AppArguments(args, declined);
            if (extra.Length > 0) start.Arguments += " " + extra;
            start.EnvironmentVariables["DOTNET_ROOT"] = Path.GetDirectoryName(host);
            start.EnvironmentVariables["DOTNET_ROOT_X64"] = Path.GetDirectoryName(host);
            start.EnvironmentVariables["DOTNET_CLI_HOME"] = Path.Combine(root, @"work\cli");
            start.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.EnvironmentVariables["TEMP"] = Path.Combine(root, @"work\temp");
            start.EnvironmentVariables["TMP"] = Path.Combine(root, @"work\temp");
            Process.Start(start);
        }
        catch (Exception e) { MessageBox.Show(e.Message, "DiskWatch2 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string FindDotNetHost(string root)
    {
        string env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(env))
        {
            string p = Path.Combine(env, "dotnet.exe");
            if (File.Exists(p)) return p;
        }
        string bundled = Path.Combine(root, @"runtime\dotnet.exe");
        if (File.Exists(bundled)) return bundled;
        string pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"dotnet\dotnet.exe");
        if (File.Exists(pf)) return pf;
        string path = Environment.GetEnvironmentVariable("PATH");
        if (path != null)
        {
            string[] dirs = path.Split(Path.PathSeparator);
            for (int i = 0; i < dirs.Length; i++)
            {
                if (string.IsNullOrEmpty(dirs[i])) continue;
                string cand = Path.Combine(dirs[i].Trim(), "dotnet.exe");
                if (File.Exists(cand)) return cand;
            }
        }
        return null;
    }

    private static string FindAppDll(string root)
    {
        string[] candidates =
        {
            Path.Combine(root, @"release\net10\DiskWatch.App.dll"),
            Path.Combine(root, @"release\net10.0-windows\DiskWatch.App.dll"),
            Path.Combine(root, "DiskWatch.App.dll"),
            Path.Combine(root, @"app\DiskWatch.App.dll")
        };
        for (int i = 0; i < candidates.Length; i++)
            if (File.Exists(candidates[i])) return candidates[i];
        return null;
    }

    private static bool IsAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>已有实例在运行时不再请求提权，交给程序原有的单实例提示。</summary>
    private static bool InstanceRunning()
    {
        try { Mutex existing; if (Mutex.TryOpenExisting(@"Local\DiskWatch2.CurrentUserSession", out existing)) { existing.Dispose(); return true; } return false; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
