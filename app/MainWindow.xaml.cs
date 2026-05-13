using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;

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
    private static readonly string ThingBuildingsFolderRelativePath = Path.Combine("Thing", "Buildings");

    private readonly ObservableCollection<BuildingEntry> buildings = [];
    private readonly ObservableCollection<OverviewModEntry> overviewMods = [];
    private string? currentModRoot;
    private AppSettings settings = new();

    public MainWindow()
    {
        InitializeComponent();
        BuildingList.ItemsSource = buildings;
        UpdateTopBarState();
    }

    public void InitializeOnStartup()
    {
        settings = AppSettings.Load();
        UpdateTopBarState();

        if (EnsureOverviewRootConfigured())
        {
            RefreshOverviewList();
            var openedLast = false;
            if (settings.OpenLastModOnStartup &&
                !string.IsNullOrWhiteSpace(settings.LastModPath) &&
                Directory.Exists(settings.LastModPath))
            {
                OpenModFolder(settings.LastModPath);
                openedLast = true;
            }

            if (!openedLast)
            {
                BuildOverviewNavigationTree();
            }

            return;
        }

        PromptOpenModFolder();
        UpdateTopBarState();
    }

    private void OpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        PromptOpenModFolder();
    }

    private bool EnsureOverviewRootConfigured()
    {
        if (!string.IsNullOrWhiteSpace(settings.ModsOverviewRoot) && Directory.Exists(settings.ModsOverviewRoot))
        {
            return true;
        }

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "首次使用：请选择 Mod 总览目录（该目录下每个子文件夹都是一个 Mod）",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return false;
        }

        settings.ModsOverviewRoot = dialog.SelectedPath;
        settings.Save();
        return true;
    }

    private void PromptOpenModFolder()
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
        UpdateTopBarState();
    }

    private void RefreshOverview_Click(object sender, RoutedEventArgs e)
    {
        if (EnsureOverviewRootConfigured())
        {
            RefreshOverviewList();
            if (!EnsureModOpen(showMessage: false))
            {
                BuildOverviewNavigationTree();
            }
            else
            {
                LoadTree();
            }

            Log("已刷新 Mod 总览列表。");
        }
    }

    private void OpenSelectedOverviewMod_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedOverviewMod();
    }

    private void ModFileTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TryGetTreeViewItemFromSource(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (IsClickOnTreeScrollChrome(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (ModFileTree.SelectedItem is TreeViewItem selected)
        {
            selected.IsSelected = false;
        }

        if (EnsureModOpen(showMessage: false))
        {
            CloseCurrentMod();
        }
    }

    private void ModFileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EnsureModOpen(showMessage: false))
        {
            return;
        }

        if (TryGetTreeViewItemFromSource(e.OriginalSource as DependencyObject) is not { Tag: OverviewModDirectoryTag tag })
        {
            return;
        }

        OpenModFolder(tag.FullPath);
        UpdateTopBarState();
    }

    private void OpenSelectedOverviewMod()
    {
        if (ModFileTree.SelectedItem is not TreeViewItem item || item.Tag is not OverviewModDirectoryTag tag)
        {
            Log("请先在导航树中选择一个 Mod。");
            return;
        }

        OpenModFolder(tag.FullPath);
        UpdateTopBarState();
    }

    private void RefreshOverviewList()
    {
        overviewMods.Clear();
        if (string.IsNullOrWhiteSpace(settings.ModsOverviewRoot) || !Directory.Exists(settings.ModsOverviewRoot))
        {
            return;
        }

        foreach (var dir in new DirectoryInfo(settings.ModsOverviewRoot).GetDirectories().OrderBy(d => d.Name))
        {
            overviewMods.Add(new OverviewModEntry(
                dir.FullName,
                dir.Name,
                File.Exists(Path.Combine(dir.FullName, MainXmlRelativePath))));
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var copy = new AppSettings
        {
            ModsOverviewRoot = settings.ModsOverviewRoot,
            LastModPath = settings.LastModPath,
            OpenLastModOnStartup = settings.OpenLastModOnStartup,
            Theme = settings.Theme
        };

        var dialog = new SettingsWindow(copy) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        settings = dialog.Settings;
        settings.Save();
        RefreshOverviewList();
        if (!EnsureModOpen(showMessage: false))
        {
            BuildOverviewNavigationTree();
        }

        Log("设置已保存。");
    }

    private void OpenModFolder(string folder)
    {
        currentModRoot = folder;
        CurrentModPathText.Text = folder;
        settings.LastModPath = folder;
        settings.Save();
        LoadKnownXmlFiles();
        ParseBuildingsFromEditor();
        LoadTree();
        Log($"已打开 Mod：{folder}");
        UpdateTopBarState();
    }

    private void CloseCurrentMod()
    {
        if (string.IsNullOrWhiteSpace(currentModRoot))
        {
            return;
        }

        currentModRoot = null;
        CurrentModPathText.Text = "未打开 Mod 文件夹";

        buildings.Clear();
        BuildingList.SelectedItem = null;
        RefreshImagePreview();

        ModFileTree.Items.Clear();
        MainXmlEditor.Text = string.Empty;
        BuildingsXmlEditor.Text = string.Empty;

        UpdateTopBarState();
        BuildOverviewNavigationTree();
        Log("已返回总览状态。");
    }

    private void UpdateTopBarState()
    {
        var opened = !string.IsNullOrWhiteSpace(currentModRoot) && Directory.Exists(currentModRoot);

        OpenModButton.Visibility = Visibility.Visible;
        NewModButton.Visibility = opened ? Visibility.Collapsed : Visibility.Visible;
        CleanMetaButton.Visibility = opened ? Visibility.Collapsed : Visibility.Visible;

        ModInfoButton.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
        ValidateXmlButton.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
        SaveXmlButton.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
        CleanMetaOpenedButton.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
        ExportReleaseButton.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NewMod_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureOverviewRootConfigured())
            {
                return;
            }

            var dialog = new NewModWindow { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            var targetFolder = Path.Combine(settings.ModsOverviewRoot!, dialog.Result.FolderName);
            if (Directory.Exists(targetFolder))
            {
                Log($"新建失败：已存在同名文件夹：{dialog.Result.FolderName}");
                return;
            }

            Directory.CreateDirectory(targetFolder);
            Directory.CreateDirectory(Path.Combine(targetFolder, "Thing", "Buildings", "images", "icon"));

            File.WriteAllText(Path.Combine(targetFolder, MainXmlRelativePath), dialog.Result.MainXml);

            var buildingsXmlFolder = Path.Combine(targetFolder, "Thing", "Buildings");
            Directory.CreateDirectory(buildingsXmlFolder);
            File.WriteAllText(Path.Combine(buildingsXmlFolder, "Buildings.xml"), dialog.Result.BuildingsXml);

            RefreshOverviewList();
            OpenModFolder(targetFolder);
            Log($"已新建 Mod：{dialog.Result.FolderName}");
        }
        catch (Exception ex)
        {
            Log($"新建 Mod 失败：{ex.Message}");
        }
    }

    private void ValidateXml_Click(object sender, RoutedEventArgs e)
    {
        var messages = ValidateAll();
        Log(string.Join(Environment.NewLine, messages));
    }

    private void ModInfo_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        var dialog = new ModInfoWindow(MainXmlEditor.Text)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.GeneratedXml))
        {
            return;
        }

        MainXmlEditor.Text = dialog.GeneratedXml;
        XmlTabs.SelectedIndex = 0;
        LoadTree();
        Log("已生成 main.xml（已写入编辑器）。别忘了点击“保存 XML”。");
    }

    private void OpenBuildingEditor_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        try
        {
            var initial = GetEditableBuildingFromSelectionOrDefaults();
            var dialog = new BuildingEditorWindow(initial) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            UpsertBuildingToBuildingsXml(dialog.Result);
            ParseBuildingsFromEditor();
            LoadTree();
            Log($"已写入 Buildings.xml（编辑器）：{dialog.Result.Id} - {dialog.Result.Name}。别忘了点击“保存 XML”。");
        }
        catch (Exception ex)
        {
            Log($"打开 Building 编辑失败：{ex.Message}");
        }
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

    private void WorldImage_Click(object sender, MouseButtonEventArgs e)
    {
        ImportImageToSelectedBuilding(BuildingImagesRelativePath, "地图显示图");
    }

    private void IconImage_Click(object sender, MouseButtonEventArgs e)
    {
        ImportImageToSelectedBuilding(BuildingIconsRelativePath, "物品图标");
    }

    private void WorldImage_DragEnter(object sender, WpfDragEventArgs e)
    {
        HandleImageDragEnter(e, WorldImageDropBorder);
    }

    private void WorldImage_DragLeave(object sender, WpfDragEventArgs e)
    {
        ResetDropBorder(WorldImageDropBorder);
    }

    private void WorldImage_Drop(object sender, WpfDragEventArgs e)
    {
        HandleImageDrop(e, BuildingImagesRelativePath, "地图显示图", WorldImageDropBorder);
    }

    private void IconImage_DragEnter(object sender, WpfDragEventArgs e)
    {
        HandleImageDragEnter(e, IconImageDropBorder);
    }

    private void IconImage_DragLeave(object sender, WpfDragEventArgs e)
    {
        ResetDropBorder(IconImageDropBorder);
    }

    private void IconImage_Drop(object sender, WpfDragEventArgs e)
    {
        HandleImageDrop(e, BuildingIconsRelativePath, "物品图标", IconImageDropBorder);
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
        if (e.NewValue is not TreeViewItem item)
        {
            return;
        }

        // 点击 Building 节点时，同步右侧选择与预览
        if (item.Tag is int buildingId)
        {
            var match = buildings.FirstOrDefault(b => b.Id == buildingId);
            if (match is not null)
            {
                BuildingList.SelectedItem = match;
                BuildingList.ScrollIntoView(match);
                RefreshImagePreview();
            }

            return;
        }

        if (item.Tag is OverviewModDirectoryTag tag)
        {
            if (Directory.Exists(tag.FullPath) &&
                !string.Equals(tag.FullPath, currentModRoot, StringComparison.OrdinalIgnoreCase))
            {
                OpenModFolder(tag.FullPath);
                UpdateTopBarState();
            }

            return;
        }

        // 点击文件节点（Buildings.xml / 图片等）时
        if (item.Tag is not string path || Directory.Exists(path))
        {
            return;
        }

        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var text = File.ReadAllText(path);
                var relativePath = Path.GetRelativePath(currentModRoot!, path);
                if (relativePath.Equals(BuildingsXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    BuildingsXmlEditor.Text = text;
                    XmlTabs.SelectedIndex = 1;
                    ParseBuildingsFromEditor();
                }
                else if (relativePath.Equals(MainXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    MainXmlEditor.Text = text;
                    XmlTabs.SelectedIndex = 0;
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
    }

    private void BuildingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshImagePreview();
    }

    private void ReloadBuildings_Click(object sender, RoutedEventArgs e)
    {
        ParseBuildingsFromEditor();
        LoadTree();
        Log("已从编辑器内容重新解析建筑条目并刷新树。");
    }

    private void AddBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        try
        {
            var (nextId, nextName) = SuggestNewBuildingDefaults();
            var dialog = new AddBuildingWindow(nextId, nextName)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var newEntry = dialog.Result!;
            AddBuildingToBuildingsXml(newEntry);
            ParseBuildingsFromEditor();
            LoadTree();
            Log($"已新增 Building：{newEntry.Id} - {newEntry.Name} ({newEntry.Type})。别忘了点击“保存 XML”。");
        }
        catch (Exception ex)
        {
            Log($"新增 Building 失败：{ex.Message}");
        }
    }

    private void ImportImageToSelectedBuilding(string targetRelativeFolder, string slotName, string? sourceImagePath = null)
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

        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"选择{slotName}",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|PNG 文件|*.png|所有文件|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            sourceImagePath = dialog.FileName;
        }

        try
        {
            if (!File.Exists(sourceImagePath))
            {
                Log($"{slotName}导入失败：文件不存在。");
                return;
            }

            var targetFolder = GetFullPath(targetRelativeFolder);
            Directory.CreateDirectory(targetFolder);
            var targetPath = Path.Combine(targetFolder, $"{building.Id}.png");
            File.Copy(sourceImagePath, targetPath, overwrite: true);
            RefreshImagePreview();
            LoadTree();
            Log($"{slotName}已导入：{Path.GetRelativePath(currentModRoot!, targetPath)}");
        }
        catch (Exception ex)
        {
            Log($"{slotName}导入失败：{ex.Message}");
        }
    }

    private static void HandleImageDragEnter(WpfDragEventArgs e, Border border)
    {
        if (TryGetSingleImagePathFromDrag(e) is not null)
        {
            e.Effects = WpfDragDropEffects.Copy;
            border.BorderBrush = System.Windows.Media.Brushes.DodgerBlue;
        }
        else
        {
            e.Effects = WpfDragDropEffects.None;
        }

        e.Handled = true;
    }

    private void HandleImageDrop(WpfDragEventArgs e, string targetRelativeFolder, string slotName, Border border)
    {
        try
        {
            var path = TryGetSingleImagePathFromDrag(e);
            if (path is null)
            {
                return;
            }

            ImportImageToSelectedBuilding(targetRelativeFolder, slotName, path);
        }
        finally
        {
            ResetDropBorder(border);
        }
    }

    private static void ResetDropBorder(Border border)
    {
        border.BorderBrush = System.Windows.Media.Brushes.LightGray;
    }

    private static string? TryGetSingleImagePathFromDrag(WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            return null;
        }

        if (e.Data.GetData(WpfDataFormats.FileDrop) is not string[] files || files.Length != 1)
        {
            return null;
        }

        var file = files[0];
        var ext = Path.GetExtension(file);
        if (!string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return file;
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

                var nameRaw = element.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value;
                var typeRaw = element.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value;
                var categoryRaw = element.Elements().FirstOrDefault(e => e.Name.LocalName == "category")?.Value;
                var nameStored = nameRaw ?? string.Empty;
                var typeStored = typeRaw ?? string.Empty;
                var categoryStored = string.IsNullOrWhiteSpace(categoryRaw) ? "UNKNOWN" : categoryRaw!;
                buildings.Add(new BuildingEntry(id, nameStored, typeStored, categoryStored));
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
        if (!EnsureModOpen(showMessage: false))
        {
            BuildOverviewNavigationTree();
            return;
        }

        ModFileTree.Items.Clear();

        // 已打开 Mod 时仍保持「Mod」为总览根，当前 Mod 作为子节点展开其内部结构
        if (!string.IsNullOrWhiteSpace(settings.ModsOverviewRoot) &&
            Directory.Exists(settings.ModsOverviewRoot))
        {
            RefreshOverviewList();
            var root = new TreeViewItem
            {
                Header = "Mod",
                Tag = settings.ModsOverviewRoot,
                IsExpanded = true
            };

            var matchedOverview = false;
            foreach (var entry in overviewMods)
            {
                var isOpen = string.Equals(entry.FullPath, currentModRoot, StringComparison.OrdinalIgnoreCase);
                var modNode = new TreeViewItem
                {
                    Header = entry.DisplayName,
                    Tag = new OverviewModDirectoryTag(entry.FullPath),
                    IsExpanded = isOpen
                };

                if (isOpen)
                {
                    AppendOpenModExplorerChildren(modNode);
                    matchedOverview = true;
                }

                root.Items.Add(modNode);
            }

            if (!matchedOverview && Directory.Exists(currentModRoot!))
            {
                var orphan = new TreeViewItem
                {
                    Header = new DirectoryInfo(currentModRoot!).Name,
                    Tag = new OverviewModDirectoryTag(currentModRoot!),
                    IsExpanded = true
                };
                AppendOpenModExplorerChildren(orphan);
                root.Items.Add(orphan);
            }

            ModFileTree.Items.Add(root);
            return;
        }

        // 未配置总览目录时：退化为单根（当前 Mod 目录名）
        var rootInfo = new DirectoryInfo(currentModRoot!);
        var singleRoot = new TreeViewItem
        {
            Header = rootInfo.Name,
            Tag = rootInfo.FullName,
            IsExpanded = true
        };
        AppendOpenModExplorerChildren(singleRoot);
        ModFileTree.Items.Add(singleRoot);
    }

    /// <summary>
    /// 在当前已打开的 Mod（<see cref="currentModRoot"/>）下追加 Prefabs / Thing / Buildings.xml 等子节点。
    /// </summary>
    private void AppendOpenModExplorerChildren(TreeViewItem modNode)
    {
        if (!EnsureModOpen(showMessage: false))
        {
            return;
        }

        var prefabsPath = Path.Combine(currentModRoot!, "Prefabs");
        if (Directory.Exists(prefabsPath))
        {
            modNode.Items.Add(CreateTreeItem(new DirectoryInfo(prefabsPath)));
        }

        var thingPath = Path.Combine(currentModRoot!, "Thing");
        var buildingsXmlPath = GetFullPath(BuildingsXmlRelativePath);
        if (!Directory.Exists(thingPath) && !File.Exists(buildingsXmlPath))
        {
            return;
        }

        var thingNode = new TreeViewItem
        {
            Header = "Thing",
            Tag = thingPath,
            IsExpanded = true
        };

        var buildingsXmlNode = new TreeViewItem
        {
            Header = File.Exists(buildingsXmlPath) ? "Buildings.xml" : "Buildings.xml（未创建）",
            Tag = buildingsXmlPath,
            IsExpanded = true
        };

        foreach (var b in buildings.OrderBy(b => b.Id))
        {
            buildingsXmlNode.Items.Add(new TreeViewItem
            {
                Header = b.DisplayName,
                Tag = b.Id,
                IsExpanded = false
            });
        }

        thingNode.Items.Add(buildingsXmlNode);
        modNode.Items.Add(thingNode);
    }

    private void BuildOverviewNavigationTree()
    {
        ModFileTree.Items.Clear();
        if (string.IsNullOrWhiteSpace(settings.ModsOverviewRoot) || !Directory.Exists(settings.ModsOverviewRoot))
        {
            ModFileTree.Items.Add(new TreeViewItem
            {
                Header = "（未配置 Mod 总览目录，请在设置中指定）",
                IsEnabled = false
            });
            return;
        }

        RefreshOverviewList();
        var root = new TreeViewItem
        {
            Header = "Mod",
            Tag = settings.ModsOverviewRoot,
            IsExpanded = true
        };

        foreach (var entry in overviewMods)
        {
            root.Items.Add(new TreeViewItem
            {
                Header = entry.DisplayName,
                Tag = new OverviewModDirectoryTag(entry.FullPath),
                IsExpanded = false
            });
        }

        ModFileTree.Items.Add(root);
    }

    private static TreeViewItem? TryGetTreeViewItemFromSource(DependencyObject? source)
    {
        while (source is not null && source is not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as TreeViewItem;
    }

    private static bool IsClickOnTreeScrollChrome(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TreeViewItem)
            {
                return false;
            }

            if (source is System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
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

    private sealed record BuildingEntry(int Id, string Name, string Type, string Category)
    {
        public string DisplayName
        {
            get
            {
                var middle = string.IsNullOrWhiteSpace(Name)
                    ? (string.IsNullOrWhiteSpace(Type) ? "UNKNOWN" : Type)
                    : Name;
                var cat = string.IsNullOrWhiteSpace(Category) ? "UNKNOWN" : Category;
                return $"{Id} - {middle} ({cat})";
            }
        }
    }

    private sealed record OverviewModDirectoryTag(string FullPath);

    internal sealed record NewBuilding(int Id, string Name, string Type, int SizeX, int SizeY, int Health);
    internal sealed record EditableBuilding(
        int Id,
        string Name,
        string Type,
        int Direction,
        int Capbility,
        int WorkbenchId,
        int Health,
        int SizeX,
        int SizeY);

    private (int nextId, string nextName) SuggestNewBuildingDefaults()
    {
        var nextId = buildings.Count == 0 ? 1 : buildings.Max(b => b.Id) + 1;
        return (nextId, $"NewBuilding_{nextId}");
    }

    private void AddBuildingToBuildingsXml(NewBuilding b)
    {
        // 基于编辑器内容增量写入，避免“保存 XML”前读写磁盘导致用户修改丢失。
        var doc = XDocument.Parse(BuildingsXmlEditor.Text, LoadOptions.PreserveWhitespace);
        if (doc.Root?.Name.LocalName != "ArrayOfModBuildingXML")
        {
            throw new InvalidOperationException("Buildings.xml 根节点不是 ArrayOfModBuildingXML。");
        }

        if (doc.Root.Elements().Any(e => e.Name.LocalName == "ModBuildingXML" &&
                                        int.TryParse(e.Elements().FirstOrDefault(x => x.Name.LocalName == "id")?.Value, out var id) &&
                                        id == b.Id))
        {
            throw new InvalidOperationException($"Buildings.xml 中已存在相同 id：{b.Id}。");
        }

        var element =
            new XElement("ModBuildingXML",
                new XElement("id", b.Id),
                new XElement("name", b.Name),
                new XElement("type", b.Type),
                new XElement("direction", 1),
                new XElement("capbility", 1),
                new XElement("workbenchId", 1),
                new XElement("health", b.Health),
                new XElement("size",
                    new XElement("x", b.SizeX),
                    new XElement("y", b.SizeY)
                ),
                new XElement("materials")
            );

        doc.Root.Add(new XText(Environment.NewLine + "  "));
        doc.Root.Add(element);
        doc.Root.Add(new XText(Environment.NewLine));
        BuildingsXmlEditor.Text = doc.Declaration is null
            ? doc.ToString()
            : doc.Declaration + Environment.NewLine + doc.ToString();

        // 确保图片目录存在（具体图片由用户导入）
        Directory.CreateDirectory(GetFullPath(BuildingImagesRelativePath));
        Directory.CreateDirectory(GetFullPath(BuildingIconsRelativePath));
    }

    private sealed record OverviewModEntry(string FullPath, string FolderName, bool HasMainXml)
    {
        public string DisplayName => HasMainXml ? FolderName : $"{FolderName}（未创建 main.xml）";
    }

    private EditableBuilding GetEditableBuildingFromSelectionOrDefaults()
    {
        // 如果用户在列表里选了建筑，就尽量从 XML 里读出完整字段做“编辑”
        if (BuildingList.SelectedItem is BuildingEntry selected)
        {
            var existing = TryReadEditableBuildingById(selected.Id);
            if (existing is not null)
            {
                return existing;
            }

            var nameForEdit = string.IsNullOrWhiteSpace(selected.Name)
                ? (string.IsNullOrWhiteSpace(selected.Type) ? $"Building_{selected.Id}" : selected.Type)
                : selected.Name;
            var typeForEdit = string.IsNullOrWhiteSpace(selected.Type) ? "Building" : selected.Type;
            return new EditableBuilding(selected.Id, nameForEdit, typeForEdit, 1, 1, 1, 100, 1, 1);
        }

        var (nextId, nextName) = SuggestNewBuildingDefaults();
        return new EditableBuilding(nextId, nextName, "Building", 1, 1, 1, 100, 1, 1);
    }

    private EditableBuilding? TryReadEditableBuildingById(int id)
    {
        try
        {
            var doc = XDocument.Parse(BuildingsXmlEditor.Text);
            if (doc.Root?.Name.LocalName != "ArrayOfModBuildingXML")
            {
                return null;
            }

            var el = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "ModBuildingXML" &&
                                                             int.TryParse(e.Elements().FirstOrDefault(x => x.Name.LocalName == "id")?.Value, out var eid) &&
                                                             eid == id);
            if (el is null)
            {
                return null;
            }

            int ReadInt(string name, int fallback)
                => int.TryParse(el.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value, out var v) ? v : fallback;

            var nameValue = el.Elements().FirstOrDefault(x => x.Name.LocalName == "name")?.Value ?? $"Building_{id}";
            var typeValue = el.Elements().FirstOrDefault(x => x.Name.LocalName == "type")?.Value ?? "Building";
            var size = el.Elements().FirstOrDefault(x => x.Name.LocalName == "size");
            var sx = 1;
            var sy = 1;
            if (size is not null)
            {
                sx = int.TryParse(size.Elements().FirstOrDefault(x => x.Name.LocalName == "x")?.Value, out var vx) ? vx : 1;
                sy = int.TryParse(size.Elements().FirstOrDefault(x => x.Name.LocalName == "y")?.Value, out var vy) ? vy : 1;
            }

            return new EditableBuilding(
                id,
                nameValue,
                typeValue,
                ReadInt("direction", 1),
                ReadInt("capbility", 1),
                ReadInt("workbenchId", 1),
                ReadInt("health", 100),
                sx,
                sy);
        }
        catch
        {
            return null;
        }
    }

    private void UpsertBuildingToBuildingsXml(EditableBuilding b)
    {
        var doc = XDocument.Parse(BuildingsXmlEditor.Text, LoadOptions.PreserveWhitespace);
        if (doc.Root?.Name.LocalName != "ArrayOfModBuildingXML")
        {
            throw new InvalidOperationException("Buildings.xml 根节点不是 ArrayOfModBuildingXML。");
        }

        var existing = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "ModBuildingXML" &&
                                                               int.TryParse(e.Elements().FirstOrDefault(x => x.Name.LocalName == "id")?.Value, out var id) &&
                                                               id == b.Id);

        var element =
            new XElement("ModBuildingXML",
                new XElement("id", b.Id),
                new XElement("name", b.Name),
                new XElement("type", b.Type),
                new XElement("direction", b.Direction),
                new XElement("capbility", b.Capbility),
                new XElement("workbenchId", b.WorkbenchId),
                new XElement("health", b.Health),
                new XElement("size",
                    new XElement("x", b.SizeX),
                    new XElement("y", b.SizeY)
                ),
                new XElement("materials")
            );

        if (existing is null)
        {
            doc.Root.Add(new XText(Environment.NewLine + "  "));
            doc.Root.Add(element);
            doc.Root.Add(new XText(Environment.NewLine));
        }
        else
        {
            existing.ReplaceWith(element);
        }

        BuildingsXmlEditor.Text = doc.Declaration is null
            ? doc.ToString()
            : doc.Declaration + Environment.NewLine + doc.ToString();

        Directory.CreateDirectory(GetFullPath(BuildingImagesRelativePath));
        Directory.CreateDirectory(GetFullPath(BuildingIconsRelativePath));
    }
}