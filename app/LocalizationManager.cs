using System.Globalization;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace XenoHavenModToolkit;

internal enum AppLanguage
{
    ZhCn,
    En
}

/// <summary>
/// 应用 UI 语言：用 ResourceDictionary 切换，XAML 用 DynamicResource，代码用 <see cref="Loc"/>。
/// </summary>
internal static class LocalizationManager
{
    private const string LanguagePathMarker = "Languages/";

    public static AppLanguage Current { get; private set; } = AppLanguage.ZhCn;

    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// 解析配置中的语言。null / 空 / system / auto → 跟随系统 UI 语言。
    /// </summary>
    public static AppLanguage Resolve(string? settingsValue)
    {
        if (string.IsNullOrWhiteSpace(settingsValue) ||
            settingsValue.Equals("system", StringComparison.OrdinalIgnoreCase) ||
            settingsValue.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return DetectFromSystem();
        }

        return Parse(settingsValue);
    }

    /// <summary>根据系统 UI 区域选择应用语言（中文系→中文，其它→英语）。</summary>
    public static AppLanguage DetectFromSystem()
    {
        try
        {
            for (var culture = CultureInfo.CurrentUICulture;
                 culture is not null && !Equals(culture, CultureInfo.InvariantCulture);
                 culture = culture.Parent)
            {
                if (culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return AppLanguage.ZhCn;
                }

                if (culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
                {
                    return AppLanguage.En;
                }
            }
        }
        catch
        {
            // fall through
        }

        return AppLanguage.En;
    }

    public static AppLanguage Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DetectFromSystem();
        }

        var normalized = value.Trim().Replace('_', '-');
        if (normalized.Equals("en", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.En;
        }

        if (normalized.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.ZhCn;
        }

        return Enum.TryParse<AppLanguage>(normalized, ignoreCase: true, out var lang)
            ? lang
            : DetectFromSystem();
    }

    public static string ToSettingsValue(AppLanguage language)
        => language switch
        {
            AppLanguage.En => "en",
            _ => "zh-CN"
        };

    public static string ToDisplayName(AppLanguage language)
        => language switch
        {
            AppLanguage.En => "English",
            _ => "中文"
        };

    public static void ApplyLanguage(AppLanguage language)
    {
        Current = language;
        var culture = language == AppLanguage.En
            ? new CultureInfo("en")
            : new CultureInfo("zh-CN");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var dictionaries = WpfApplication.Current.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source is not null && source.Contains(LanguagePathMarker, StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        var file = language == AppLanguage.En ? "en" : "zh-CN";
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Languages/{file}.xaml", UriKind.Relative)
        });

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string GetString(string key)
    {
        if (WpfApplication.Current?.TryFindResource(key) is string text)
        {
            return text;
        }

        return key;
    }
}

/// <summary>代码侧取本地化字符串。</summary>
internal static class Loc
{
    public static string Get(string key) => LocalizationManager.GetString(key);

    public static string Format(string key, params object[] args)
        => string.Format(CultureInfo.CurrentUICulture, Get(key), args);
}
