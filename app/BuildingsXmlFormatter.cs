using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace XenoHavenModToolkit;

internal static class BuildingsXmlFormatter
{
    public static string Serialize(XDocument document)
    {
        if (document.Declaration is null)
        {
            document.Declaration = new XDeclaration("1.0", "utf-8", null);
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        using var stringWriter = new StringWriter();
        using (var writer = XmlWriter.Create(stringWriter, settings))
        {
            document.Save(writer);
        }

        return stringWriter.ToString();
    }
}
