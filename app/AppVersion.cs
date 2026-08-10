using System.Reflection;

namespace XenoHavenModToolkit;

/// <summary>
/// 应用版本信息：以 csproj 的 Version / InformationalVersion 为准。
/// </summary>
internal static class AppVersion
{
    private static readonly Lazy<string> LazyDisplay = new(ReadDisplayVersion);

    public static string Display => LazyDisplay.Value;

    public static string ProductName
    {
        get
        {
            var product = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyProductAttribute>()
                ?.Product;
            return string.IsNullOrWhiteSpace(product) ? "XenoHavenModTool" : product;
        }
    }

    public static string WindowTitle => $"{ProductName} {Display}";

    private static string ReadDisplayVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // 去掉可能附带的 +git SHA 等后缀，便于核对主版本号
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        if (version is null)
        {
            return "0.0.0";
        }

        if (version.Revision <= 0 && version.Build <= 0)
        {
            return $"{version.Major}.{version.Minor}";
        }

        if (version.Revision <= 0)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        return version.ToString();
    }
}
