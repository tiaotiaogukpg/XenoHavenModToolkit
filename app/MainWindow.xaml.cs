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
    private bool suppressTileClearOnTreeDeselect;

    public MainWindow()
    {
        InitializeComponent();
        BuildingTiles.ItemsSource = buildings;
        UpdateTopBarState();
    }

    public void InitializeOnStartup()
    {
        Log($"运行模式：{AppPaths.RunModeLabel}");
        Log($"应用基础目录：{AppPaths.GetApplicationBaseDirectory()}");
        Log($"配置文件路径：{AppPaths.GetConfigFilePath()}");
        Log($"Mods 目录路径：{AppPaths.GetModsDirectory()}");

        settings = AppSettings.Load();
        if (!gameData.IsReady && !string.IsNullOrWhiteSpace(gameData.LoadError))
        {
            Log(gameData.LoadError);
        }

        if (!EnsureModsDirectory())
        {
            UpdateTopBarState();
            return;
        }

        RefreshOverviewList();
        BuildOverviewNavigationTree();
        Log($"扫描到的工程数量：{overviewMods.Count}");

        if (settings.OpenLastModOnStartup &&
            !string.IsNullOrWhiteSpace(settings.LastModPath) &&
            Directory.Exists(settings.LastModPath))
        {
            TrySelectModInTree(settings.LastModPath);
        }

        UpdateTopBarState();
    }

    private void OpenModFolder_Click(object sender, RoutedEventArgs e)
    {
        PromptOpenModFolder();
    }

    /// <summary>
    /// 确保 Mods 目录存在；失败时记录完整路径与原因，并提示用户。
    /// </summary>
    private bool EnsureModsDirectory()
    {
        var modsDir = AppPaths.GetModsDirectory();
        try
        {
            if (!Directory.Exists(modsDir))
            {
                Directory.CreateDirectory(modsDir);
                Log($"已创建 Mods 目录：{modsDir}");
            }

            return true;
        }
        catch (Exception ex)
        {
            var message = $"无法创建 Mods 目录：{modsDir}{Environment.NewLine}{ex.Message}";
            Log(message);
            System.Windows.MessageBox.Show(this, message, "Mods 目录", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
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
        if (!EnsureModsDirectory())
        {
            return;
        }

        RefreshOverviewList();
        BuildOverviewNavigationTree();
        if (EnsureModOpen(showMessage: false))
        {
            ReloadBuildingsXmlFromDisk();
            ParseBuildingsFromEditor();
            RefreshBuildingNodesInTree();
        }

        Log($"已刷新 Mod 总览列表（工程数：{overviewMods.Count}）。");
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
        var modsRoot = AppPaths.GetModsDirectory();
        if (!Directory.Exists(modsRoot))
        {
            return;
        }

        foreach (var dir in new DirectoryInfo(modsRoot).GetDirectories().OrderBy(d => d.Name))
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

    private void TopBar_ButtonClick(object sender, RoutedEventArgs e)
    {
        ClearSidebarSelection();
    }

    private void SaveSettingsOrReport()
    {
        var error = settings.TrySave();
        if (error is null)
        {
            return;
        }

        Log(error);
        System.Windows.MessageBox.Show(this, error, "保存配置", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ClearSidebarSelection()
    {
        if (ModFileTree.SelectedItem is TreeViewItem selected)
        {
            selected.IsSelected = false;
        }

        BuildingTiles.SelectedItem = null;
        UpdateToolbarForSelection();
    }

    private void OpenModFolder(string folder)
    {
        currentModRoot = folder;
        CurrentModPathText.Text = folder;
        settings.LastModPath = folder;
        SaveSettingsOrReport();
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

        OpenModButton.IsEnabled = true;
        NewModButton.IsEnabled = IsModsDirectoryReady() && (selection == TreeSelectionKind.OverviewRoot || !opened);

        ModInfoButton.IsEnabled = opened &&
                                  selection is TreeSelectionKind.ModRoot
                                               or TreeSelectionKind.MainXml
                                               or TreeSelectionKind.BuildingsXml
                                               or TreeSelectionKind.BuildingNode
                                               or TreeSelectionKind.ModOther;
        AddBuildingButton.IsEnabled = opened &&
                                      selection is TreeSelectionKind.ModRoot
                                                   or TreeSelectionKind.OverviewModFolder
                                                   or TreeSelectionKind.BuildingsXml;
        EditBuildingButton.IsEnabled = opened && selection == TreeSelectionKind.BuildingNode;
        DeleteBuildingButton.IsEnabled = opened && selection == TreeSelectionKind.BuildingNode;
        DeleteModButton.IsEnabled = ModFileTree.SelectedItem is TreeViewItem { Tag: OverviewModDirectoryTag } ||
                                    (opened && selection is TreeSelectionKind.ModRoot
                                                        or TreeSelectionKind.MainXml
                                                        or TreeSelectionKind.BuildingsXml
                                                        or TreeSelectionKind.BuildingNode
                                                        or TreeSelectionKind.ModOther);
    }

    private static bool IsModsDirectoryReady()
        => Directory.Exists(AppPaths.GetModsDirectory());

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
            case string path when path.Equals(AppPaths.GetModsDirectory(), StringComparison.OrdinalIgnoreCase):
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

    private int NormalizeProductionLineId(int raw)
        => gameData.FindProductionLine(raw)?.Id ?? gameData.DefaultProductionLineId;

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
            if (!EnsureModsDirectory())
            {
                return;
            }

            var modsRoot = AppPaths.GetModsDirectory();
            var dialog = new NewModWindow { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            var folderName = CreateUniqueModFolderName(modsRoot, dialog.Result.Name);
            var targetFolder = Path.Combine(modsRoot, folderName);

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

        if (!ConfirmDialog.Show(this, "清理 .meta", "将删除当前 Mod 目录内所有 *.meta 文件。该操作只建议用于发布版 Mod。是否继续？"))
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

    private void ModFileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item)
        {
            if (!suppressTileClearOnTreeDeselect)
            {
                BuildingTiles.SelectedItem = null;
            }

            UpdateToolbarForSelection();
            return;
        }

        switch (item.Tag)
        {
            case OverviewModDirectoryTag directoryTag:
                EnsureModActivated(directoryTag.FullPath);
                BuildingTiles.SelectedItem = null;
                break;
            case BuildingTreeTag buildingTag:
                EnsureModActivated(buildingTag.ModRoot);
                SyncBuildingTileSelection(buildingTag.BuildingId);
                break;
            case string path:
                BuildingTiles.SelectedItem = null;

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

        // 点右侧 Building 时，清除左侧树的上次选中痕迹，避免双向残留高亮
        if (BuildingTiles.SelectedItem is BuildingEntry &&
            ModFileTree.SelectedItem is TreeViewItem selected)
        {
            suppressTileClearOnTreeDeselect = true;
            try
            {
                selected.IsSelected = false;
            }
            finally
            {
                suppressTileClearOnTreeDeselect = false;
            }
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

    private void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        var modRoot = (ModFileTree.SelectedItem is TreeViewItem { Tag: OverviewModDirectoryTag directoryTag })
            ? directoryTag.FullPath
            : currentModRoot;

        if (string.IsNullOrWhiteSpace(modRoot) || !Directory.Exists(modRoot))
        {
            return;
        }

        var folderName = new DirectoryInfo(modRoot).Name;
        if (!ConfirmDialog.Show(this, "删除 MOD", $"你确定要删除 “{folderName}“吗？"))
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(currentModRoot) &&
                string.Equals(currentModRoot, modRoot, StringComparison.OrdinalIgnoreCase))
            {
                CloseCurrentMod();
            }

            Directory.Delete(modRoot, recursive: true);
            RefreshOverviewList();
            BuildOverviewNavigationTree();
            UpdateTopBarState();
            Log($"已删除 MOD：{folderName}");
        }
        catch (Exception ex)
        {
            Log($"删除 MOD 失败：{ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "删除 MOD", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        if (!ConfirmDialog.Show(this, "删除 Building", $"你确定要删除 “{selected.Id}-{selected.Name}” 吗？"))
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
            var initial = new EditableBuilding(
                nextId,
                nextName,
                "BOX",
                1,
                BuildingFieldOptions.DefaultCapbility,
                gameData.DefaultWorkbenchId,
                0,
                BuildingFieldOptions.FixedHealth,
                1,
                1,
                [],
                BuildingFieldOptions.DefaultBarrier("BOX"));

            var dialog = new BuildingEditorWindow(initial, currentModRoot!, gameData) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            UpsertBuildingToBuildingsXml(dialog.Result);
            SaveBuildingsXmlToDisk();
            ParseBuildingsFromEditor();
            RefreshBuildingNodesInTree();
            Log($"已新增 Building：{dialog.Result.Id} - {dialog.Result.Name} ({dialog.Result.Type})。");
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
            var typeValue = element.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value ?? string.Empty;
            if (BuildingFieldOptions.ShowsCapbility(typeValue))
            {
                ValidatePositiveInt(element, "capbility", id, messages);
            }

            ValidatePositiveInt(element, "workbenchId", id, messages);
            if (BuildingFieldOptions.RequiresSimulateId(typeValue))
            {
                ValidateSimulateId(element, id, messages);
            }

            ValidatePositiveInt(element, "health", id, messages);
            ValidateOptionalBool(element, "barrier", id, messages);

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

            ValidateOptionalBool(element, "barrier", id, messages);
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

    private static void ValidateOptionalBool(XElement parent, string childName, int buildingId, List<string> messages)
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == childName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!bool.TryParse(value.Trim(), out _))
        {
            messages.Add($"[错误] 建筑 {buildingId} 的 building.{childName} 需要是 true 或 false。");
        }
    }

    private void ValidateSimulateId(XElement parent, int buildingId, List<string> messages)
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == "simulateId")?.Value;
        if (!int.TryParse(value, out var simulateId) || simulateId <= 0)
        {
            messages.Add($"[错误] 建筑 {buildingId} 的 building.simulateId 需要是正整数。");
            return;
        }

        if (gameData.ProductionLines.Count == 0)
        {
            messages.Add($"[警告] 未加载 {GameDataPaths.ProductionLinesFileName}，无法校验建筑 {buildingId} 的 simulateId。");
            return;
        }

        if (gameData.FindProductionLine(simulateId) is null)
        {
            messages.Add($"[错误] 建筑 {buildingId} 的 simulateId 不在 {GameDataPaths.ProductionLinesFileName} 中：{simulateId}。");
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
        var modsRoot = AppPaths.GetModsDirectory();

        ModFileTree.Items.Clear();
        if (!Directory.Exists(modsRoot))
        {
            ModFileTree.Items.Add(new TreeViewItem
            {
                Header = "（Mods 目录不可用）",
                IsEnabled = false
            });
            return;
        }

        RefreshOverviewList();
        var root = new TreeViewItem
        {
            Header = "Mod",
            Tag = modsRoot,
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
        int SimulateId,
        int Health,
        int SizeX,
        int SizeY,
        IReadOnlyList<CraftMaterial> Materials,
        bool Barrier);

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
            BuildingFieldOptions.ShowsCapbility(b.Type)
                ? new XElement("capbility", b.Capbility)
                : null,
            new XElement("workbenchId", b.WorkbenchId),
            BuildingFieldOptions.RequiresSimulateId(b.Type) && b.SimulateId > 0
                ? new XElement("simulateId", b.SimulateId)
                : null,
            new XElement("health", b.Health),
            new XElement("size",
                new XElement("x", b.SizeX),
                new XElement("y", b.SizeY)),
            CreateMaterialsElement(b.Materials),
            new XElement("barrier", b.Barrier ? "true" : "false"));

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
        // Building 本地序号独立于 ModId：图片按 1.png、2.png… 命名，XML id 同步使用该序号。
        // 取当前未占用的最小正整数（从 1 起），避免沿用旧的「ModId + 序号」大 ID。
        var usedIds = buildings.Select(b => b.Id).ToHashSet();
        var nextId = 1;
        while (usedIds.Contains(nextId))
        {
            nextId++;
        }

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
                0,
                BuildingFieldOptions.FixedHealth,
                b.SizeX,
                b.SizeY,
                [],
                BuildingFieldOptions.DefaultBarrier(b.Type)));

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
            return new EditableBuilding(selected.Id, nameForEdit, typeForEdit, 1, BuildingFieldOptions.DefaultCapbility, gameData.DefaultWorkbenchId, 0, BuildingFieldOptions.FixedHealth, 1, 1, [], BuildingFieldOptions.DefaultBarrier(typeForEdit));
        }

        var (nextId, nextName) = SuggestNewBuildingDefaults();
        return new EditableBuilding(nextId, nextName, "Building", 1, BuildingFieldOptions.DefaultCapbility, gameData.DefaultWorkbenchId, 0, BuildingFieldOptions.FixedHealth, 1, 1, [], BuildingFieldOptions.DefaultBarrier("Building"));
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

            bool ReadBool(string name, bool fallback)
            {
                var text = el.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return fallback;
                }

                return bool.TryParse(text, out var v) ? v : fallback;
            }

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
                NormalizeProductionLineId(ReadInt("simulateId", gameData.DefaultProductionLineId)),
                BuildingFieldOptions.FixedHealth,
                sx,
                sy,
                ReadMaterials(el),
                ReadBool("barrier", BuildingFieldOptions.DefaultBarrier(typeValue)));
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