using System.IO;

namespace XenoHavenModToolkit;

internal static class GameDataPaths
{
    internal const string DocFolderName = "DOC";
    internal const string MaterialsFileName = "K-可用材料表.xlsx";
    internal const string WorkbenchesFileName = "K-可用工作台.xlsx";

    internal static string ResolveDocDirectory()
    {
        var besideExe = Path.Combine(AppContext.BaseDirectory, DocFolderName);
        if (ContainsGameDataFiles(besideExe))
        {
            return besideExe;
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir, DocFolderName);
            if (ContainsGameDataFiles(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return besideExe;
    }

    internal static string MaterialsFilePath => Path.Combine(ResolveDocDirectory(), MaterialsFileName);

    internal static string WorkbenchesFilePath => Path.Combine(ResolveDocDirectory(), WorkbenchesFileName);

    private static bool ContainsGameDataFiles(string directory)
        => File.Exists(Path.Combine(directory, MaterialsFileName))
           && File.Exists(Path.Combine(directory, WorkbenchesFileName));
}
