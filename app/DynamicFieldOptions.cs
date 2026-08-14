namespace XenoHavenModToolkit;

/// <summary>
/// Thing/Dynamic（生物类）字段规则，与 Thing/Buildings 同级；当前仅 FARMING_GOLEM。
/// 原版管线：制造床 303678 + 材料 100954×15 / 100412×15 / 303648×5 → CreateMonster。
/// </summary>
internal static class DynamicFieldOptions
{
    internal static readonly string[] DefaultTypes =
    [
        "FARMING_GOLEM"
    ];

    internal static readonly int[] DefaultDirections = [1];

    internal const int FixedHealth = 10;

    /// <summary>原版 Creature Prefab placeWidth/Height（scale=1 时的占地基准）。</summary>
    internal const int BasePlaceSizeX = 1;
    internal const int BasePlaceSizeY = 1;

    /// <summary>兼容旧名：原版基准占地 X。</summary>
    internal const int FixedPlaceSizeX = BasePlaceSizeX;

    /// <summary>兼容旧名：原版基准占地 Y。</summary>
    internal const int FixedPlaceSizeY = BasePlaceSizeY;

    /// <summary>原版农业傀儡制造床 ID（仅作新建默认建议；生产地可选任意工作台）。</summary>
    internal const int FarmingGolemWorkbenchId = 303678;

    internal const string FarmingGolemWorkbenchName = "农业傀儡制造床";

    /// <summary>原版 CapsuleCollider2D 参考尺寸（宽×高，仅说明用）。</summary>
    internal const string FarmingGolemColliderHint =
        "原版基准占地 1×1；size 随 scale 按比例取整。碰撞默认开启。";

    /// <summary>相对原版基准占地的默认比例（1 = 与原版 placeWidth/Height 一致）。</summary>
    internal const double DefaultVisualScale = 1.0;

    internal const double MinVisualScale = 0.1;
    internal const double MaxVisualScale = 4.0;

    /// <summary>原版 Build Formula 材料：铁锭 / 钢零件 / 智能元件。</summary>
    internal static IReadOnlyList<(int Id, int Count)> FarmingGolemDefaultMaterials { get; } =
    [
        (100954, 15),
        (100412, 15),
        (303648, 5)
    ];

    internal static LabeledIdOption FarmingGolemWorkbenchOption { get; } =
        new(FarmingGolemWorkbenchName, FarmingGolemWorkbenchId);

    internal static bool IsFarmingGolem(string type)
        => string.Equals(type, "FARMING_GOLEM", StringComparison.OrdinalIgnoreCase);

    internal static bool RequiresFixedDirection(string type)
        => IsFarmingGolem(type);

    internal static bool RequiresSimulateId(string type)
        => IsFarmingGolem(type);

    /// <summary>size 不由手填，而由原版基准占地 × scale 推导。</summary>
    internal static bool UsesScaledPlaceSize(string type)
        => IsFarmingGolem(type);

    /// <summary>旧名兼容。</summary>
    internal static bool RequiresFixedPlaceSize(string type)
        => UsesScaledPlaceSize(type);

    internal static bool ShowsCapbility(string type)
        => false;

    internal static bool ShowsVisualScale(string type)
        => IsFarmingGolem(type);

    internal static bool DefaultBarrier(string type)
        => IsFarmingGolem(type);

    /// <summary>按原版农业傀儡占地（1×1）与 scale 计算 size.x / size.y（至少为 1）。</summary>
    internal static (int X, int Y) ComputeScaledPlaceSize(double scale)
    {
        var ratio = Math.Clamp(scale, MinVisualScale, MaxVisualScale);
        return (ScaleAxis(BasePlaceSizeX, ratio), ScaleAxis(BasePlaceSizeY, ratio));
    }

    private static int ScaleAxis(int baseSize, double ratio)
        => Math.Max(1, (int)Math.Round(baseSize * ratio, MidpointRounding.AwayFromZero));

    internal static IReadOnlyList<MainWindow.CraftMaterial> CreateDefaultMaterials()
        => FarmingGolemDefaultMaterials
            .Select(m => new MainWindow.CraftMaterial(m.Id, m.Count))
            .ToArray();
}
