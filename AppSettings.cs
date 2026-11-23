using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClickFx;

internal sealed class AppSettings
{
    public bool Effects { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool KeySound { get; set; } = true;
    public int Volume { get; set; } = 40;
    public string Style { get; set; } = "spark";
    public string SoundPack { get; set; } = "blue";
    public bool StartWithWindows { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "click-fx",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
            if (loaded is null)
                return new AppSettings();
            loaded.Volume = Math.Clamp(loaded.Volume, 0, 100);
            loaded.Style = "spark";
            loaded.SoundPack = "blue";
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Volume = Math.Clamp(Volume, 0, 100);
        Style = "spark";
        SoundPack = "blue";
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
