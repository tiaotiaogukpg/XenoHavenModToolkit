using System.Windows;
using System.Xml.Linq;

namespace XenoHavenModToolkit;

public partial class ModInfoWindow : Window
{
    internal string? GeneratedXml { get; private set; }

    public ModInfoWindow(string existingMainXml)
    {
        InitializeComponent();
        TryPrefill(existingMainXml);
    }

    private void TryPrefill(string existingMainXml)
    {
        try
        {
            var doc = XDocument.Parse(existingMainXml);
            if (doc.Root?.Name.LocalName != "defs")
            {
                return;
            }

            NameBox.Text = doc.Root.Element("name")?.Value ?? NameBox.Text;
            AuthorBox.Text = doc.Root.Element("auth")?.Value ?? AuthorBox.Text;
            VersionBox.Text = doc.Root.Element("version")?.Value ?? VersionBox.Text;
            SpecBox.Text = doc.Root.Element("specifications")?.Value ?? SpecBox.Text;
            DescriptionBox.Text = doc.Root.Element("description")?.Value ?? DescriptionBox.Text;
        }
        catch
        {
            // ignore invalid xml while user is editing
        }
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var author = AuthorBox.Text.Trim();
        var version = VersionBox.Text.Trim();
        var spec = SpecBox.Text.Trim();
        var description = DescriptionBox.Text;

        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(this, "name 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            System.Windows.MessageBox.Show(this, "auth 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            System.Windows.MessageBox.Show(this, "version 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(spec))
        {
            System.Windows.MessageBox.Show(this, "specifications 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("defs",
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                new XElement("name", name),
                new XElement("auth", author),
                new XElement("version", version),
                new XElement("specifications", spec),
                new XElement("description", description ?? string.Empty)
            )
        );

        GeneratedXml = doc.ToString();
        DialogResult = true;
    }
}

