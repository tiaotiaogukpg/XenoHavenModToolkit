using System.IO;
using ImageMagick;

namespace XenoHavenModToolkit;

/// <summary>
/// 农业傀儡拆分图：归一化到 1026×1026，并按 Spine 挂靠坐标裁切部件。
/// </summary>
internal static class FarmingGolemPartsSheet
{
    internal const int TargetSize = 1026;

    internal static readonly PartRect Head1 = new("Head1", 0, 0, 342, 342);
    internal static readonly PartRect Head2 = new("Head2", 342, 0, 342, 342);
    internal static readonly PartRect Head3 = new("Head3", 684, 0, 342, 342);
    internal static readonly PartRect HandR = new("HandR", 0, 342, 342, 409);
    internal static readonly PartRect Body = new("Body", 342, 342, 342, 409);
    internal static readonly PartRect HandL = new("HandL", 684, 342, 342, 409);
    internal static readonly PartRect FootR = new("FootR", 309, 751, 202, 275);
    internal static readonly PartRect FootL = new("FootL", 513, 751, 202, 275);

    private static readonly PartRect[] FixedParts =
    [
        Head1, Head2, Head3, HandR, Body, HandL
    ];

    internal readonly record struct PartRect(string Name, int X, int Y, int Width, int Height);

    internal static string GetSheetRelativePath(int buildingId)
        => Path.Combine("Thing", "Dynamic", "images", $"{buildingId}.png");

    internal static string GetPartsRelativeDir(int buildingId)
        => Path.Combine("Thing", "Dynamic", "images", "parts", buildingId.ToString());

