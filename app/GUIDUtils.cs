namespace XenoHavenModToolkit;

/// <summary>
/// 与游戏 <c>Darwin III/Assets/Scripts/Utils/GUIDUtils.cs</c> 一致。
/// </summary>
internal static class GUIDUtils
{
    public static string Generate() => Guid.NewGuid().ToString("N");
}
