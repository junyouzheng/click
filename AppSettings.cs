using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClickFx;

internal sealed class AppSettings
{
    public bool Effects { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool KeySound { get; set; } = true;
    public string Style { get; set; } = "spark";
    public string SoundPack { get; set; } = "blue";
    public bool StartWithWindows { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(Root, "click", "settings.json");

    private static string LegacyFilePath => Path.Combine(Root, "click-fx", "settings.json");

    private static string Root =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static AppSettings Load()
    {
        try
        {
            var migrated = false;
            string? path = null;
            if (File.Exists(FilePath))
                path = FilePath;
            else if (File.Exists(LegacyFilePath))
            {
                path = LegacyFilePath;
                migrated = true;
            }

            if (path is null)
                return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            if (loaded is null)
                return new AppSettings();
            loaded.Style = "spark";
            loaded.SoundPack = "blue";
            if (migrated)
                loaded.Save();
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Style = "spark";
        SoundPack = "blue";
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
