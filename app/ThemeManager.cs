using System.Windows;
using WpfApplication = System.Windows.Application;

namespace XenoHavenModToolkit;

internal enum AppTheme
{
    Light,
    Dark
}

internal static class ThemeManager
{
    private const string ThemePathMarker = "Themes/";

    public static AppTheme Parse(string? value)
    {
        return Enum.TryParse<AppTheme>(value, ignoreCase: true, out var theme)
            ? theme
            : AppTheme.Light;
    }

    public static void ApplyTheme(AppTheme theme)
    {
        var dictionaries = WpfApplication.Current.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source is not null && source.Contains(ThemePathMarker, StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative)
        });
    }
}
