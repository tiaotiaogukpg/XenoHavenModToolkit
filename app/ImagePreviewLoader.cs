using System.IO;
using System.Windows.Media.Imaging;

namespace XenoHavenModToolkit;

/// <summary>
/// 从磁盘加载预览图；绕过 WPF 按 URI 缓存，保证覆盖同路径 PNG 后能看到新图。
/// </summary>
internal static class ImagePreviewLoader
{
    internal static BitmapImage? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            // 先整文件读入内存再解码，避免 FileStream 在 EndInit 后被释放导致空白图，
            // 同时不依赖 URI 缓存（同路径覆盖后仍能刷新）。
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
