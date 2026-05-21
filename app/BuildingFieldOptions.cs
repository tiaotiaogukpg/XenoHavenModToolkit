namespace XenoHavenModToolkit;

internal static class BuildingFieldOptions
{
    internal static readonly string[] DefaultTypes =
    [
        "BOX",
        "SIMPLE_OBJECT",
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
}
