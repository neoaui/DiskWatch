using Microsoft.Win32;
using System.Text.RegularExpressions;
using DiskWatch.Core;

namespace DiskWatch.Windows;

public static partial class ApplicationDiscovery
{
    public static ClassificationContext Discover(Settings settings)
    {
        var result = new ClassificationContext { WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows), Overrides = settings.Overrides.ToList() };
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos })
        {
            var path = Environment.GetFolderPath(folder); if (!string.IsNullOrWhiteSpace(path)) result.PersonalRoots.Add(path);
        }
        using (var shell = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
        {
            // Downloads known-folder GUID; only read this allowlisted configuration value.
            var downloads = shell?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
            if (!string.IsNullOrWhiteSpace(downloads)) result.PersonalRoots.Add(Environment.ExpandEnvironmentVariables(downloads));
        }
        if (!settings.ReadInstallationMetadata) return result;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (var keyName in uninstall.GetSubKeyNames())
                {
                    using var item = uninstall.OpenSubKey(keyName);
                    var name = item?.GetValue("DisplayName") as string;
                    var path = item?.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                    if (!Path.IsPathFullyQualified(path) || Classifier.Normalize(path).Length < 5) continue;
                    result.Applications.Add(new(name, path));
                    if (keyName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase)) result.GameRoots.Add(path);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { /* Optional evidence; fallback remains explicit. */ }
        }
        try
        {
            using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = steam?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                result.GameRoots.Add(Path.Combine(steamPath, "steamapps"));
                var manifest = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(manifest) && new FileInfo(manifest).Length <= 1024 * 1024)
                    foreach (Match match in SteamLibraryPath().Matches(File.ReadAllText(manifest)))
                    {
                        var path = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (Path.IsPathFullyQualified(path)) result.GameRoots.Add(Path.Combine(path, "steamapps"));
                    }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        result.Applications = result.Applications.DistinctBy(x => (x.Name, x.Directory.ToUpperInvariant())).ToList();
        return result;
    }
    [GeneratedRegex("\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamLibraryPath();
}
