using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace XenoHavenModToolkit;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string MainXmlRelativePath = "main.xml";
    private static readonly string BuildingsXmlRelativePath = Path.Combine("Thing", "Buildings", "Buildings.xml");
    private static readonly string BuildingImagesRelativePath = Path.Combine("Thing", "Buildings", "images");
    private static readonly string BuildingIconsRelativePath = Path.Combine("Thing", "Buildings", "images", "icon");

    private readonly ObservableCollection<BuildingEntry> buildings = [];
    private string? currentModRoot;

    public MainWindow()
    {
        InitializeComponent();
        BuildingList.ItemsSource = buildings;
    }

    private void OpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择 XenoHaven Mod 根目录（包含 main.xml）",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        OpenModFolder(dialog.SelectedPath);
    }

    private void OpenModFolder(string folder)
    {
        currentModRoot = folder;
        CurrentModPathText.Text = folder;
        LoadTree();
        LoadKnownXmlFiles();
        ParseBuildingsFromEditor();
        Log($"已打开 Mod：{folder}");
    }

    private void ValidateXml_Click(object sender, RoutedEventArgs e)
    {
        var messages = ValidateAll();
        Log(string.Join(Environment.NewLine, messages));
    }

    private void SaveXml_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        try
        {
            var validationMessages = ValidateAll();
            if (validationMessages.Any(message => message.StartsWith("[错误]", StringComparison.Ordinal)))
            {
                Log("保存已取消：请先修复 XML 错误。" + Environment.NewLine + string.Join(Environment.NewLine, validationMessages));
                return;
            }

            File.WriteAllText(GetFullPath(MainXmlRelativePath), MainXmlEditor.Text);
            Directory.CreateDirectory(Path.GetDirectoryName(GetFullPath(BuildingsXmlRelativePath))!);
            File.WriteAllText(GetFullPath(BuildingsXmlRelativePath), BuildingsXmlEditor.Text);
            ParseBuildingsFromEditor();
            LoadTree();
            Log("XML 已保存。");
        }
        catch (Exception ex)
        {
            Log($"保存失败：{ex.Message}");
        }
    }

    private void ImportWorldImage_Click(object sender, RoutedEventArgs e)
    {
        ImportImageToSelectedBuilding(BuildingImagesRelativePath, "地图显示图");
    }

    private void ImportIconImage_Click(object sender, RoutedEventArgs e)
    {
        ImportImageToSelectedBuilding(BuildingIconsRelativePath, "物品图标");
    }

    private void CleanMeta_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            "将删除当前 Mod 目录内所有 *.meta 文件。该操作只建议用于发布版 Mod。是否继续？",
            "清理 .meta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var deleted = DeleteMetaFiles(currentModRoot!);
            LoadTree();
            Log($"已删除 {deleted} 个 .meta 文件。");
        }
        catch (Exception ex)
        {
            Log($"清理 .meta 失败：{ex.Message}");
        }
    }

    private void ExportRelease_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择发布版 Mod 输出目录",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        try
        {
            var sourceName = new DirectoryInfo(currentModRoot!).Name;
            var targetRoot = Path.Combine(dialog.SelectedPath, sourceName);
            CopyDirectoryWithoutMeta(currentModRoot!, targetRoot);
            Log($"发布版已导出：{targetRoot}");
        }
        catch (Exception ex)
        {
            Log($"导出失败：{ex.Message}");
        }
    }

    private void ModFileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: string path } || Directory.Exists(path))
        {
            return;
        }

        if (!Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(path);
            var relativePath = Path.GetRelativePath(currentModRoot!, path);
            if (relativePath.Equals(MainXmlRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                MainXmlEditor.Text = text;
                XmlTabs.SelectedIndex = 0;
            }
            else if (relativePath.Equals(BuildingsXmlRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                BuildingsXmlEditor.Text = text;
                XmlTabs.SelectedIndex = 1;
                ParseBuildingsFromEditor();
            }
            else
            {
                BuildingsXmlEditor.Text = text;
                XmlTabs.SelectedIndex = 1;
            }

            Log($"已载入 XML：{relativePath}");
        }
        catch (Exception ex)
        {
            Log($"载入 XML 失败：{ex.Message}");
        }
    }

    private void BuildingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshImagePreview();
    }

    private void ImportImageToSelectedBuilding(string targetRelativeFolder, string slotName)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        if (BuildingList.SelectedItem is not BuildingEntry building)
        {
            Log("请先在右侧选择一个建筑条目。");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"选择{slotName}",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|PNG 文件|*.png|所有文件|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var targetFolder = GetFullPath(targetRelativeFolder);
            Directory.CreateDirectory(targetFolder);
            var targetPath = Path.Combine(targetFolder, $"{building.Id}.png");
            File.Copy(dialog.FileName, targetPath, overwrite: true);
            RefreshImagePreview();
            LoadTree();
            Log($"{slotName}已导入：{Path.GetRelativePath(currentModRoot!, targetPath)}");
        }
        catch (Exception ex)
        {
            Log($"{slotName}导入失败：{ex.Message}");
        }
    }

    private void LoadKnownXmlFiles()
    {
        if (!EnsureModOpen())
        {
            return;
        }

        MainXmlEditor.Text = ReadTextOrTemplate(
            MainXmlRelativePath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <defs xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <name>New Mod</name>
              <auth>Author</auth>
              <version>1.0.0</version>
              <specifications>0.0.1</specifications>
              <description></description>
            </defs>
            """);

        BuildingsXmlEditor.Text = ReadTextOrTemplate(
            BuildingsXmlRelativePath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfModBuildingXML xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            </ArrayOfModBuildingXML>
            """);
    }

    private string ReadTextOrTemplate(string relativePath, string template)
    {
        var path = GetFullPath(relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : template;
    }

    private List<string> ValidateAll()
    {
        var messages = new List<string>();
        ValidateXmlDocument("main.xml", MainXmlEditor.Text, "defs", messages);
        ValidateBuildingsXml(messages);

        if (messages.Count == 0)
        {
            messages.Add("[通过] XML 基础校验通过。");
        }

        return messages;
    }

    private static void ValidateXmlDocument(string label, string xmlText, string? expectedRoot, List<string> messages)
    {
        try
        {
            var document = XDocument.Parse(xmlText, LoadOptions.SetLineInfo);
            if (expectedRoot is not null && document.Root?.Name.LocalName != expectedRoot)
            {
                messages.Add($"[错误] {label} 根节点应为 <{expectedRoot}>，当前为 <{document.Root?.Name.LocalName ?? "空"}>。");
            }
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            messages.Add($"[错误] {label} 不是合法 XML：{ex.Message}");
        }
    }

    private void ValidateBuildingsXml(List<string> messages)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(BuildingsXmlEditor.Text, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            messages.Add($"[错误] Buildings.xml 不是合法 XML：{ex.Message}");
            return;
        }

        if (document.Root?.Name.LocalName != "ArrayOfModBuildingXML")
        {
            messages.Add($"[错误] Buildings.xml 根节点应为 <ArrayOfModBuildingXML>，当前为 <{document.Root?.Name.LocalName ?? "空"}>。");
            return;
        }

        var seenIds = new HashSet<int>();
        foreach (var element in document.Root.Elements().Where(e => e.Name.LocalName == "ModBuildingXML"))
        {
            var idText = element.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
            if (!int.TryParse(idText, out var id))
            {
                messages.Add("[错误] 存在缺少有效 <id> 的 ModBuildingXML。");
                continue;
            }

            if (!seenIds.Add(id))
            {
                messages.Add($"[错误] 建筑 id 重复：{id}。");
            }

            ValidatePositiveInt(element, "direction", id, messages);
            ValidatePositiveInt(element, "capbility", id, messages);
            ValidatePositiveInt(element, "workbenchId", id, messages);
            ValidatePositiveInt(element, "health", id, messages);

            var size = element.Elements().FirstOrDefault(e => e.Name.LocalName == "size");
            if (size is null)
            {
                messages.Add($"[错误] 建筑 {id} 缺少 <size>。");
            }
            else
            {
                ValidatePositiveInt(size, "x", id, messages, "size");
                ValidatePositiveInt(size, "y", id, messages, "size");
            }

            var materials = element.Elements().FirstOrDefault(e => e.Name.LocalName == "materials");
            if (materials is not null)
            {
                foreach (var material in materials.Elements().Where(e => e.Name.LocalName == "ModCraftMaterialData"))
                {
                    ValidatePositiveInt(material, "id", id, messages, "material");
                    ValidatePositiveInt(material, "count", id, messages, "material");
                }
            }
        }

        messages.Add($"[信息] 识别到 {seenIds.Count} 个建筑条目。");
    }

    private static void ValidatePositiveInt(XElement parent, string childName, int buildingId, List<string> messages, string scope = "building")
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == childName)?.Value;
        if (!int.TryParse(value, out var number) || number <= 0)
        {
            messages.Add($"[错误] 建筑 {buildingId} 的 {scope}.{childName} 需要是正整数。");
        }
    }

    private void ParseBuildingsFromEditor()
    {
        buildings.Clear();

        try
        {
            var document = XDocument.Parse(BuildingsXmlEditor.Text);
            if (document.Root?.Name.LocalName != "ArrayOfModBuildingXML")
            {
                return;
            }

            foreach (var element in document.Root.Elements().Where(e => e.Name.LocalName == "ModBuildingXML"))
            {
                var idText = element.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
                if (!int.TryParse(idText, out var id))
                {
                    continue;
                }

                var name = element.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value ?? "(未命名)";
                var type = element.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value ?? "?";
                buildings.Add(new BuildingEntry(id, name, type));
            }

            RefreshImagePreview();
        }
        catch
        {
            // Text editing may leave the XML temporarily invalid; validation reports the details.
        }
    }

    private void RefreshImagePreview()
    {
        WorldImagePreview.Source = null;
        IconImagePreview.Source = null;

        if (!EnsureModOpen(showMessage: false) || BuildingList.SelectedItem is not BuildingEntry building)
        {
            WorldImagePathText.Text = "地图显示图：未选择建筑";
            IconImagePathText.Text = "物品图标：未选择建筑";
            return;
        }

        var worldPath = GetFullPath(Path.Combine(BuildingImagesRelativePath, $"{building.Id}.png"));
        var iconPath = GetFullPath(Path.Combine(BuildingIconsRelativePath, $"{building.Id}.png"));

        WorldImagePathText.Text = $"地图显示图：{Path.GetRelativePath(currentModRoot!, worldPath)}";
        IconImagePathText.Text = $"物品图标：{Path.GetRelativePath(currentModRoot!, iconPath)}";
        WorldImagePreview.Source = LoadBitmapIfExists(worldPath);
        IconImagePreview.Source = LoadBitmapIfExists(iconPath);
    }

    private static BitmapImage? LoadBitmapIfExists(string path)
    {
        if (!File.Exists(path))
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

    private void LoadTree()
    {
        ModFileTree.Items.Clear();
        if (!EnsureModOpen(showMessage: false))
        {
            return;
        }

        ModFileTree.Items.Add(CreateTreeItem(new DirectoryInfo(currentModRoot!)));
    }

    private static TreeViewItem CreateTreeItem(FileSystemInfo info)
    {
        var item = new TreeViewItem
        {
            Header = info.Name,
            Tag = info.FullName,
            IsExpanded = info is DirectoryInfo
        };

        if (info is DirectoryInfo directory)
        {
            foreach (var childDirectory in directory.GetDirectories().OrderBy(d => d.Name))
            {
                item.Items.Add(CreateTreeItem(childDirectory));
            }

            foreach (var file in directory.GetFiles().OrderBy(f => f.Name))
            {
                item.Items.Add(CreateTreeItem(file));
            }
        }

        return item;
    }

    private static int DeleteMetaFiles(string root)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.meta", SearchOption.AllDirectories))
        {
            File.Delete(file);
            count++;
        }

        return count;
    }

    private static void CopyDirectoryWithoutMeta(string sourceRoot, string targetRoot)
    {
        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, recursive: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relativePath));
        }

        Directory.CreateDirectory(targetRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file).Equals(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var targetPath = Path.Combine(targetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private string GetFullPath(string relativePath)
    {
        return Path.Combine(currentModRoot!, relativePath);
    }

    private bool EnsureModOpen(bool showMessage = true)
    {
        if (!string.IsNullOrWhiteSpace(currentModRoot) && Directory.Exists(currentModRoot))
        {
            return true;
        }

        if (showMessage)
        {
            Log("请先打开一个 Mod 文件夹。");
        }

        return false;
    }

    private void Log(string message)
    {
        LogBox.Text = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}{LogBox.Text}";
    }

    private sealed record BuildingEntry(int Id, string Name, string Type)
    {
        public string DisplayName => $"{Id} - {Name} ({Type})";
    }
}