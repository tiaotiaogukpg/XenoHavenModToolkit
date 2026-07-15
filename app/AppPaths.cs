using System.IO;

namespace XenoHavenModToolkit;

/// <summary>
/// 应用路径的唯一权威实现：开发模式指向仓库根，生产模式指向可执行文件目录。
/// </summary>
internal static class AppPaths
{
    private const string ModsFolderName = "Mods";
    private const string ConfigFileName = "config.json";
    private const string SolutionMarkerFileName = "XenoHavenModToolkit.slnx";
    private const int MaxWalkUpLevels = 8;

    private static readonly Lazy<ResolvedPaths> Resolved = new(Resolve);

    internal static bool IsDevelopmentMode => Resolved.Value.IsDevelopment;

    internal static string RunModeLabel => IsDevelopmentMode ? "development" : "production";

    internal static string GetApplicationBaseDirectory() => Resolved.Value.BaseDirectory;

    internal static string GetModsDirectory() => Resolved.Value.ModsDirectory;

    internal static string GetConfigFilePath() => Resolved.Value.ConfigFilePath;

    /// <summary>
    /// 测试用：根据给定的假执行目录解析，不依赖真实 AppContext.BaseDirectory。
    /// </summary>
    internal static ResolvedPaths ResolveFromExecutableDirectory(string executableDirectory)
        => ResolveFrom(executableDirectory);

    internal readonly record struct ResolvedPaths(
        bool IsDevelopment,
        string BaseDirectory,
        string ModsDirectory,
        string ConfigFilePath);

    private static ResolvedPaths Resolve()
        => ResolveFrom(AppContext.BaseDirectory);

    private static ResolvedPaths ResolveFrom(string startDirectory)
    {
        var exeDir = Path.GetFullPath(startDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        if (TryFindRepositoryRoot(exeDir, out var repoRoot))
        {
            return CreatePaths(isDevelopment: true, baseDirectory: repoRoot);
        }

        return CreatePaths(isDevelopment: false, baseDirectory: exeDir);
    }

    private static ResolvedPaths CreatePaths(bool isDevelopment, string baseDirectory)
    {
        var baseDir = Path.GetFullPath(baseDirectory);
        return new ResolvedPaths(
            isDevelopment,
            baseDir,
            Path.Combine(baseDir, ModsFolderName),
            Path.Combine(baseDir, ConfigFileName));
    }

    private static bool TryFindRepositoryRoot(string startDirectory, out string repositoryRoot)
    {
        var dir = startDirectory;
        for (var i = 0; i < MaxWalkUpLevels; i++)
        {
            var solutionMarker = Path.Combine(dir, SolutionMarkerFileName);
            var appProject = Path.Combine(dir, "app", "app.csproj");
            if (File.Exists(solutionMarker) || File.Exists(appProject))
            {
                repositoryRoot = dir;
                return true;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        repositoryRoot = string.Empty;
        return false;
    }
}
