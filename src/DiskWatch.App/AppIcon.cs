using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace DiskWatch.App;

internal static class AppIcon
{
    private static Stream Open() => typeof(AppIcon).Assembly.GetManifestResourceStream("DiskWatch.Icon")
        ?? throw new InvalidOperationException("缺少内嵌程序图标。");

    internal static BitmapFrame WindowIcon()
    {
        using var stream = Open();
        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        return bitmap;
    }

    internal static System.Drawing.Icon TrayIcon()
    {
        using var stream = Open();
        using var icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }
}
