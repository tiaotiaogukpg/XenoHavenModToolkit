using System.IO;
using System.Windows;
using System.Xml.Linq;

namespace XenoHavenModToolkit;

public partial class ModInfoWindow : Window
{
    private readonly string modRoot;

    internal string? GeneratedXml { get; private set; }
    internal string? IconSourcePath { get; private set; }
    internal string? ScreenshotSourcePath { get; private set; }

    public ModInfoWindow(string modRoot, string existingMainXml)
    {
        InitializeComponent();
        this.modRoot = modRoot;
        TryPrefill(existingMainXml);
        EnsureGuidInitialized();
        RefreshRootImageStatus();
    }

    private void EnsureGuidInitialized()
    {
        if (Guid.TryParse(GuidBox.Text?.Trim(), out var guid) && guid != Guid.Empty)
        {
            return;
        }

        GuidBox.Text = Guid.NewGuid().ToString("D");
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

            GuidBox.Text = doc.Root.Element("guid")?.Value ?? GuidBox.Text;
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

    private void RegenerateGuid_Click(object sender, RoutedEventArgs e)
    {
        GuidBox.Text = Guid.NewGuid().ToString("D");
    }

    private void ImportIcon_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseImage("选择 MOD 图标") is not { } path)
        {
            return;
        }

        IconSourcePath = path;
        RefreshRootImageStatus();
    }

    private void ImportScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseImage("选择 MOD 截图") is not { } path)
        {
            return;
        }

        ScreenshotSourcePath = path;
        RefreshRootImageStatus();
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var guidText = GuidBox.Text.Trim();
        var name = NameBox.Text.Trim();
        var author = AuthorBox.Text.Trim();
        var version = VersionBox.Text.Trim();
        var spec = SpecBox.Text.Trim();
        var description = DescriptionBox.Text;

        if (!Guid.TryParse(guidText, out var guid) || guid == Guid.Empty)
        {
            System.Windows.MessageBox.Show(this, "guid 不合法。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (string.IsNullOrWhiteSpace(description))
        {
            System.Windows.MessageBox.Show(this, "description 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!HasExistingOrSelectedImage(ModRootAssets.IconRelativePath, IconSourcePath, "MOD 图标 icon.png") ||
            !HasExistingOrSelectedImage(ModRootAssets.ScreenshotRelativePath, ScreenshotSourcePath, "MOD 截图 screenshot.png"))
        {
            return;
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("defs",
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                new XElement("guid", guid.ToString("D")),
                new XElement("name", name),
                new XElement("auth", author),
                new XElement("version", version),
                new XElement("specifications", spec),
                new XElement("description", description.Trim())
            )
        );

        GeneratedXml = ModXmlFormatter.Serialize(doc);
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

    private void RefreshRootImageStatus()
    {
        IconPathText.Text = FormatRootImageStatus(ModRootAssets.IconRelativePath, IconSourcePath);
        ScreenshotPathText.Text = FormatRootImageStatus(ModRootAssets.ScreenshotRelativePath, ScreenshotSourcePath);
    }

    private string FormatRootImageStatus(string relativePath, string? selectedPath)
    {
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            return $"已选择：{selectedPath}";
        }

        var targetPath = Path.Combine(modRoot, relativePath);
        return File.Exists(targetPath)
            ? $"当前：{relativePath}"
            : $"缺失：{relativePath}";
    }

    private bool HasExistingOrSelectedImage(string relativePath, string? selectedPath, string label)
    {
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            if (!File.Exists(selectedPath))
            {
                System.Windows.MessageBox.Show(this, $"{label}不存在：{selectedPath}", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!ModRootAssets.IsSupportedImage(selectedPath))
            {
                System.Windows.MessageBox.Show(this, $"{label}只支持 png、jpg、jpeg、bmp、webp。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        if (!File.Exists(Path.Combine(modRoot, relativePath)))
        {
            System.Windows.MessageBox.Show(this, $"请导入{label}。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }
}

