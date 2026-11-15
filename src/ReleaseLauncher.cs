using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("DiskWatch")]
[assembly: AssemblyProduct("DiskWatch")]
[assembly: AssemblyCompany("neoaui")]
[assembly: AssemblyCopyright("Copyright (C) 2026 neoaui")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0")]
[assembly: AssemblyDescription("Windows disk space monitor")]

/// <summary>
/// 发布包入口：自身只负责每次启动请求一次管理员权限，再启动 app\DiskWatch.App.exe。
/// 用户点「否」或请求失败时，以普通权限启动程序。
/// </summary>
internal static class ReleaseLauncher
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string root = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string app = Path.Combine(root, @"app\DiskWatch.App.exe");
            if (!File.Exists(app)) throw new FileNotFoundException("找不到 DiskWatch 程序文件。请把压缩包完整解压到一个空文件夹后，再双击 DiskWatch.exe。");
            bool declined = false;
            if (LaunchPolicy.Decide(IsAdministrator(), InstanceRunning(), args) == LaunchAction.RequestElevation)
            {
                var self = new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, LaunchPolicy.ElevatedLauncherArguments(args));
                self.UseShellExecute = true;
                self.Verb = "runas";
                self.WorkingDirectory = root;
                try { if (Process.Start(self) == null) throw new InvalidOperationException("未创建提权进程。"); return; }
                catch (Win32Exception e) { declined = true; Trace.WriteLine(LaunchPolicy.FallbackReason(e.NativeErrorCode)); }
                catch (InvalidOperationException) { declined = true; }
            }
            var start = new ProcessStartInfo(app);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WorkingDirectory = Path.GetDirectoryName(app);
            string extra = LaunchPolicy.AppArguments(args, declined);
            if (extra.Length > 0) start.Arguments = extra;
            if (Process.Start(start) == null) throw new InvalidOperationException("未能启动 DiskWatch。");
        }
        catch (Exception e) { MessageBox.Show(e.Message, "DiskWatch 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static bool IsAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool InstanceRunning()
    {
        try
        {
            Mutex existing;
            if (Mutex.TryOpenExisting(@"Local\DiskWatch2.CurrentUserSession", out existing))
            {
                existing.Dispose();
                return true;
            }
            return false;
        }
        catch (UnauthorizedAccessException) { return true; }
    }
}
