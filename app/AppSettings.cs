using System.IO;
using System.Text.Json;

namespace XenoHavenModToolkit;

internal sealed class AppSettings
{
    public string? ModsOverviewRoot { get; set; }
    public string? LastModPath { get; set; }
    public bool OpenLastModOnStartup { get; set; } = true;
    public string Theme { get; set; } = nameof(AppTheme.Light);

    public static string GetSettingsFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XenoHavenModTool");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            var path = GetSettingsFilePath();
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var path = GetSettingsFilePath();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}

