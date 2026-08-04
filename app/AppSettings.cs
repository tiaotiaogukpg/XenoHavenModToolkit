using System.IO;
using System.Text.Json;

namespace XenoHavenModToolkit;

internal sealed class AppSettings
{
    public string? LastModPath { get; set; }
    public bool OpenLastModOnStartup { get; set; } = true;
    public string Theme { get; set; } = nameof(AppTheme.Light);
    /// <summary>UI 语言：zh-CN / en</summary>
    public string Language { get; set; } = "zh-CN";

    public static string GetConfigFilePath() => AppPaths.GetConfigFilePath();

    public static AppSettings Load()
    {
        try
        {
            var path = GetConfigFilePath();
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            // 旧字段（如 ModsOverviewRoot）不在模型中，反序列化时会被忽略。
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// 保存配置。成功返回 null；失败返回可读错误信息（含完整路径）。
    /// </summary>
    public string? TrySave()
    {
        var path = GetConfigFilePath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            return null;
        }
        catch (Exception ex)
        {
            return Loc.Format("Str.Settings.SaveFailed", path, ex.Message);
        }
    }
}
