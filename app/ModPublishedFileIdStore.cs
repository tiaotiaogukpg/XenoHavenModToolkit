using System.IO;

namespace XenoHavenModToolkit;

/// <summary>
/// 创意工坊 PublishedFileId 侧车文件，与 main.xml 同目录。
/// main.xml 中 steamPublishedFileId 恒为 0，实际上传 ID 仅存于此文件。
/// </summary>
internal static class ModPublishedFileIdStore
{
    public const string FileName = "steamPublishedFileId.id";

    public static string GetPath(string modRoot)
        => Path.Combine(modRoot, FileName);

    public static ulong ReadOrZero(string modRoot, string? mainXmlText = null)
    {
        if (TryRead(modRoot, out var id))
        {
            return id;
        }

        if (TryParseFromMainXml(mainXmlText, out id) && id > 0)
        {
            Write(modRoot, id);
            return id;
        }

        return 0;
    }

    public static bool TryParseFromMainXml(string? mainXmlText, out ulong publishedFileId)
    {
        publishedFileId = 0;
        if (string.IsNullOrWhiteSpace(mainXmlText))
        {
            return false;
        }

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(mainXmlText);
            var steamText = doc.Root?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "steamPublishedFileId")?.Value;
            return ulong.TryParse(steamText?.Trim(), out publishedFileId) && publishedFileId > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>将 main.xml 中的 steamPublishedFileId 重置为 0（若曾被写入）。</summary>
    public static bool TryClearMainXmlField(string mainXmlPath, ref string mainXmlText, out string? error)
    {
        error = null;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(mainXmlText);
            if (doc.Root is null || doc.Root.Name.LocalName != "defs")
            {
                return false;
            }

            var element = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "steamPublishedFileId");
            if (element is null || element.Value.Trim() == "0")
            {
                return false;
            }

            element.Value = "0";
            var serialized = ModXmlFormatter.Serialize(doc);
            ModXmlIO.WriteAllText(mainXmlPath, serialized);
            mainXmlText = serialized;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryRead(string modRoot, out ulong publishedFileId)
    {
        publishedFileId = 0;
        var path = GetPath(modRoot);
        if (!File.Exists(path))
        {
            return false;
        }

        var text = File.ReadAllText(path).Trim();
        return ulong.TryParse(text, out publishedFileId) && publishedFileId > 0;
    }

    public static void Write(string modRoot, ulong publishedFileId)
    {
        File.WriteAllText(GetPath(modRoot), publishedFileId.ToString() + Environment.NewLine);
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

    public static bool IsSidecarFile(string fileName)
        => string.Equals(fileName, FileName, StringComparison.OrdinalIgnoreCase);

    public static bool ShouldExcludeFromPublish(string fileName)
        => IsSidecarFile(fileName);
}
