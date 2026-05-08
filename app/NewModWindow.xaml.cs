using System.Windows;
using System.Xml.Linq;

namespace XenoHavenModToolkit;

public partial class NewModWindow : Window
{
    internal NewModResult? Result { get; private set; }

    public NewModWindow()
    {
        InitializeComponent();
        NameBox.Text = "New Mod";
        AuthorBox.Text = "Author";
        FolderNameBox.Text = "mod_new_01";
        FolderNameBox.SelectAll();
        FolderNameBox.Focus();
    }

    internal sealed record NewModResult(string FolderName, string MainXml, string BuildingsXml);

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var folderName = FolderNameBox.Text.Trim();
        var name = NameBox.Text.Trim();
        var author = AuthorBox.Text.Trim();
        var version = VersionBox.Text.Trim();
        var spec = SpecBox.Text.Trim();
        var description = DescriptionBox.Text;

        if (string.IsNullOrWhiteSpace(folderName))
        {
            System.Windows.MessageBox.Show(this, "文件夹名不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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

        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(spec))
        {
            System.Windows.MessageBox.Show(this, "version/specifications 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mainDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("defs",
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                new XElement("guid", Guid.NewGuid().ToString("D")),
                new XElement("name", name),
                new XElement("auth", author),
                new XElement("version", version),
                new XElement("specifications", spec),
                new XElement("description", description ?? string.Empty)
            )
        );

        var buildingsXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfModBuildingXML xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            </ArrayOfModBuildingXML>
            """;

        Result = new NewModResult(folderName, mainDoc.ToString(), buildingsXml);
        DialogResult = true;
    }
}

