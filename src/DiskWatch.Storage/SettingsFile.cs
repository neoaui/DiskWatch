using System.Text.Json;
using DiskWatch.Core;

namespace DiskWatch.Storage;

public static class SettingsFile
{
    public static Settings Load(string path)
    {
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Repository.JsonOptions) ?? throw new InvalidDataException("设置为空") : new Settings();
        settings.Validate(); return settings;
    }
    public static void Save(string path, Settings settings)
    {
        settings.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Repository.JsonOptions), new System.Text.UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
