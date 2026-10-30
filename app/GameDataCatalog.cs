using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace XenoHavenModToolkit;

internal sealed class GameDataCatalog
{
    private static readonly string[] NameHeaderAliases = ["名称", "name", "Name", "材料名称", "物品名称", "工作台名称", "生产线名称"];
    private static readonly string[] IdHeaderAliases = ["ID", "id", "Id", "材料ID", "物品ID", "工作台ID", "生产线ID"];
    private static readonly string[] EnglishNameHeaderAliases = ["英", "英文", "English", "english", "English Name", "english name", "en"];

    private GameDataCatalog(
        IReadOnlyList<LabeledIdOption> materials,
        IReadOnlyList<LabeledIdOption> workbenches,
        IReadOnlyList<LabeledIdOption> productionLines,
        string? loadError,
        string? productionLinesLoadWarning = null)
    {
        Materials = materials;
        Workbenches = workbenches;
        ProductionLines = productionLines;
        LoadError = loadError;
        ProductionLinesLoadWarning = productionLinesLoadWarning;
        IsReady = loadError is null && materials.Count > 0 && workbenches.Count > 0;
    }

    public IReadOnlyList<LabeledIdOption> Materials { get; }

    public IReadOnlyList<LabeledIdOption> Workbenches { get; }

    public IReadOnlyList<LabeledIdOption> ProductionLines { get; }

    public bool IsReady { get; }

    public string? LoadError { get; }

    public string? ProductionLinesLoadWarning { get; }

    public static GameDataCatalog Load()
    {
        try
        {
            var docDir = GameDataPaths.ResolveDocDirectory();
            var materialsPath = Path.Combine(docDir, GameDataPaths.MaterialsFileName);
            var workbenchesPath = Path.Combine(docDir, GameDataPaths.WorkbenchesFileName);
            var productionLinesPath = Path.Combine(docDir, GameDataPaths.ProductionLinesFileName);

            if (!File.Exists(materialsPath) || !File.Exists(workbenchesPath))
            {
                return new GameDataCatalog([], [], [], $"Game data tables not found. Expected directory: {docDir}");
            }

            var materials = LoadSheet(materialsPath, GameDataPaths.MaterialsFileName);
            var workbenches = LoadSheet(workbenchesPath, GameDataPaths.WorkbenchesFileName, readEnglishName: true);
            var productionLines = Array.Empty<LabeledIdOption>();
            string? productionLinesLoadWarning = null;
            if (File.Exists(productionLinesPath))
            {
                try
                {
                    productionLines = LoadSheet(productionLinesPath, GameDataPaths.ProductionLinesFileName).ToArray();
                }
                catch (Exception ex)
                {
                    productionLinesLoadWarning = $"Failed to load {GameDataPaths.ProductionLinesFileName}: {ex.Message}";
                }
            }
            else
            {
                productionLinesLoadWarning = $"{GameDataPaths.ProductionLinesFileName} not found; PRODUCTION_LINE simulateId list unavailable.";
            }

            return new GameDataCatalog(materials, workbenches, productionLines, null, productionLinesLoadWarning);
        }
        catch (Exception ex)
        {
            return new GameDataCatalog([], [], [], $"Failed to load game data tables: {ex.Message}");
        }
    }

    public LabeledIdOption? FindMaterial(int id)
        => Materials.FirstOrDefault(option => option.Id == id);

    public LabeledIdOption? FindWorkbench(int id)
        => Workbenches.FirstOrDefault(option => option.Id == id);

    public LabeledIdOption? FindProductionLine(int id)
        => ProductionLines.FirstOrDefault(option => option.Id == id);

    public int DefaultWorkbenchId
        => Workbenches.Count > 0 ? Workbenches[0].Id : 0;

    public int DefaultProductionLineId
        => ProductionLines.Count > 0 ? ProductionLines[0].Id : 0;

    private static IReadOnlyList<LabeledIdOption> LoadSheet(string filePath, string displayName, bool readEnglishName = false)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException($"{displayName} has no worksheets.");

        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException($"{displayName} is empty.");

        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastColumn == 0)
        {
            throw new InvalidOperationException($"{displayName} is missing a header row.");
        }

        var nameColumn = -1;
        var idColumn = -1;
        var englishNameColumn = -1;
        for (var column = 1; column <= lastColumn; column++)
        {
            var header = headerRow.Cell(column).GetString().Trim();
            if (nameColumn < 0 && NameHeaderAliases.Any(alias => string.Equals(alias, header, StringComparison.OrdinalIgnoreCase)))
            {
                nameColumn = column;
            }

            if (idColumn < 0 && IdHeaderAliases.Any(alias => string.Equals(alias, header, StringComparison.OrdinalIgnoreCase)))
            {
                idColumn = column;
            }

            if (readEnglishName && englishNameColumn < 0 &&
                EnglishNameHeaderAliases.Any(alias => string.Equals(alias, header, StringComparison.OrdinalIgnoreCase)))
            {
                englishNameColumn = column;
            }
        }

        if (nameColumn < 0 || idColumn < 0)
        {
            throw new InvalidOperationException($"{displayName} must include name and ID header columns.");
        }

        var options = new List<LabeledIdOption>();
        var seenIds = new HashSet<int>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var name = row.Cell(nameColumn).GetString().Trim();
            var idText = row.Cell(idColumn).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idText))
            {
                continue;
            }

            if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                continue;
            }

            if (!seenIds.Add(id))
            {
                continue;
            }

            string? englishName = null;
            if (readEnglishName && englishNameColumn > 0)
            {
                englishName = row.Cell(englishNameColumn).GetString().Trim();
                if (string.IsNullOrWhiteSpace(englishName))
                {
                    englishName = null;
                }
            }

            options.Add(readEnglishName
                ? new LabeledIdOption(name, id, englishName)
                : new LabeledIdOption(name, id));
        }

        if (options.Count == 0)
        {
            throw new InvalidOperationException($"{displayName} has no valid data rows.");
        }

        return options;
    }
}
