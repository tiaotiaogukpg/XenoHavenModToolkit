using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace XenoHavenModToolkit;

internal sealed class GameDataCatalog
{
    private static readonly string[] NameHeaderAliases = ["名称", "name", "Name", "材料名称", "物品名称", "工作台名称", "生产线名称"];
    private static readonly string[] IdHeaderAliases = ["ID", "id", "Id", "材料ID", "物品ID", "工作台ID", "生产线ID"];

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
                return new GameDataCatalog([], [], [], $"未找到游戏数据表，请确认目录存在：{docDir}");
            }

            var materials = LoadSheet(materialsPath, GameDataPaths.MaterialsFileName);
            var workbenches = LoadSheet(workbenchesPath, GameDataPaths.WorkbenchesFileName);
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
                    productionLinesLoadWarning = $"加载 {GameDataPaths.ProductionLinesFileName} 失败：{ex.Message}";
                }
            }
            else
            {
                productionLinesLoadWarning = $"未找到 {GameDataPaths.ProductionLinesFileName}，PRODUCTION_LINE 的 simulateId 下拉不可用。";
            }

            return new GameDataCatalog(materials, workbenches, productionLines, null, productionLinesLoadWarning);
        }
        catch (Exception ex)
        {
            return new GameDataCatalog([], [], [], $"加载游戏数据表失败：{ex.Message}");
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

    private static IReadOnlyList<LabeledIdOption> LoadSheet(string filePath, string displayName)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException($"{displayName} 中没有工作表。");

        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException($"{displayName} 为空。");

        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastColumn == 0)
        {
            throw new InvalidOperationException($"{displayName} 缺少表头。");
        }

        var nameColumn = -1;
        var idColumn = -1;
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
        }

        if (nameColumn < 0 || idColumn < 0)
        {
            throw new InvalidOperationException($"{displayName} 需要包含「名称/name」与「ID/id」表头列。");
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

            options.Add(new LabeledIdOption(name, id));
        }

        if (options.Count == 0)
        {
            throw new InvalidOperationException($"{displayName} 未解析到有效数据行。");
        }

        return options;
    }
}
