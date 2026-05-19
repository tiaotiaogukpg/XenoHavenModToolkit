using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace XenoHavenModToolkit;

internal static class ModXmlFormatter
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
            Encoding = ModXmlIO.Utf8NoBom
        };

        using var stringWriter = new Utf8StringWriter();
        using (var writer = XmlWriter.Create(stringWriter, settings))
        {
            document.Save(writer);
        }

        return stringWriter.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => ModXmlIO.Utf8NoBom;
    }
}
