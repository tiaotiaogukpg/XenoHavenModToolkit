namespace XenoHavenModToolkit;

internal static class FarmingGolemOptions
{
    internal static readonly IReadOnlyList<LabeledIdOption> SimulateOptions =
    [
        new LabeledIdOption("伐木傀儡", 20191),
        new LabeledIdOption("采矿傀儡", 20192),
        new LabeledIdOption("收割傀儡", 20193)
    ];

    internal static int DefaultSimulateId => SimulateOptions[0].Id;

    internal static bool IsKnownSimulateId(int id)
        => SimulateOptions.Any(option => option.Id == id);

    internal static LabeledIdOption? Find(int id)
        => SimulateOptions.FirstOrDefault(option => option.Id == id);
}
