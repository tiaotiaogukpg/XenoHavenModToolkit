using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace XenoHavenModToolkit;

public partial class ModInfoWindow : Window
{
    private readonly string modRoot;
    private readonly int modBaseId;

    internal string? GeneratedXml { get; private set; }
    internal string? IconSourcePath { get; private set; }
    internal string? ScreenshotSourcePath { get; private set; }

    public ModInfoWindow(string modRoot, string existingMainXml)
    {
        InitializeComponent();
        this.modRoot = modRoot;
        var (baseId, steamId) = TryPrefill(existingMainXml);
        modBaseId = baseId;
        IdBox.Text = modBaseId.ToString();
        SteamPublishedFileIdBox.Text = steamId.ToString();
        SupportVersionBox.Text = "1";
        RefreshRootImageStatus();
    }

    private (int modBaseId, long steamPublishedFileId) TryPrefill(string existingMainXml)
    {
        var baseId = Random.Shared.Next(100000, 900001) * 100;
        long steamId = 0;

        try
        {
            var doc = XDocument.Parse(existingMainXml);
            if (doc.Root?.Name.LocalName != "defs")
            {
                return (baseId, steamId);
            }

            var idText = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
            if (int.TryParse(idText?.Trim(), out var parsedBaseId) &&
                parsedBaseId is > 10000000 and <= 200000000)
            {
                baseId = parsedBaseId;
            }

            var steamText = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "steamPublishedFileId")?.Value;
            if (long.TryParse(steamText?.Trim(), out var parsedSteamId) && parsedSteamId >= 0)
            {
                steamId = parsedSteamId;
            }

            NameBox.Text = doc.Root.Element("name")?.Value ?? NameBox.Text;
            AuthorBox.Text = doc.Root.Element("auth")?.Value ?? AuthorBox.Text;
            VersionBox.Text = doc.Root.Element("version")?.Value ?? VersionBox.Text;
            DescriptionBox.Text = doc.Root.Element("description")?.Value ?? DescriptionBox.Text;
            CategoryBox.Text = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "Category")?.Value ?? CategoryBox.Text;
        }
        catch
        {
            // ignore invalid xml while user is editing
        }

        return (baseId, steamId);
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

        if (string.IsNullOrWhiteSpace(category))
        {
            System.Windows.MessageBox.Show(this, "Category 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (!long.TryParse(SteamPublishedFileIdBox.Text.Trim(), out var steamPublishedFileId))
        {
            steamPublishedFileId = 0;
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("defs",
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                new XElement("id", modBaseId),
                new XElement("steamPublishedFileId", steamPublishedFileId),
                new XElement("SupportVersion", 1),
                new XElement("Category", category),
                new XElement("name", name),
                new XElement("auth", author),
                new XElement("version", version),
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

        var iconPath = ResolveRootImagePath(ModRootAssets.IconRelativePath, IconSourcePath);
        var screenshotPath = ResolveRootImagePath(ModRootAssets.ScreenshotRelativePath, ScreenshotSourcePath);
        IconImagePreview.Source = LoadBitmapIfExists(iconPath);
        ScreenshotImagePreview.Source = LoadBitmapIfExists(screenshotPath);
    }

    private string ResolveRootImagePath(string relativePath, string? selectedPath)
        => !string.IsNullOrWhiteSpace(selectedPath) ? selectedPath : Path.Combine(modRoot, relativePath);

    private static BitmapImage? LoadBitmapIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
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
