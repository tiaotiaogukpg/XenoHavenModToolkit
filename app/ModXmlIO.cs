using System.IO;
using System.Text;

namespace XenoHavenModToolkit;

internal static class ModXmlIO
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string ReadAllText(string path)
        => File.ReadAllText(path, Utf8NoBom);

    public static void WriteAllText(string path, string contents)
        => File.WriteAllText(path, contents, Utf8NoBom);
}
