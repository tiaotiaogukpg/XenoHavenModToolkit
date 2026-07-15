using System.IO;
using Xunit;

namespace XenoHavenModToolkit.Tests;

public class AppPathsTests
{
    [Fact]
    public void ResolveFromExecutableDirectory_FindsRepoRoot_WhenSolutionMarkerPresent()
    {
        var temp = CreateTempDir();
        try
        {
            var repoRoot = Path.Combine(temp, "RepoRoot");
            var exeDir = Path.Combine(repoRoot, "app", "bin", "Debug", "net10.0-windows");
            Directory.CreateDirectory(exeDir);
            File.WriteAllText(Path.Combine(repoRoot, "XenoHavenModToolkit.slnx"), "<Solution />");

            var resolved = AppPaths.ResolveFromExecutableDirectory(exeDir);

            Assert.True(resolved.IsDevelopment);
            Assert.Equal(Path.GetFullPath(repoRoot), resolved.BaseDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(repoRoot), "Mods"), resolved.ModsDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(repoRoot), "config.json"), resolved.ConfigFilePath);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ResolveFromExecutableDirectory_UsesExeDir_WhenNoRepoMarker()
    {
        var temp = CreateTempDir();
        try
        {
            var publishDir = Path.Combine(temp, "Publish");
            Directory.CreateDirectory(publishDir);

            var resolved = AppPaths.ResolveFromExecutableDirectory(publishDir);

            Assert.False(resolved.IsDevelopment);
            Assert.Equal(Path.GetFullPath(publishDir), resolved.BaseDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(publishDir), "Mods"), resolved.ModsDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(publishDir), "config.json"), resolved.ConfigFilePath);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void ResolveFromExecutableDirectory_FindsRepoRoot_ByAppCsproj()
    {
        var temp = CreateTempDir();
        try
        {
            var repoRoot = Path.Combine(temp, "RepoByCsproj");
            var exeDir = Path.Combine(repoRoot, "app", "bin", "Release", "net10.0-windows");
            Directory.CreateDirectory(exeDir);
            Directory.CreateDirectory(Path.Combine(repoRoot, "app"));
            File.WriteAllText(Path.Combine(repoRoot, "app", "app.csproj"), "<Project />");

            var resolved = AppPaths.ResolveFromExecutableDirectory(exeDir);

            Assert.True(resolved.IsDevelopment);
            Assert.Equal(Path.GetFullPath(repoRoot), resolved.BaseDirectory);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "XenoHavenModToolkitTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}
