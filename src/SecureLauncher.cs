using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

internal static class SecureLauncher
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string selfPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string root = Path.GetDirectoryName(selfPath);
            // 暂存包不能通过双击触发管理员执行；只有部署后的固定入口可运行。
            if (!SecureLaunchPolicy.IsDeploymentLocation(root))
                throw new InvalidOperationException("这是待审阅的暂存启动器。完成部署后，请从正式安装目录打开。");
            if (!File.Exists(Path.Combine(root, @"runtime\dotnet.exe")) || !File.Exists(Path.Combine(root, @"app\DiskWatch.App.dll")))
                throw new FileNotFoundException("受保护目录中的运行时或程序文件缺失。");
            SecureLaunchPolicy.CleanCurrentProcessEnvironment();
            bool declined = false;
            if (LaunchPolicy.Decide(IsAdministrator(), InstanceRunning(), args) == LaunchAction.RequestElevation)
            {
                var self = new ProcessStartInfo(selfPath, LaunchPolicy.ElevatedLauncherArguments(args))
                    { UseShellExecute = true, Verb = "runas", WorkingDirectory = root };
                try { if (Process.Start(self) == null) throw new InvalidOperationException("未创建提权进程。"); return; }
                catch (Win32Exception e) { declined = true; Trace.WriteLine(LaunchPolicy.FallbackReason(e.NativeErrorCode)); }
                catch (InvalidOperationException) { declined = true; }
            }
            Directory.CreateDirectory(Path.Combine(SecureLaunchPolicy.WorkRoot, "temp"));
            Directory.CreateDirectory(Path.Combine(SecureLaunchPolicy.WorkRoot, "cli"));
            var start = SecureLaunchPolicy.CreateAppStart(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows), args, declined);
            if (Process.Start(start) == null) throw new InvalidOperationException("未能启动 DiskWatch2。");
        }
        catch (Exception e) { MessageBox.Show(e.Message, "DiskWatch2 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static bool IsAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    private static bool InstanceRunning()
    {
        try { Mutex existing; if (!Mutex.TryOpenExisting(@"Local\DiskWatch2.CurrentUserSession", out existing)) return false; existing.Dispose(); return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
}
