using System.IO;

namespace XenoHavenModToolkit;

internal static class ModRootAssets
{
    public const string IconRelativePath = "icon.png";
    public const string ScreenshotRelativePath = "screenshot.png";
    public const string ImageDialogFilter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|PNG 文件|*.png|所有文件|*.*";

    public static bool IsSupportedImage(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase);
    }

    public static void CopyToRoot(string sourcePath, string modRoot, string targetRelativePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("图片文件不存在。", sourcePath);
        }

        if (!IsSupportedImage(sourcePath))
        {
            throw new InvalidOperationException("只支持 png、jpg、jpeg、bmp、webp 图片。");
        }

        var targetPath = Path.Combine(modRoot, targetRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }
}