    internal static bool IsSupportedSource(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".psd", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase);
    }

    internal static string DialogFilter =>
        "农业傀儡拆分图|*.psd;*.png;*.jpg;*.jpeg;*.bmp;*.webp|PSD 文件|*.psd|PNG 文件|*.png|所有文件|*.*";

    /// <summary>
    /// 将源图归一化并写出总拼图与 parts/。
    /// </summary>
    internal static void ImportAndExport(string sourcePath, string modRoot, int buildingId)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("拆分图文件不存在。", sourcePath);
        }

        if (!IsSupportedSource(sourcePath))
        {
            throw new InvalidOperationException("只支持 psd、png、jpg、jpeg、bmp、webp。");
        }

        // 禁止用「当前已导出的拼图」自身作为源：Magick 读着同一文件时无法可靠覆盖写出。
        var sheetPath = Path.Combine(modRoot, GetSheetRelativePath(buildingId));
        if (PathsEqual(sourcePath, sheetPath))
        {
            throw new InvalidOperationException(
                "请选择一份新的拆分图源文件（PSD/PNG），不要直接选中已导出的 images/<id>.png。");
        }

        using var image = new MagickImage(sourcePath);
        NormalizeToTargetSquare(image);

        Directory.CreateDirectory(Path.GetDirectoryName(sheetPath)!);
        WritePngAtomically(image, sheetPath);

        var partsDir = Path.Combine(modRoot, GetPartsRelativeDir(buildingId));
        Directory.CreateDirectory(partsDir);
        ClearPartPngs(partsDir);

        foreach (var part in FixedParts)
        {
            WriteCrop(image, part, Path.Combine(partsDir, $"{part.Name}.png"));
        }

        ExportFeet(image, partsDir);

        // 同步物品栏图标：用 Head1，避免只换拆分图而图标仍是旧图
        var headPath = Path.Combine(partsDir, "Head1.png");
        if (File.Exists(headPath))
        {
            var iconPath = Path.Combine(modRoot, "Thing", "Dynamic", "images", "icon", $"{buildingId}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
            File.Copy(headPath, iconPath, overwrite: true);
        }
    }

    internal static string? ValidateExportedAssets(string modRoot, int buildingId)
    {
        var sheetPath = Path.Combine(modRoot, GetSheetRelativePath(buildingId));
        if (!File.Exists(sheetPath))
        {
            return $"缺少农业傀儡拆分图：{GetSheetRelativePath(buildingId)}";
        }

        using (var sheet = new MagickImage(sheetPath))
        {
            if (sheet.Width != TargetSize || sheet.Height != TargetSize)
            {
                return $"农业傀儡拆分图尺寸必须是 {TargetSize}×{TargetSize}，当前为 {sheet.Width}×{sheet.Height}。";
            }
        }

        var partsDir = Path.Combine(modRoot, GetPartsRelativeDir(buildingId));
        foreach (var part in FixedParts)
        {
            var path = Path.Combine(partsDir, $"{part.Name}.png");
            if (!File.Exists(path))
            {
                return $"缺少部件：parts/{buildingId}/{part.Name}.png";
            }

            using var partImage = new MagickImage(path);
            if (partImage.Width != part.Width || partImage.Height != part.Height)
            {
                return $"部件 {part.Name} 尺寸应为 {part.Width}×{part.Height}，当前为 {partImage.Width}×{partImage.Height}。";
            }
        }

        var footR = Path.Combine(partsDir, "FootR.png");
        var footL = Path.Combine(partsDir, "FootL.png");
        if (!File.Exists(footR) || !File.Exists(footL))
        {
            return $"缺少脚部件：parts/{buildingId}/FootR.png 与 FootL.png";
        }

        return null;
    }

    internal static void TryDeleteAssets(string modRoot, int buildingId)
    {
        TryDeleteFile(Path.Combine(modRoot, GetSheetRelativePath(buildingId)));
        var partsDir = Path.Combine(modRoot, GetPartsRelativeDir(buildingId));
        if (Directory.Exists(partsDir))
        {
            try
            {
                Directory.Delete(partsDir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    internal static void TryRenameAssets(string modRoot, int oldId, int newId)
    {
        if (oldId == newId)
        {
            return;
        }

        var oldSheet = Path.Combine(modRoot, GetSheetRelativePath(oldId));
        var newSheet = Path.Combine(modRoot, GetSheetRelativePath(newId));
        if (File.Exists(oldSheet) && !File.Exists(newSheet))
        {
            try
            {
                File.Move(oldSheet, newSheet);
            }
            catch
            {
                // ignore
            }
        }

        var oldParts = Path.Combine(modRoot, GetPartsRelativeDir(oldId));
        var newParts = Path.Combine(modRoot, GetPartsRelativeDir(newId));
        if (Directory.Exists(oldParts) && !Directory.Exists(newParts))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newParts)!);
                Directory.Move(oldParts, newParts);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void NormalizeToTargetSquare(MagickImage image)
    {
        if (image.Width != image.Height)
        {
            throw new InvalidOperationException(
                $"农业傀儡拆分图必须是正方形，当前为 {image.Width}×{image.Height}。请先裁成正方形后再导入。");
        }

        if (image.Width != TargetSize || image.Height != TargetSize)
        {
            image.FilterType = FilterType.Lanczos;
            image.Resize(TargetSize, TargetSize);
        }

        image.Alpha(AlphaOption.Set);
        image.BackgroundColor = MagickColors.Transparent;
    }

    private static void ExportFeet(MagickImage sheet, string partsDir)
    {
        var hasRight = RegionHasOpaquePixels(sheet, FootR);
        var hasLeft = RegionHasOpaquePixels(sheet, FootL);

        if (!hasRight && !hasLeft)
        {
            throw new InvalidOperationException("脚区域没有任何有效像素。请至少绘制一只脚（右脚或左脚槽位）。");
        }

        if (hasRight && hasLeft)
        {
            WriteCrop(sheet, FootR, Path.Combine(partsDir, "FootR.png"));
            WriteCrop(sheet, FootL, Path.Combine(partsDir, "FootL.png"));
            return;
        }

        var source = hasRight ? FootR : FootL;
        var sharedPath = Path.Combine(partsDir, "_FootShared.png");
        WriteCrop(sheet, source, sharedPath);
        File.Copy(sharedPath, Path.Combine(partsDir, "FootR.png"), overwrite: true);
        File.Copy(sharedPath, Path.Combine(partsDir, "FootL.png"), overwrite: true);
        File.Delete(sharedPath);
    }

    private static bool RegionHasOpaquePixels(MagickImage sheet, PartRect rect)
    {
        using var crop = sheet.Clone();
        crop.Crop(new MagickGeometry(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height));
        crop.ResetPage();

        using var pixels = crop.GetPixels();
        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var color = pixels.GetPixel(x, y).ToColor();
                if (color is not null && color.A > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void WriteCrop(MagickImage sheet, PartRect rect, string targetPath)
    {
        using var crop = sheet.Clone();
        crop.Crop(new MagickGeometry(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height));
        crop.ResetPage();
        WritePngAtomically(crop, targetPath);
    }

    /// <summary>
    /// 先写临时文件再覆盖，避免目标 PNG 被预览占用或同路径读写时覆盖失败。
    /// </summary>
    private static void WritePngAtomically(IMagickImage image, string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath) ?? ".";
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            image.Write(tempPath, MagickFormat.Png);
            File.Copy(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ClearPartPngs(string partsDir)
    {
        foreach (var file in Directory.EnumerateFiles(partsDir, "*.png"))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // ignore — 后续原子写出仍会覆盖
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }
}
