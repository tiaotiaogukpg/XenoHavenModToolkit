namespace XenoHavenModToolkit;

internal static class BuildingFieldOptions
{
    internal static readonly string[] DefaultTypes =
    [
        "BOX",
        "SIMPLE_OBJECT",
        "SMALL_LAMP",
        "STREET_LIGHT",
        "PRODUCTION_LINE"
    ];

    internal static readonly int[] DefaultDirections =
    [
        1,
        3
    ];

    internal const int MinCapbility = 16;
    internal const int MaxCapbility = 96;
    internal const int DefaultCapbility = 16;
    internal const int FixedHealth = 10;

    internal static bool RequiresFixedDirection(string type)
        => string.Equals(type, "BOX", StringComparison.OrdinalIgnoreCase) ||
           IsSimpleDecorType(type);

    internal static bool IsProductionLine(string type)
        => string.Equals(type, "PRODUCTION_LINE", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSimpleDecorType(string type)
        => string.Equals(type, "SIMPLE_OBJECT", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(type, "SMALL_LAMP", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(type, "STREET_LIGHT", StringComparison.OrdinalIgnoreCase);

    internal static bool RequiresSimulateId(string type)
        => IsProductionLine(type);

    internal static bool ShowsCapbility(string type)
        => !IsSimpleDecorType(type) &&
           !IsProductionLine(type);

    /// <summary>
    /// BOX 默认开启碰撞；其它类型（含灯）默认关闭，由编辑窗 On/Off 滑块控制。
    /// </summary>
    internal static bool DefaultBarrier(string type)
        => string.Equals(type, "BOX", StringComparison.OrdinalIgnoreCase);
}
