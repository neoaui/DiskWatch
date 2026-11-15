using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;

// C# 5：与独立 .NET Framework 启动器和测试共用，不依赖应用 DLL。
internal static class SecureLaunchPolicy
{
    public static readonly string InstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DiskWatch");
    public static readonly string WorkRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskWatch");

    public static bool IsDeploymentLocation(string directory)
    {
        return Path.GetFullPath(directory).TrimEnd('\\').Equals(InstallRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInjectionVariable(string name)
    {
        return name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DEVPATH", StringComparison.OrdinalIgnoreCase)
            || name.Equals("__COMPAT_LAYER", StringComparison.OrdinalIgnoreCase);
    }

    public static void RemoveInjectionVariables(StringDictionary environment)
    {
        var keys = new string[environment.Count]; environment.Keys.CopyTo(keys, 0);
        foreach (var key in keys) if (IsInjectionVariable(key)) environment.Remove(key);
    }

    public static void CleanCurrentProcessEnvironment()
    {
        // runas 的 ShellExecute 不允许显式环境块：先清理当前进程环境，再派生提升后的启动器。
        // 仅修改本进程，不写用户/系统环境变量或注册表。
        foreach (DictionaryEntry pair in Environment.GetEnvironmentVariables())
            if (IsInjectionVariable((string)pair.Key)) Environment.SetEnvironmentVariable((string)pair.Key, null);
    }

    public static ProcessStartInfo CreateAppStart(string root, string windowsRoot, string[] args, bool declined)
    {
        root = Path.GetFullPath(root);
        string runtime = Path.Combine(root, "runtime");
        string app = Path.Combine(root, @"app\DiskWatch.App.dll");
        var start = new ProcessStartInfo(Path.Combine(runtime, "dotnet.exe"), "\"" + app + "\"")
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
        string extra = LaunchPolicy.AppArguments(args, declined);
        if (extra.Length != 0) start.Arguments += " " + extra;
        ConfigureEnvironment(start.EnvironmentVariables, root, windowsRoot);
        return start;
    }

    public static void ConfigureEnvironment(StringDictionary environment, string root, string windowsRoot)
    {
        RemoveInjectionVariables(environment);
        string runtime = Path.Combine(root, "runtime");
        environment["DOTNET_ROOT"] = runtime;
        environment["DOTNET_ROOT_X64"] = runtime;
        environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        environment["DOTNET_CLI_HOME"] = Path.Combine(WorkRoot, "cli");
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_EnableDiagnostics"] = "0";
        environment["CORECLR_ENABLE_PROFILING"] = "0";
        environment["COR_ENABLE_PROFILING"] = "0";
        environment["TEMP"] = Path.Combine(WorkRoot, "temp");
        environment["TMP"] = Path.Combine(WorkRoot, "temp");
        environment["DISKWATCH_ROOT"] = WorkRoot;
        environment["SystemRoot"] = windowsRoot;
        environment["WINDIR"] = windowsRoot;
        environment["COMSPEC"] = Path.Combine(windowsRoot, @"System32\cmd.exe");
        environment["PATH"] = runtime + ";" + Path.Combine(windowsRoot, "System32") + ";" + windowsRoot;
    }
}
