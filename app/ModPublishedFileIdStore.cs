using System.IO;
using System.Xml.Linq;

namespace XenoHavenModToolkit;

/// <summary>
/// 本地 PublishedFileId 旁路存储：Mod 根目录 <c>steamPublishedFileId.id</c>。
/// <c>main.xml</c> 中 <c>steamPublishedFileId</c> 恒为 0，上传时排除该旁路文件。
/// </summary>
internal static class ModPublishedFileIdStore
{
    public const string FileName = "steamPublishedFileId.id";

    public static string GetFilePath(string modRoot)
        => Path.Combine(modRoot, FileName);

    public static bool ShouldExcludeFromPublish(string fileName)
        => string.Equals(fileName, FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 优先读旁路文件；若不存在则回退解析 main.xml（便于旧工程迁移）。
    /// </summary>
    public static ulong ReadOrZero(string modRoot, string? mainXmlText = null)
    {
        var path = GetFilePath(modRoot);
        if (File.Exists(path))
        {
            try
            {
                var text = File.ReadAllText(path).Trim();
                if (ulong.TryParse(text, out var fromFile))
                {
                    return fromFile;
                }
            }
            catch
            {
                // fall through to main.xml
            }
        }

        if (string.IsNullOrWhiteSpace(mainXmlText))
        {
            var mainXmlPath = Path.Combine(modRoot, "main.xml");
            if (File.Exists(mainXmlPath))
            {
                try
                {
                    mainXmlText = ModXmlIO.ReadAllText(mainXmlPath);
                }
                catch
                {
                    return 0;
                }
            }
        }

        return TryReadFromMainXml(mainXmlText);
    }

    public static void Write(string modRoot, ulong publishedFileId)
    {
        Directory.CreateDirectory(modRoot);
        var path = GetFilePath(modRoot);
        File.WriteAllText(path, publishedFileId.ToString() + Environment.NewLine);
    }

    public static bool TryWrite(string modRoot, ulong publishedFileId, out string? error)
    {
        error = null;
        try
        {
            Write(modRoot, publishedFileId);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static ulong TryReadFromMainXml(string? mainXmlText)
    {
        if (string.IsNullOrWhiteSpace(mainXmlText))
        {
            return 0;
        }

        try
        {
            var doc = XDocument.Parse(mainXmlText);
            if (doc.Root?.Name.LocalName != "defs")
            {
                return 0;
            }

            var idText = doc.Root.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "steamPublishedFileId")
                ?.Value;
            return ulong.TryParse(idText?.Trim(), out var id) ? id : 0;
        }
        catch
        {
            return 0;
        }
    }
}
