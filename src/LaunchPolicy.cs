using System;
using System.Collections.Generic;

/// <summary>
/// 启动器的提权决策（纯逻辑，C# 5 语法，供 .NET Framework csc 编译的启动器和单元测试共用）。
/// 每次启动请求一次管理员权限；用户点「否」或请求失败时改用普通权限启动，不报错、不退出。
/// </summary>
internal enum LaunchAction { StartDirect, RequestElevation }

internal static class LaunchPolicy
{
    public const int ErrorCancelled = 1223;
    /// <summary>提权后的启动器实例带此参数，直接启动程序，避免循环请求。</summary>
    public const string ElevatedFlag = "--elevated-launch";
    /// <summary>手动跳过提权（例如调试）。</summary>
    public const string NoElevateFlag = "--no-elevate";
    /// <summary>传给程序：本次用户拒绝或未能获得管理员权限。</summary>
    public const string DeclinedFlag = "--elevation-declined";
    private static readonly string[] PassThrough = { "--ui-review", "--smoke-ui" };

    public static LaunchAction Decide(bool isAdministrator, bool instanceRunning, string[] args)
    {
        if (isAdministrator || instanceRunning) return LaunchAction.StartDirect;
        foreach (var a in args) if (a == ElevatedFlag || a == NoElevateFlag) return LaunchAction.StartDirect;
        return LaunchAction.RequestElevation;
    }

    /// <summary>请求提权失败后的说明；无论哪种错误都改用普通权限启动。</summary>
    public static string FallbackReason(int nativeErrorCode)
    {
        return nativeErrorCode == ErrorCancelled ? "用户在管理员确认窗口中选择了「否」" : "请求管理员权限失败（错误 " + nativeErrorCode + "）";
    }

    /// <summary>以管理员身份重新运行启动器时的参数。</summary>
    public static string ElevatedLauncherArguments(string[] args)
    {
        var list = new List<string> { ElevatedFlag };
        foreach (var a in args) if (Array.IndexOf(PassThrough, a) >= 0) list.Add(a);
        return string.Join(" ", list.ToArray());
    }

    /// <summary>传给 DiskWatch.App 的附加参数。</summary>
    public static string AppArguments(string[] args, bool declined)
    {
        var list = new List<string>();
        foreach (var a in args) if (Array.IndexOf(PassThrough, a) >= 0) list.Add(a);
        if (declined) list.Add(DeclinedFlag);
        return string.Join(" ", list.ToArray());
    }
}