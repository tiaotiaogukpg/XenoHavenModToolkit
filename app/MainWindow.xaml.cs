using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
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
    private static readonly string ThingBuildingsFolderRelativePath = Path.Combine("Thing", "Buildings");

    private readonly ObservableCollection<BuildingEntry> buildings = [];
    private readonly ObservableCollection<OverviewModEntry> overviewMods = [];
    private readonly GameDataCatalog gameData = GameDataCatalog.Load();
    private string mainXmlText = string.Empty;
    private string buildingsXmlText = string.Empty;
    private string? currentModRoot;
    private int? currentModBaseId;
    private AppSettings settings = new();

    public MainWindow()
    {
        InitializeComponent();
        BuildingTiles.ItemsSource = buildings;
        UpdateTopBarState();
    }

    public void InitializeOnStartup()
    {
        settings = AppSettings.Load();
        if (!gameData.IsReady && !string.IsNullOrWhiteSpace(gameData.LoadError))
        {
            Log(gameData.LoadError);
        }

        UpdateTopBarState();

        if (EnsureOverviewRootConfigured())
        {
            RefreshOverviewList();
            BuildOverviewNavigationTree();
            if (settings.OpenLastModOnStartup &&
                !string.IsNullOrWhiteSpace(settings.LastModPath) &&
                Directory.Exists(settings.LastModPath))
            {
                TrySelectModInTree(settings.LastModPath);
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
            BuildOverviewNavigationTree();
            if (EnsureModOpen(showMessage: false))
            {
                ReloadBuildingsXmlFromDisk();
                ParseBuildingsFromEditor();
                RefreshBuildingNodesInTree();
            }

            Log("已刷新 Mod 总览列表。");
        }
    }

    private void ModFileTree_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (TryGetTreeViewItemFromSource(e.OriginalSource as DependencyObject) is TreeViewItem)
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
        if (TryGetTreeViewItemFromSource(e.OriginalSource as DependencyObject) is not TreeViewItem item)
        {
            return;
        }

        if (item.Tag is BuildingTreeTag buildingTag)
        {
            if (EnsureModOpen() && EnsureGameDataReady())
            {
                OpenBuildingEditor_Click(sender, e);
            }

            e.Handled = true;
        }
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
            var mainXmlPath = Path.Combine(dir.FullName, MainXmlRelativePath);
            var hasMainXml = File.Exists(mainXmlPath);
            var category = string.Empty;
            if (hasMainXml)
            {
                category = TryReadMainXmlRootValue(mainXmlPath, "Category") ?? string.Empty;
            }

            overviewMods.Add(new OverviewModEntry(
                dir.FullName,
                dir.Name,
                hasMainXml,
                category));
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
        BuildOverviewNavigationTree();
        if (EnsureModOpen(showMessage: false))
        {
            TrySelectModInTree(currentModRoot!);
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
        currentModBaseId = TryDetermineCurrentModBaseId();
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
        currentModBaseId = null;
        CurrentModPathText.Text = "未打开 Mod 文件夹";

        buildings.Clear();
        BuildingTiles.SelectedItem = null;

        mainXmlText = string.Empty;
        buildingsXmlText = string.Empty;

        BuildOverviewNavigationTree();
        UpdateTopBarState();
        Log("已返回总览状态。");
    }

    private void UpdateTopBarState()
    {
        UpdateToolbarForSelection();
    }

    private void UpdateToolbarForSelection()
    {
        var opened = !string.IsNullOrWhiteSpace(currentModRoot) && Directory.Exists(currentModRoot);
        var selection = ClassifyCurrentSelection();

        OpenModButton.IsEnabled = !opened;
        NewModButton.IsEnabled = IsOverviewRootConfigured() && (selection == TreeSelectionKind.OverviewRoot || !opened);

        ModInfoButton.IsEnabled = opened && selection is TreeSelectionKind.ModRoot or TreeSelectionKind.MainXml;
        AddBuildingButton.IsEnabled = opened && selection == TreeSelectionKind.BuildingsXml;
        EditBuildingButton.IsEnabled = opened && selection == TreeSelectionKind.BuildingNode;
        DeleteBuildingButton.IsEnabled = opened && selection == TreeSelectionKind.BuildingNode;
        ExportReleaseButton.IsEnabled = opened && selection == TreeSelectionKind.ModRoot;
    }

    private bool IsOverviewRootConfigured()
        => !string.IsNullOrWhiteSpace(settings.ModsOverviewRoot) && Directory.Exists(settings.ModsOverviewRoot);

    private TreeSelectionKind ClassifyCurrentSelection()
    {
        if (ModFileTree.SelectedItem is TreeViewItem treeItem)
        {
            return ClassifyTreeItem(treeItem);
        }

        if (BuildingTiles.SelectedItem is BuildingEntry)
        {
            return TreeSelectionKind.BuildingNode;
        }

        return TreeSelectionKind.None;
    }

    private TreeSelectionKind ClassifyTreeItem(TreeViewItem item)
    {
        switch (item.Tag)
        {
            case string path when !string.IsNullOrWhiteSpace(settings.ModsOverviewRoot) &&
                                  path.Equals(settings.ModsOverviewRoot, StringComparison.OrdinalIgnoreCase):
                return TreeSelectionKind.OverviewRoot;
            case OverviewModDirectoryTag directoryTag:
                if (!string.IsNullOrWhiteSpace(currentModRoot) &&
                    string.Equals(directoryTag.FullPath, currentModRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return TreeSelectionKind.ModRoot;
                }

                return TreeSelectionKind.OverviewModFolder;
            case BuildingTreeTag buildingTag when !string.IsNullOrWhiteSpace(currentModRoot) &&
                string.Equals(buildingTag.ModRoot, currentModRoot, StringComparison.OrdinalIgnoreCase):
                return TreeSelectionKind.BuildingNode;
            case string path when !string.IsNullOrWhiteSpace(currentModRoot):
                if (string.Equals(path, currentModRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return TreeSelectionKind.ModRoot;
                }

                if (File.Exists(path))
                {
                    var relativePath = Path.GetRelativePath(currentModRoot, path);
                    if (relativePath.Equals(MainXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return TreeSelectionKind.MainXml;
                    }

                    if (relativePath.Equals(BuildingsXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return TreeSelectionKind.BuildingsXml;
                    }
                }

                return TreeSelectionKind.ModOther;
            default:
                return TreeSelectionKind.None;
        }
    }

    private bool EnsureGameDataReady()
    {
        if (gameData.IsReady)
        {
            return true;
        }

        System.Windows.MessageBox.Show(
            this,
            gameData.LoadError ?? "游戏数据表未就绪。",
            "游戏数据",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private int NormalizeWorkbenchId(int raw)
        => gameData.FindWorkbench(raw)?.Id ?? gameData.DefaultWorkbenchId;

    private static int NormalizeCapbility(int raw)
    {
        if (raw < BuildingFieldOptions.MinCapbility)
        {
            return BuildingFieldOptions.DefaultCapbility;
        }

        return raw > BuildingFieldOptions.MaxCapbility ? BuildingFieldOptions.MaxCapbility : raw;
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

            var folderName = CreateUniqueModFolderName(settings.ModsOverviewRoot!, dialog.Result.Name);
            var targetFolder = Path.Combine(settings.ModsOverviewRoot!, folderName);

            Directory.CreateDirectory(targetFolder);
            Directory.CreateDirectory(Path.Combine(targetFolder, "Thing", "Buildings", "images", "icon"));

            ModXmlIO.WriteAllText(Path.Combine(targetFolder, MainXmlRelativePath), dialog.Result.MainXml);
            ModRootAssets.CopyToRoot(dialog.Result.IconSourcePath, targetFolder, ModRootAssets.IconRelativePath);
            ModRootAssets.CopyToRoot(dialog.Result.ScreenshotSourcePath, targetFolder, ModRootAssets.ScreenshotRelativePath);

            var buildingsXmlFolder = Path.Combine(targetFolder, "Thing", "Buildings");
            Directory.CreateDirectory(buildingsXmlFolder);
            ModXmlIO.WriteAllText(Path.Combine(buildingsXmlFolder, "Buildings.xml"), dialog.Result.BuildingsXml);

            RefreshOverviewList();
            BuildOverviewNavigationTree();
            TrySelectModInTree(targetFolder);
            Log($"已新建 Mod：{folderName}");
        }
        catch (Exception ex)
        {
            Log($"新建 Mod 失败：{ex.Message}");
        }
    }

    private static string CreateUniqueModFolderName(string root, string name)
    {
        var baseName = MakeSafeFolderName(name);
        var candidate = baseName;
        var suffix = 2;
        while (Directory.Exists(Path.Combine(root, candidate)))
        {
            candidate = $"{baseName}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string MakeSafeFolderName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(name.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray())
            .Trim()
            .TrimEnd('.');

        return string.IsNullOrWhiteSpace(cleaned) ? "Mod" : cleaned;
    }

    private void ModInfo_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        var dialog = new ModInfoWindow(currentModRoot!, mainXmlText)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.GeneratedXml))
        {
            return;
        }

        try
        {
            var generatedXml = dialog.GeneratedXml;
            if (!string.IsNullOrWhiteSpace(dialog.IconSourcePath))
            {
                ModRootAssets.CopyToRoot(dialog.IconSourcePath, currentModRoot!, ModRootAssets.IconRelativePath);
            }

            if (!string.IsNullOrWhiteSpace(dialog.ScreenshotSourcePath))
            {
                ModRootAssets.CopyToRoot(dialog.ScreenshotSourcePath, currentModRoot!, ModRootAssets.ScreenshotRelativePath);
            }

            var messages = new List<string>();
            ValidateModMetadata(currentModRoot!, generatedXml, messages);
            if (messages.Any(message => message.StartsWith("[错误]", StringComparison.Ordinal)))
            {
                Log("main.xml 保存前校验失败：" + Environment.NewLine + string.Join(Environment.NewLine, messages));
                System.Windows.MessageBox.Show(this, string.Join(Environment.NewLine, messages), "Mod 信息校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            mainXmlText = generatedXml;
            ModXmlIO.WriteAllText(GetFullPath(MainXmlRelativePath), mainXmlText);
            currentModBaseId = TryDetermineCurrentModBaseId();
            RefreshOverviewList();
            BuildOverviewNavigationTree();
            Log("main.xml 已更新。");
        }
        catch (Exception ex)
        {
            Log($"main.xml 更新失败：{ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "Mod 信息", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenBuildingEditor_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen() || !EnsureGameDataReady())
        {
            return;
        }

        try
        {
            var initial = GetEditableBuildingFromSelectionOrDefaults();
            var dialog = new BuildingEditorWindow(initial, currentModRoot!, gameData) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            UpsertBuildingToBuildingsXml(dialog.Result);
            SaveBuildingsXmlToDisk();
            ParseBuildingsFromEditor();
            RefreshBuildingNodesInTree();
            Log($"已更新 Building：{dialog.Result.Id} - {dialog.Result.Name}。");
        }
        catch (Exception ex)
        {
            Log($"打开 Building 编辑失败：{ex.Message}");
        }
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
            BuildOverviewNavigationTree();
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

        var messages = new List<string>();
        ValidateModMetadata(currentModRoot!, mainXmlText, messages);
        if (messages.Any(message => message.StartsWith("[错误]", StringComparison.Ordinal)))
        {
            Log("导出发布版前校验失败：" + Environment.NewLine + string.Join(Environment.NewLine, messages));
            System.Windows.MessageBox.Show(this, string.Join(Environment.NewLine, messages), "导出发布版", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        switch (item.Tag)
        {
            case OverviewModDirectoryTag directoryTag:
                EnsureModActivated(directoryTag.FullPath);
                break;
            case BuildingTreeTag buildingTag:
                EnsureModActivated(buildingTag.ModRoot);
                SyncBuildingTileSelection(buildingTag.BuildingId);
                break;
            case string path:
                if (TryResolveModRootFromPath(path) is { } modRoot)
                {
                    EnsureModActivated(modRoot);
                }

                if (!File.Exists(path))
                {
                    break;
                }

                if (EnsureModOpen(showMessage: false) &&
                    Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var text = ModXmlIO.ReadAllText(path);
                        var relativePath = Path.GetRelativePath(currentModRoot!, path);
                        if (relativePath.Equals(BuildingsXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                        {
                            buildingsXmlText = text;
                            ParseBuildingsFromEditor();
                        }
                        else if (relativePath.Equals(MainXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                        {
                            mainXmlText = text;
                        }
                        else
                        {
                            buildingsXmlText = text;
                        }

                        Log($"已载入 XML：{relativePath}");
                    }
                    catch (Exception ex)
                    {
                        Log($"载入 XML 失败：{ex.Message}");
                    }
                }

                break;
        }

        UpdateToolbarForSelection();
    }

    private void BuildingTiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(currentModRoot) || !Directory.Exists(currentModRoot))
        {
            return;
        }

        if (BuildingTiles.SelectedItem is BuildingEntry entry &&
            TryFindBuildingTreeItem(ModFileTree, currentModRoot, entry.Id) is TreeViewItem treeItem)
        {
            treeItem.IsSelected = true;
            treeItem.BringIntoView();
        }

        UpdateToolbarForSelection();
    }

    private void BuildingTiles_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetListBoxItemFromSource(e.OriginalSource as DependencyObject) is null ||
            BuildingTiles.SelectedItem is not BuildingEntry)
        {
            return;
        }

        OpenBuildingEditor_Click(sender, e);
    }

    private void DeleteBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        if (BuildingTiles.SelectedItem is not BuildingEntry selected)
        {
            System.Windows.MessageBox.Show(this, "请先在磁贴区选中要删除的 Building。", "删除 Building", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"确定从 Buildings.xml 中删除 Building「{selected.Name}」（id={selected.Id}）吗？\n将同时尝试删除 Thing/Buildings/images 与 icon 下对应的 {selected.Id}.png（若存在）。",
            "删除 Building",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!RemoveBuildingFromBuildingsXml(selected.Id))
            {
                System.Windows.MessageBox.Show(this, $"未在 Buildings.xml 中找到 id={selected.Id} 的条目。", "删除 Building", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TryDeleteBuildingImageFiles(selected.Id);
            SaveBuildingsXmlToDisk();
            BuildingTiles.SelectedItem = null;
            ParseBuildingsFromEditor();
            RefreshBuildingNodesInTree();
            UpdateTopBarState();
            Log($"已删除 Building：id={selected.Id} - {selected.Name}。");
        }
        catch (Exception ex)
        {
            Log($"删除 Building 失败：{ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "删除 Building", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen() || !EnsureGameDataReady())
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
            SaveBuildingsXmlToDisk();
            ParseBuildingsFromEditor();
            RefreshBuildingNodesInTree();
            Log($"已新增 Building：{newEntry.Id} - {newEntry.Name} ({newEntry.Type})。");
        }
        catch (Exception ex)
        {
            Log($"新增 Building 失败：{ex.Message}");
        }
    }

    private void LoadKnownXmlFiles()
    {
        if (!EnsureModOpen())
        {
            return;
        }

        mainXmlText = ReadTextOrTemplate(
            MainXmlRelativePath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <defs xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <id>70000000</id>
              <steamPublishedFileId>0</steamPublishedFileId>
              <SupportVersion>1</SupportVersion>
              <Category>Building</Category>
              <name>New Mod</name>
              <auth>Author</auth>
              <version>1.0.0</version>
              <description>Mod description</description>
            </defs>
            """);

        buildingsXmlText = ReadTextOrTemplate(
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
        return File.Exists(path) ? ModXmlIO.ReadAllText(path) : template;
    }

    private void ReloadBuildingsXmlFromDisk()
    {
        if (!EnsureModOpen(showMessage: false))
        {
            buildingsXmlText = string.Empty;
            return;
        }

        buildingsXmlText = ReadTextOrTemplate(
            BuildingsXmlRelativePath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfModBuildingXML xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            </ArrayOfModBuildingXML>
            """);
    }

    private void SaveBuildingsXmlToDisk()
    {
        if (!EnsureModOpen())
        {
            return;
        }

        var messages = new List<string>();
        ValidateBuildingsXml(messages);
        if (messages.Any(message => message.StartsWith("[错误]", StringComparison.Ordinal)))
        {
            Log("Buildings.xml 写盘前校验发现问题：" + Environment.NewLine + string.Join(Environment.NewLine, messages));
        }

        var path = GetFullPath(BuildingsXmlRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ModXmlIO.WriteAllText(path, buildingsXmlText);
    }

    private static void ValidateModMetadata(string modRoot, string mainXml, List<string> messages)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(mainXml, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            messages.Add($"[错误] main.xml 不是合法 XML：{ex.Message}");
            return;
        }

        if (document.Root?.Name.LocalName != "defs")
        {
            messages.Add($"[错误] main.xml 根节点应为 <defs>，当前为 <{document.Root?.Name.LocalName ?? "空"}>。");
            return;
        }

        var description = document.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "description")?.Value;
        if (string.IsNullOrWhiteSpace(description))
        {
            messages.Add("[错误] main.xml 的 <description> 为必填项。");
        }

        ValidateRequiredRootImage(modRoot, ModRootAssets.IconRelativePath, "MOD 图标 icon.png", messages);
        ValidateRequiredRootImage(modRoot, ModRootAssets.ScreenshotRelativePath, "MOD 截图 screenshot.png", messages);
    }

    private static void ValidateRequiredRootImage(string modRoot, string relativePath, string label, List<string> messages)
    {
        var path = Path.Combine(modRoot, relativePath);
        if (!File.Exists(path))
        {
            messages.Add($"[错误] 缺少{label}：{relativePath}。");
        }
    }

    private void ValidateBuildingsXml(List<string> messages)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(buildingsXmlText, LoadOptions.SetLineInfo);
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
            var document = XDocument.Parse(buildingsXmlText);
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
                var iconPath = EnsureModOpen(showMessage: false)
                    ? GetFullPath(Path.Combine(BuildingIconsRelativePath, $"{id}.png"))
                    : string.Empty;
                buildings.Add(new BuildingEntry(id, nameStored, typeStored, categoryStored, iconPath));
            }
        }
        catch
        {
            // External editing may leave the XML temporarily invalid; validation reports the details.
        }
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

    private void RefreshBuildingNodesInTree()
    {
        if (!EnsureModOpen(showMessage: false))
        {
            return;
        }

        var buildingsXmlPath = GetFullPath(BuildingsXmlRelativePath);
        if (TryFindTreeViewItemByTag(ModFileTree, buildingsXmlPath) is not TreeViewItem buildingsXmlNode)
        {
            BuildOverviewNavigationTree();
            return;
        }

        buildingsXmlNode.Items.Clear();
        foreach (var b in buildings.OrderBy(b => b.Id))
        {
            buildingsXmlNode.Items.Add(new TreeViewItem
            {
                Header = b.DisplayName,
                Tag = new BuildingTreeTag(currentModRoot!, b.Id),
                IsExpanded = false
            });
        }
    }

    private void LoadTree() => BuildOverviewNavigationTree();

    private static HashSet<string> CaptureTreeExpandedTags(ItemsControl parent)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpandedTags(parent, expanded);
        return expanded;
    }

    private static void CollectExpandedTags(ItemsControl parent, HashSet<string> expanded)
    {
        foreach (var item in parent.Items)
        {
            if (item is not TreeViewItem node)
            {
                continue;
            }

            if (node.IsExpanded && TryGetTreeTagKey(node.Tag, out var key))
            {
                expanded.Add(key);
            }

            CollectExpandedTags(node, expanded);
        }
    }

    private static void RestoreTreeState(ItemsControl parent, HashSet<string> expandedTags, object? selectedTag)
    {
        foreach (var item in parent.Items)
        {
            if (item is not TreeViewItem node)
            {
                continue;
            }

            if (TryGetTreeTagKey(node.Tag, out var key) && expandedTags.Contains(key))
            {
                node.IsExpanded = true;
            }

            RestoreTreeState(node, expandedTags, selectedTag);

            if (TagsMatch(node.Tag, selectedTag))
            {
                node.IsSelected = true;
                node.BringIntoView();
            }
        }
    }

    private static TreeViewItem? TryFindTreeViewItemByTag(ItemsControl parent, object tag)
    {
        foreach (var item in parent.Items)
        {
            if (item is not TreeViewItem node)
            {
                continue;
            }

            if (TagsMatch(node.Tag, tag))
            {
                return node;
            }

            var found = TryFindTreeViewItemByTag(node, tag);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool TagsMatch(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (left is OverviewModDirectoryTag leftTag && right is OverviewModDirectoryTag rightTag)
        {
            return string.Equals(leftTag.FullPath, rightTag.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        if (left is string leftPath && right is string rightPath)
        {
            return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        }

        if (left is BuildingTreeTag leftBuilding && right is BuildingTreeTag rightBuilding)
        {
            return leftBuilding.BuildingId == rightBuilding.BuildingId &&
                   string.Equals(leftBuilding.ModRoot, rightBuilding.ModRoot, StringComparison.OrdinalIgnoreCase);
        }

        return Equals(left, right);
    }

    private static bool TryGetTreeTagKey(object? tag, out string key)
    {
        switch (tag)
        {
            case null:
                key = string.Empty;
                return false;
            case string path:
                key = path;
                return true;
            case OverviewModDirectoryTag directoryTag:
                key = directoryTag.FullPath;
                return true;
            case BuildingTreeTag buildingTag:
                key = $"building:{buildingTag.ModRoot}:{buildingTag.BuildingId}";
                return true;
            default:
                key = tag.ToString() ?? string.Empty;
                return !string.IsNullOrEmpty(key);
        }
    }

    private static TreeViewItem? TryFindBuildingTreeItem(ItemsControl parent, string? modRoot, int buildingId)
    {
        if (string.IsNullOrWhiteSpace(modRoot))
        {
            return null;
        }

        return TryFindTreeViewItemByTag(parent, new BuildingTreeTag(modRoot, buildingId));
    }

    private void EnsureModActivated(string modRoot)
    {
        if (!Directory.Exists(modRoot))
        {
            return;
        }

        if (string.Equals(currentModRoot, modRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        OpenModFolder(modRoot);
    }

    private void SyncBuildingTileSelection(int buildingId)
    {
        var match = buildings.FirstOrDefault(b => b.Id == buildingId);
        if (match is not null)
        {
            BuildingTiles.SelectedItem = match;
            BuildingTiles.ScrollIntoView(match);
        }
    }

    private bool TrySelectModInTree(string modRoot)
    {
        var tag = new OverviewModDirectoryTag(modRoot);
        if (TryFindTreeViewItemByTag(ModFileTree, tag) is not TreeViewItem node)
        {
            return false;
        }

        node.IsSelected = true;
        node.BringIntoView();
        return true;
    }

    private static string? TryResolveModRootFromPath(string path)
    {
        if (Directory.Exists(path))
        {
            if (File.Exists(Path.Combine(path, MainXmlRelativePath)))
            {
                return path;
            }

            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, MainXmlRelativePath)))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static IReadOnlyList<BuildingEntry> ReadBuildingEntriesFromMod(string modRoot)
    {
        var buildingsXmlPath = Path.Combine(modRoot, BuildingsXmlRelativePath);
        if (!File.Exists(buildingsXmlPath))
        {
            return [];
        }

        var entries = new List<BuildingEntry>();
        try
        {
            var document = XDocument.Parse(ModXmlIO.ReadAllText(buildingsXmlPath));
            if (document.Root?.Name.LocalName != "ArrayOfModBuildingXML")
            {
                return entries;
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
                var iconPath = Path.Combine(modRoot, BuildingIconsRelativePath, $"{id}.png");
                entries.Add(new BuildingEntry(id, nameStored, typeStored, categoryStored, iconPath));
            }
        }
        catch
        {
            // 外部编辑可能导致暂时无效，树仍显示空建筑列表。
        }

        return entries;
    }

    private static void AppendModExplorerChildren(
        TreeViewItem modNode,
        string modRoot,
        IReadOnlyList<BuildingEntry> buildingEntries)
    {
        foreach (var relativePath in new[] { MainXmlRelativePath, ModRootAssets.IconRelativePath, ModRootAssets.ScreenshotRelativePath })
        {
            var path = Path.Combine(modRoot, relativePath);
            modNode.Items.Add(new TreeViewItem
            {
                Header = File.Exists(path) ? relativePath : $"{relativePath}（缺失）",
                Tag = path,
                IsExpanded = false
            });
        }

        var prefabsPath = Path.Combine(modRoot, "Prefabs");
        if (Directory.Exists(prefabsPath))
        {
            modNode.Items.Add(CreateTreeItem(new DirectoryInfo(prefabsPath)));
        }

        var thingPath = Path.Combine(modRoot, "Thing");
        var buildingsXmlPath = Path.Combine(modRoot, BuildingsXmlRelativePath);
        if (!Directory.Exists(thingPath) && !File.Exists(buildingsXmlPath))
        {
            return;
        }

        var thingNode = new TreeViewItem
        {
            Header = "Thing",
            Tag = thingPath,
            IsExpanded = false
        };

        var buildingsXmlNode = new TreeViewItem
        {
            Header = File.Exists(buildingsXmlPath) ? "Buildings.xml" : "Buildings.xml（未创建）",
            Tag = buildingsXmlPath,
            IsExpanded = false
        };

        foreach (var b in buildingEntries.OrderBy(b => b.Id))
        {
            buildingsXmlNode.Items.Add(new TreeViewItem
            {
                Header = b.DisplayName,
                Tag = new BuildingTreeTag(modRoot, b.Id),
                IsExpanded = false
            });
        }

        thingNode.Items.Add(buildingsXmlNode);
        modNode.Items.Add(thingNode);
    }

    private void BuildOverviewNavigationTree()
    {
        var expandedTags = CaptureTreeExpandedTags(ModFileTree);
        var selectedTag = (ModFileTree.SelectedItem as TreeViewItem)?.Tag;

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

        foreach (var entry in OrderedOverviewMods())
        {
            var modNode = CreateOverviewModTreeItem(entry, isExpanded: false);
            var buildingEntries = ReadBuildingEntriesFromMod(entry.FullPath);
            AppendModExplorerChildren(modNode, entry.FullPath, buildingEntries);
            root.Items.Add(modNode);
        }

        ModFileTree.Items.Add(root);
        RestoreTreeState(ModFileTree, expandedTags, selectedTag);
        UpdateToolbarForSelection();
    }

    private IEnumerable<OverviewModEntry> OrderedOverviewMods()
    {
        return overviewMods
            .OrderByDescending(entry => entry.HasMainXml)
            .ThenBy(entry => entry.FolderName, StringComparer.OrdinalIgnoreCase);
    }

    private TreeViewItem CreateOverviewModTreeItem(OverviewModEntry entry, bool isExpanded)
    {
        var item = new TreeViewItem
        {
            Header = entry.DisplayName,
            Tag = new OverviewModDirectoryTag(entry.FullPath),
            IsExpanded = isExpanded
        };

        if (entry.HasMainXml)
        {
            item.Foreground = (System.Windows.Media.Brush)FindResource("Theme.TreeViewModValidForeground");
        }

        return item;
    }

    private static TreeViewItem? TryGetTreeViewItemFromSource(DependencyObject? source)
    {
        while (source is not null && source is not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as TreeViewItem;
    }

    private static ListBoxItem? TryGetListBoxItemFromSource(DependencyObject? source)
    {
        while (source is not null && source is not ListBoxItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as ListBoxItem;
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

    private sealed record BuildingEntry(int Id, string Name, string Type, string Category, string IconPath)
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

        public string TileLabel
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Name)
                    ? (string.IsNullOrWhiteSpace(Type) ? "UNKNOWN" : Type)
                    : Name;
                return $"{Id} - {name}";
            }
        }

        public BitmapImage? IconImage => LoadBitmapIfExists(IconPath);
    }

    private sealed record OverviewModDirectoryTag(string FullPath);

    internal sealed record NewBuilding(int Id, string Name, string Type, int SizeX, int SizeY, int Health);
    internal sealed class CraftMaterial
    {
        public CraftMaterial(int id, int count)
        {
            Id = id;
            Count = count;
        }

        public int Id { get; set; }
        public int Count { get; set; }
        public string DisplayText => FormattableString.Invariant($"{Id}x{Count}");
    }

    internal sealed record EditableBuilding(
        int Id,
        string Name,
        string Type,
        int Direction,
        int Capbility,
        int WorkbenchId,
        int Health,
        int SizeX,
        int SizeY,
        IReadOnlyList<CraftMaterial> Materials);

    private static XElement CreateMaterialsElement(IReadOnlyList<CraftMaterial> materials) =>
        new(
            "materials",
            materials.Select(m =>
                new XElement("ModCraftMaterialData",
                    new XElement("id", m.Id),
                    new XElement("count", m.Count))));

    private static XElement CreateModBuildingXmlElement(EditableBuilding b) =>
        new(
            "ModBuildingXML",
            new XElement("id", b.Id),
            new XElement("name", b.Name),
            new XElement("type", b.Type),
            new XElement("direction", b.Direction),
            new XElement("capbility", b.Capbility),
            new XElement("workbenchId", b.WorkbenchId),
            new XElement("health", b.Health),
            new XElement("size",
                new XElement("x", b.SizeX),
                new XElement("y", b.SizeY)),
            CreateMaterialsElement(b.Materials));

    private static IReadOnlyList<CraftMaterial> ReadMaterials(XElement element)
    {
        var materials = element.Elements().FirstOrDefault(x => x.Name.LocalName == "materials");
        if (materials is null)
        {
            return [];
        }

        return materials
            .Elements()
            .Where(x => x.Name.LocalName == "ModCraftMaterialData")
            .Select(x =>
            {
                var idText = x.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
                var countText = x.Elements().FirstOrDefault(e => e.Name.LocalName == "count")?.Value;
                return int.TryParse(idText, out var id) && int.TryParse(countText, out var count)
                    ? new CraftMaterial(id, count)
                    : null;
            })
            .Where(x => x is not null)
            .Cast<CraftMaterial>()
            .ToArray();
    }

    private (int nextId, string nextName) SuggestNewBuildingDefaults()
    {
        // BuildingId 独立于 ModId：仅建议在 1–99 范围内。
        // 新建时默认取“当前最大 BuildingId + 1”（无则从 1 起），不做强约束拦截。
        var maxId = buildings.Count == 0 ? 0 : buildings.Max(b => b.Id);
        var nextId = Math.Max(1, maxId + 1);
        return (nextId, $"NewBuilding_{nextId}");
    }

    private void AddBuildingToBuildingsXml(NewBuilding b)
    {
        // 基于编辑器内容增量写入，避免“保存 XML”前读写磁盘导致用户修改丢失。
        var doc = XDocument.Parse(buildingsXmlText, LoadOptions.PreserveWhitespace);
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

        var element = CreateModBuildingXmlElement(
            new EditableBuilding(
                b.Id,
                b.Name,
                b.Type,
                1,
                BuildingFieldOptions.DefaultCapbility,
                gameData.DefaultWorkbenchId,
                BuildingFieldOptions.FixedHealth,
                b.SizeX,
                b.SizeY,
                []));

        doc.Root.Add(element);
        buildingsXmlText = ModXmlFormatter.Serialize(doc);

        // 确保图片目录存在（具体图片由用户导入）
        Directory.CreateDirectory(GetFullPath(BuildingImagesRelativePath));
        Directory.CreateDirectory(GetFullPath(BuildingIconsRelativePath));
    }

    private sealed record OverviewModEntry(string FullPath, string FolderName, bool HasMainXml, string Category)
    {
        public string DisplayName
        {
            get
            {
                var baseName = HasMainXml ? FolderName : $"{FolderName}（未创建 main.xml）";
                var category = Category?.Trim();
                return string.IsNullOrWhiteSpace(category) ? baseName : $"{category} - {baseName}";
            }
        }
    }

    private EditableBuilding GetEditableBuildingFromSelectionOrDefaults()
    {
        // 如果用户在列表里选了建筑，就尽量从 XML 里读出完整字段做“编辑”
        if (BuildingTiles.SelectedItem is BuildingEntry selected)
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
            return new EditableBuilding(selected.Id, nameForEdit, typeForEdit, 1, BuildingFieldOptions.DefaultCapbility, gameData.DefaultWorkbenchId, BuildingFieldOptions.FixedHealth, 1, 1, []);
        }

        var (nextId, nextName) = SuggestNewBuildingDefaults();
        return new EditableBuilding(nextId, nextName, "Building", 1, BuildingFieldOptions.DefaultCapbility, gameData.DefaultWorkbenchId, BuildingFieldOptions.FixedHealth, 1, 1, []);
    }

    private EditableBuilding? TryReadEditableBuildingById(int id)
    {
        try
        {
            var doc = XDocument.Parse(buildingsXmlText);
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
                NormalizeCapbility(ReadInt("capbility", BuildingFieldOptions.DefaultCapbility)),
                NormalizeWorkbenchId(ReadInt("workbenchId", gameData.DefaultWorkbenchId)),
                BuildingFieldOptions.FixedHealth,
                sx,
                sy,
                ReadMaterials(el));
        }
        catch
        {
            return null;
        }
    }

    private bool RemoveBuildingFromBuildingsXml(int id)
    {
        var doc = XDocument.Parse(buildingsXmlText, LoadOptions.PreserveWhitespace);
        if (doc.Root?.Name.LocalName != "ArrayOfModBuildingXML")
        {
            throw new InvalidOperationException("Buildings.xml 根节点不是 ArrayOfModBuildingXML。");
        }

        var existing = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "ModBuildingXML" &&
                                                               int.TryParse(e.Elements().FirstOrDefault(x => x.Name.LocalName == "id")?.Value, out var elId) &&
                                                               elId == id);

        if (existing is null)
        {
            return false;
        }

        existing.Remove();
        buildingsXmlText = ModXmlFormatter.Serialize(doc);
        return true;
    }

    private void TryDeleteBuildingImageFiles(int id)
    {
        foreach (var rel in new[]
                 {
                     Path.Combine(BuildingImagesRelativePath, $"{id}.png"),
                     Path.Combine(BuildingIconsRelativePath, $"{id}.png")
                 })
        {
            try
            {
                var full = GetFullPath(rel);
                if (File.Exists(full))
                {
                    File.Delete(full);
                }
            }
            catch
            {
                // 忽略单文件删除失败，避免阻断 XML 已删后的保存流程
            }
        }
    }

    private void TryRenameBuildingImageFiles(int oldId, int newId)
    {
        if (oldId == newId)
        {
            return;
        }

        foreach (var relativeDir in new[] { BuildingImagesRelativePath, BuildingIconsRelativePath })
        {
            try
            {
                var source = GetFullPath(Path.Combine(relativeDir, $"{oldId}.png"));
                if (!File.Exists(source))
                {
                    continue;
                }

                var target = GetFullPath(Path.Combine(relativeDir, $"{newId}.png"));
                if (File.Exists(target))
                {
                    continue;
                }

                File.Move(source, target);
            }
            catch (Exception ex)
            {
                Log($"迁移建筑图片失败（{oldId}→{newId}）：{ex.Message}");
            }
        }
    }

    private void UpsertBuildingToBuildingsXml(EditableBuilding b)
    {
        var doc = XDocument.Parse(buildingsXmlText, LoadOptions.PreserveWhitespace);
        if (doc.Root?.Name.LocalName != "ArrayOfModBuildingXML")
        {
            throw new InvalidOperationException("Buildings.xml 根节点不是 ArrayOfModBuildingXML。");
        }

        var existing = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "ModBuildingXML" &&
                                                               int.TryParse(e.Elements().FirstOrDefault(x => x.Name.LocalName == "id")?.Value, out var id) &&
                                                               id == b.Id);

        var element = CreateModBuildingXmlElement(b);

        if (existing is null)
        {
            doc.Root.Add(element);
        }
        else
        {
            existing.ReplaceWith(element);
        }

        buildingsXmlText = ModXmlFormatter.Serialize(doc);

        Directory.CreateDirectory(GetFullPath(BuildingImagesRelativePath));
        Directory.CreateDirectory(GetFullPath(BuildingIconsRelativePath));
    }

    private static string? TryReadMainXmlRootValue(string mainXmlPath, string elementName)
    {
        try
        {
            if (!File.Exists(mainXmlPath))
            {
                return null;
            }

            var doc = XDocument.Parse(ModXmlIO.ReadAllText(mainXmlPath));
            if (doc.Root?.Name.LocalName != "defs")
            {
                return null;
            }

            return doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == elementName)?.Value;
        }
        catch
        {
            return null;
        }
    }

    private int? TryDetermineCurrentModBaseId()
    {
        var baseIdFromMain = TryReadMainXmlRootValue(GetFullPath(MainXmlRelativePath), "id");
        if (int.TryParse(baseIdFromMain?.Trim(), out var baseId) &&
            baseId is > 10000000 and <= 200000000)
        {
            return baseId;
        }

        return null;
    }

}