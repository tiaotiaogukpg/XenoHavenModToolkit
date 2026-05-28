using System.IO;
using System.Windows;
using System.Xml.Linq;

namespace XenoHavenModToolkit;

public partial class NewModWindow : Window
{
    private string? iconSourcePath;
    private string? screenshotSourcePath;
    private readonly int modBaseId;

    internal NewModResult? Result { get; private set; }

    public NewModWindow()
    {
        InitializeComponent();
        modBaseId = Random.Shared.Next(100000, 900001) * 100;
        IdBox.Text = modBaseId.ToString();
        SupportVersionBox.Text = "1";
        NameBox.Text = "New Mod";
        AuthorBox.Text = "Author";
        NameBox.SelectAll();
        NameBox.Focus();
    }

    internal sealed record NewModResult(string Name, string MainXml, string BuildingsXml, string IconSourcePath, string ScreenshotSourcePath);

    private void ImportIcon_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseImage("选择 MOD 图标") is not { } path)
        {
            return;
        }

        iconSourcePath = path;
        IconPathText.Text = path;
    }

    private void ImportScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseImage("选择 MOD 截图") is not { } path)
        {
            return;
        }

        screenshotSourcePath = path;
        ScreenshotPathText.Text = path;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var author = AuthorBox.Text.Trim();
        var version = VersionBox.Text.Trim();
        var category = CategoryBox.Text.Trim();
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

        if (string.IsNullOrWhiteSpace(description))
        {
            System.Windows.MessageBox.Show(this, "description 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            System.Windows.MessageBox.Show(this, "Category 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ValidateSelectedImage(iconSourcePath, "MOD 图标 icon.png") ||
            !ValidateSelectedImage(screenshotSourcePath, "MOD 截图 screenshot.png"))
        {
            return;
        }

        var mainDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("defs",
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                new XElement("id", modBaseId),
                new XElement("steamPublishedFileId", 0),
                new XElement("SupportVersion", 1),
                new XElement("Category", category),
                new XElement("name", name),
                new XElement("auth", author),
                new XElement("version", version),
                new XElement("description", description.Trim())
            )
        );

        var buildingsDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ArrayOfModBuildingXML",
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance")));

        Result = new NewModResult(
            name,
            ModXmlFormatter.Serialize(mainDoc),
            ModXmlFormatter.Serialize(buildingsDoc),
            iconSourcePath!,
            screenshotSourcePath!);
        DialogResult = true;
    }

    private string? TryChooseImage(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = ModRootAssets.ImageDialogFilter
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private bool ValidateSelectedImage(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            System.Windows.MessageBox.Show(this, $"请导入{label}。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!File.Exists(path))
        {
            System.Windows.MessageBox.Show(this, $"{label}不存在：{path}", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!ModRootAssets.IsSupportedImage(path))
        {
            System.Windows.MessageBox.Show(this, $"{label}只支持 png、jpg、jpeg、bmp、webp。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }
}

