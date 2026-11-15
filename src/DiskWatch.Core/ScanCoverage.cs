using System.Security.Cryptography;
using System.Text;

namespace DiskWatch.Core;

public static class ScanCoverage
{
    public const string Version = "coverage-v2";
    public static string Key(Settings settings) => Version + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", settings.ExcludedPaths.Select(Classifier.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)))));

    public static void Finish(Snapshot s)
    {
        if (s.Status == "Complete" && s.Errors > 0) s.Status = "Partial";
    }

    public static bool Verified(Snapshot s) => s.IsComparable && s.HardLinksSkipped == 0 && s.ReparsePointsSkipped == 0 && !string.IsNullOrWhiteSpace(s.VolumeId)
        && s.CoverageKey.StartsWith(Version + ":", StringComparison.Ordinal);
}
