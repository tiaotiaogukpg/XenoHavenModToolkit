namespace XenoHavenModToolkit;

/// <summary>
/// 与游戏 <c>ModBuildingXML.GetHashId()</c> 一致：对 <paramref name="uuid"/> 取哈希。
/// Unity/Mono 上 <see cref="string.GetHashCode()"/> 使用 .NET Framework 兼容算法；
/// 桌面 .NET Core+ 默认实现可能随机化，故工具侧使用相同兼容算法。
/// </summary>
internal static class ModBuildingHash
{
    public static int GetHashId(string uuid)
    {
        if (string.IsNullOrEmpty(uuid))
        {
            return 0;
        }

        return LegacyNetStringHash(uuid);
    }

    /// <summary>
    /// 与 .NET Framework / Mono <see cref="string.GetHashCode()"/> 相同的字符串哈希。
    /// </summary>
    internal static int LegacyNetStringHash(string value)
    {
        unchecked
        {
            var hash1 = 5381;
            var hash2 = hash1;

            for (var i = 0; i < value.Length; i++)
            {
                hash1 = ((hash1 << 5) + hash1) ^ value[i];
                i++;
                if (i >= value.Length)
                {
                    break;
                }

                hash2 = ((hash2 << 5) + hash2) ^ value[i];
            }

            return hash1 + (hash2 * 1566083941);
        }
    }
}
