namespace XenoHavenModToolkit;

internal static class BuildingFieldOptions
{
    internal static readonly string[] DefaultTypes =
    [
        "BOX",
        "SIMPLE_OBJECT"
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
           string.Equals(type, "SIMPLE_OBJECT", StringComparison.OrdinalIgnoreCase);

    internal static bool ShowsCapbility(string type)
        => !string.Equals(type, "SIMPLE_OBJECT", StringComparison.OrdinalIgnoreCase);
}
