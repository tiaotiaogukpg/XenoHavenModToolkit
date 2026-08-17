using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    private static readonly string DynamicsXmlRelativePath = Path.Combine("Thing", "Dynamic", "Dynamics.xml");
    private static readonly string DynamicImagesRelativePath = Path.Combine("Thing", "Dynamic", "images");
    private static readonly string DynamicIconsRelativePath = Path.Combine("Thing", "Dynamic", "images", "icon");
    private static readonly string DynamicFolderRelativePath = Path.Combine("Thing", "Dynamic");
    /// <summary>曾误放在 Mod 根下的 Dynamic/，打开时迁回 Thing/Dynamic。</summary>
    private static readonly string LegacyRootDynamicFolderRelativePath = "Dynamic";

    private readonly ObservableCollection<BuildingEntry> buildings = [];
    private readonly ObservableCollection<BuildingEntry> dynamics = [];
    private readonly ObservableCollection<BuildingEntry> combinedTiles = [];
    private readonly ObservableCollection<OverviewModEntry> overviewMods = [];
    private readonly GameDataCatalog gameData = GameDataCatalog.Load();
    private string mainXmlText = string.Empty;
    private string buildingsXmlText = string.Empty;
    private string dynamicsXmlText = string.Empty;
    private string? currentModRoot;
    private int? currentModBaseId;
    private AppSettings settings = new();
    private bool suppressTileClearOnTreeDeselect;
    private SteamSession? steamSession;
    private TileBoardMode tileBoardMode = TileBoardMode.Buildings;

    private enum TileBoardMode
    {
        Buildings,
        Dynamics,
        Combined
    }

    private bool suppressLanguageComboEvent;

    public MainWindow()
    {
        InitializeComponent();
        BuildingTiles.ItemsSource = buildings;
        Closed += MainWindow_Closed;
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        InitLanguageCombo();
        UpdateWindowTitle();
        UpdateTopBarState();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowTitle();
    }

    public void InitializeOnStartup()
    {
        settings = AppSettings.Load();
        SyncLanguageCombo(LocalizationManager.Resolve(settings.Language));

        Log($"版本：{AppVersion.Display}");
        Log($"运行模式：{AppPaths.RunModeLabel}");
        Log($"应用基础目录：{AppPaths.GetApplicationBaseDirectory()}");
        Log($"配置文件路径：{AppPaths.GetConfigFilePath()}");
        Log($"Mods 目录路径：{AppPaths.GetModsDirectory()}");

        ConnectSteam(showFailureDialog: true);

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

    private void InitLanguageCombo()
    {
        suppressLanguageComboEvent = true;
        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationManager.ToDisplayName(AppLanguage.ZhCn),
            Tag = AppLanguage.ZhCn
        });
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationManager.ToDisplayName(AppLanguage.En),
            Tag = AppLanguage.En
        });
        SyncLanguageCombo(LocalizationManager.Current);
        suppressLanguageComboEvent = false;
    }

    private void SyncLanguageCombo(AppLanguage language)
    {
        suppressLanguageComboEvent = true;
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (item.Tag is AppLanguage lang && lang == language)
            {
                LanguageCombo.SelectedItem = item;
                break;
            }
        }

        suppressLanguageComboEvent = false;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressLanguageComboEvent)
        {
            return;
        }

        if (LanguageCombo.SelectedItem is not ComboBoxItem { Tag: AppLanguage language })
        {
            return;
        }

        if (language == LocalizationManager.Current)
        {
            return;
        }

        LocalizationManager.ApplyLanguage(language);
        settings.Language = LocalizationManager.ToSettingsValue(language);
        SaveSettingsOrReport();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateWindowTitle();

        if (string.IsNullOrWhiteSpace(currentModRoot))
        {
            CurrentModPathText.Text = Loc.Get("Str.Main.NoModOpen");
        }

        RefreshSteamStatusUi();
    }

    private void UpdateWindowTitle()
    {
        Title = AppVersion.WindowTitle;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        AboutWindow.Show(this);
    }

    private void ConnectSteam(bool showFailureDialog)
    {
        steamSession?.Dispose();
        steamSession = SteamSession.TryStart(SteamAppIds.XenoHaven);
        RefreshSteamStatusUi();

        if (steamSession.IsAvailable)
        {
            Log($"Steam 已连接：{steamSession.PersonaName}（SteamID {steamSession.SteamId64}，AppID {steamSession.AppId}）");
            return;
        }

        var reason = steamSession.FailureReason ?? Loc.Get("Str.UnknownReason");
        Log($"Steam 未连接：{reason}");
        if (showFailureDialog)
        {
            System.Windows.MessageBox.Show(
                this,
                Loc.Format("Str.Main.SteamConnectFailed", reason),
                Loc.Get("Str.Main.SteamAccountTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RefreshSteamStatusUi()
    {
        if (SteamStatusText is null)
        {
            return;
        }

        SteamStatusText.Text = steamSession?.StatusText ?? Loc.Get("Str.Main.SteamDisconnected");
        SteamStatusText.ToolTip = steamSession?.IsAvailable == true
            ? Loc.Get("Str.Main.SteamConnectedTip")
            : steamSession?.FailureReason ?? Loc.Get("Str.Main.SteamDisconnected");
        if (SteamReconnectButton is not null)
        {
            SteamReconnectButton.IsEnabled = steamSession?.IsAvailable != true;
        }

        UpdateToolbarForSelection();
    }

    private void SteamReconnect_Click(object sender, RoutedEventArgs e)
    {
        ConnectSteam(showFailureDialog: true);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
        steamSession?.Dispose();
        steamSession = null;
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
            System.Windows.MessageBox.Show(this, message, Loc.Get("Str.Main.ModsDirTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void PromptOpenModFolder()
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = Loc.Get("Str.Main.OpenModFolderDescription"),
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
            ReloadDynamicsXmlFromDisk();
            ParseDynamicsFromEditor();
            RefreshBuildingNodesInTree();
            RefreshDynamicNodesInTree();
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
        else if (item.Tag is DynamicTreeTag)
        {
            if (EnsureModOpen() && EnsureGameDataReady())
            {
                OpenDynamicEditor_Click(sender, e);
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
                category = TryReadMainXmlRootValue(mainXmlPath, "category")
                    ?? TryReadMainXmlRootValue(mainXmlPath, "Category")
                    ?? string.Empty;
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
        // 立即清一次；再延迟清一次，避免模态窗关闭后焦点回到磁贴导致 ListBoxItem 再次自选中。
        ClearSidebarSelection();
        Dispatcher.BeginInvoke(ClearSidebarSelection, DispatcherPriority.Input);
    }

    private void SaveSettingsOrReport()
    {
        var error = settings.TrySave();
        if (error is null)
        {
            return;
        }

        Log(error);
        System.Windows.MessageBox.Show(this, error, Loc.Get("Str.Main.SaveSettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ClearSidebarSelection()
    {
        if (ModFileTree.SelectedItem is TreeViewItem selected)
        {
            selected.IsSelected = false;
        }

        BuildingTiles.UnselectAll();
        BuildingTiles.SelectedIndex = -1;
        BuildingTiles.SelectedItem = null;

        // 焦点若仍落在磁贴上，WPF 会在 GotFocus 时重新选中该项
        if (BuildingTiles.IsKeyboardFocusWithin)
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, this);
        }

        UpdateToolbarForSelection();
    }

    private void OpenModFolder(string folder)
    {
        currentModRoot = folder;
        CurrentModPathText.Text = folder;
        settings.LastModPath = folder;
        SaveSettingsOrReport();
        LoadKnownXmlFiles();
        EnsureDynamicScaffoldOnDisk();
        ParseBuildingsFromEditor();
        ParseDynamicsFromEditor();
        currentModBaseId = TryDetermineCurrentModBaseId();
        Log($"已打开 Mod：{folder}");
        BuildOverviewNavigationTree();
        TrySelectModInTree(folder);
        UpdateTopBarState();
    }

    /// <summary>
    /// 打开 Mod 时确保 Thing/Dynamic 存在（与 Thing/Buildings 同级）；并迁移误放在根目录的 Dynamic/。
    /// </summary>
    private void EnsureDynamicScaffoldOnDisk()
    {
        if (string.IsNullOrWhiteSpace(currentModRoot))
        {
            return;
        }

        try
        {
            TryMigrateRootDynamicFolderIntoThing(currentModRoot);

            Directory.CreateDirectory(Path.Combine(currentModRoot, DynamicFolderRelativePath, "images", "icon"));
            Directory.CreateDirectory(Path.Combine(currentModRoot, DynamicFolderRelativePath, "images", "parts"));
            var dynamicsPath = Path.Combine(currentModRoot, DynamicsXmlRelativePath);
            if (!File.Exists(dynamicsPath))
            {
                var empty = """
                    <?xml version="1.0" encoding="utf-8"?>
                    <ArrayOfModDynamicXML xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                    </ArrayOfModDynamicXML>
                    """;
                ModXmlIO.WriteAllText(dynamicsPath, empty);
            }

            dynamicsXmlText = ReadTextOrTemplate(
                DynamicsXmlRelativePath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <ArrayOfModDynamicXML xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                </ArrayOfModDynamicXML>
                """);
        }
        catch (Exception ex)
        {
            Log($"补齐 Dynamic 目录失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 将 Mod 根下的 Dynamic/ 迁回 Thing/Dynamic（与 Buildings 同级）。
    /// </summary>
    private static void TryMigrateRootDynamicFolderIntoThing(string modRoot)
    {
        var targetRoot = Path.Combine(modRoot, DynamicFolderRelativePath);
        var rootLegacy = Path.Combine(modRoot, LegacyRootDynamicFolderRelativePath);
        if (Directory.Exists(targetRoot) || !Directory.Exists(rootLegacy))
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(modRoot, "Thing"));
        Directory.Move(rootLegacy, targetRoot);
    }

    private void CloseCurrentMod(bool rebuildNavigationTree = true)
    {
        if (string.IsNullOrWhiteSpace(currentModRoot))
        {
            buildings.Clear();
            dynamics.Clear();
            combinedTiles.Clear();
            tileBoardMode = TileBoardMode.Buildings;
            BuildingTiles.ItemsSource = buildings;
            BuildingTiles.SelectedItem = null;
            UpdateTopBarState();
            return;
        }

        currentModRoot = null;
        currentModBaseId = null;
        CurrentModPathText.Text = Loc.Get("Str.Main.NoModOpen");

        buildings.Clear();
        dynamics.Clear();
        combinedTiles.Clear();
        tileBoardMode = TileBoardMode.Buildings;
        BuildingTiles.ItemsSource = buildings;
        BuildingTiles.SelectedItem = null;

        mainXmlText = string.Empty;
        buildingsXmlText = string.Empty;
        dynamicsXmlText = string.Empty;

        if (rebuildNavigationTree)
        {
            BuildOverviewNavigationTree();
        }

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
                                               or TreeSelectionKind.ThingFolder
                                               or TreeSelectionKind.BuildingsXml
                                               or TreeSelectionKind.BuildingNode
                                               or TreeSelectionKind.DynamicsXml
                                               or TreeSelectionKind.DynamicNode
                                               or TreeSelectionKind.ModOther;
        UploadWorkshopButton.IsEnabled = opened && steamSession?.IsAvailable == true;
        AddBuildingButton.IsEnabled = opened &&
                                      selection is TreeSelectionKind.ModRoot
                                                   or TreeSelectionKind.OverviewModFolder
                                                   or TreeSelectionKind.ThingFolder
                                                   or TreeSelectionKind.BuildingsXml
                                                   or TreeSelectionKind.DynamicsXml
                                                   or TreeSelectionKind.DynamicFolder
                                                   or TreeSelectionKind.BuildingsFolder;
        EditBuildingButton.IsEnabled = opened &&
                                       selection is TreeSelectionKind.BuildingNode
                                                    or TreeSelectionKind.DynamicNode;
        DeleteBuildingButton.IsEnabled = opened &&
                                         selection is TreeSelectionKind.BuildingNode
                                                      or TreeSelectionKind.DynamicNode;
        DeleteModButton.IsEnabled = ModFileTree.SelectedItem is TreeViewItem { Tag: OverviewModDirectoryTag } ||
                                    (opened && selection is TreeSelectionKind.ModRoot
                                                        or TreeSelectionKind.MainXml
                                                        or TreeSelectionKind.BuildingsXml
                                                        or TreeSelectionKind.BuildingNode
                                                        or TreeSelectionKind.DynamicsXml
                                                        or TreeSelectionKind.DynamicNode
                                                        or TreeSelectionKind.ModOther);
    }

    private static bool IsModsDirectoryReady()
        => Directory.Exists(AppPaths.GetModsDirectory());

    private static bool IsOverviewRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var left = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var right = Path.GetFullPath(AppPaths.GetModsDirectory()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return left.Equals(right, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private TreeSelectionKind ClassifyCurrentSelection()
    {
        // 右侧磁贴选中优先：合并视图下点磁贴时左侧树会被清空，需据此启用编辑/删除
        if (BuildingTiles.SelectedItem is BuildingEntry entry)
        {
            return IsDynamicEntry(entry) ? TreeSelectionKind.DynamicNode : TreeSelectionKind.BuildingNode;
        }

        if (ModFileTree.SelectedItem is TreeViewItem treeItem)
        {
            return ClassifyTreeItem(treeItem);
        }

        return TreeSelectionKind.None;
    }

    private bool IsShowingDynamicsTiles
        => tileBoardMode == TileBoardMode.Dynamics;

    private static bool IsDynamicEntry(BuildingEntry entry)
        => string.Equals(entry.Category, "Dynamic", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 按树选中上下文切换右侧瓷砖：
    /// Mod / Thing → Buildings+Dynamic；Buildings 分支只显示建筑；Dynamic 分支只显示生物。
    /// </summary>
    private void ApplyTileBoardForSelection(TreeSelectionKind selection)
    {
        var mode = selection switch
        {
            TreeSelectionKind.DynamicFolder
                or TreeSelectionKind.DynamicsXml
                or TreeSelectionKind.DynamicNode
                => TileBoardMode.Dynamics,
            TreeSelectionKind.BuildingsFolder
                or TreeSelectionKind.BuildingsXml
                or TreeSelectionKind.BuildingNode
                => TileBoardMode.Buildings,
            TreeSelectionKind.ModRoot
                or TreeSelectionKind.OverviewModFolder
                or TreeSelectionKind.MainXml
                or TreeSelectionKind.ThingFolder
                => TileBoardMode.Combined,
            _ => TileBoardMode.Buildings
        };

        if (mode == tileBoardMode && mode != TileBoardMode.Combined)
        {
            return;
        }

        tileBoardMode = mode;
        BuildingTiles.SelectedItem = null;
        BuildingTiles.ItemsSource = mode switch
        {
            TileBoardMode.Dynamics => dynamics,
            TileBoardMode.Combined => RebuildCombinedTiles(),
            _ => buildings
        };
    }

    private ObservableCollection<BuildingEntry> RebuildCombinedTiles()
    {
        combinedTiles.Clear();
        foreach (var building in buildings)
        {
            combinedTiles.Add(building);
        }

        foreach (var dynamic in dynamics)
        {
            combinedTiles.Add(dynamic);
        }

        return combinedTiles;
    }

    /// <summary>
    /// 增删改后刷新右侧列表；若当前是合并视图则保持合并，避免跳到仅 Buildings/Dynamics。
    /// </summary>
    private void RefreshTileBoardAfterMutation(TreeSelectionKind preferredWhenNotCombined)
    {
        if (tileBoardMode == TileBoardMode.Combined)
        {
            RebuildCombinedTiles();
            // 强制重绑，避免同路径 PNG 被 WPF 缓存导致瓷砖图标不更新。
            BuildingTiles.ItemsSource = null;
            BuildingTiles.ItemsSource = combinedTiles;
            return;
        }

        var mode = preferredWhenNotCombined switch
        {
            TreeSelectionKind.DynamicFolder
                or TreeSelectionKind.DynamicsXml
                or TreeSelectionKind.DynamicNode
                => TileBoardMode.Dynamics,
            _ => TileBoardMode.Buildings
        };

        tileBoardMode = mode;
        var source = mode == TileBoardMode.Dynamics ? dynamics : buildings;
        BuildingTiles.ItemsSource = null;
        BuildingTiles.ItemsSource = source;
    }

    private TreeSelectionKind ClassifyTreeItem(TreeViewItem item)
    {
        switch (item.Tag)
        {
            case string path when IsOverviewRootPath(path):
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
            case DynamicTreeTag dynamicTag when !string.IsNullOrWhiteSpace(currentModRoot) &&
                string.Equals(dynamicTag.ModRoot, currentModRoot, StringComparison.OrdinalIgnoreCase):
                return TreeSelectionKind.DynamicNode;
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

                    if (relativePath.Equals(DynamicsXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return TreeSelectionKind.DynamicsXml;
                    }
                }

                if (Directory.Exists(path))
                {
                    var relativePath = Path.GetRelativePath(currentModRoot, path);
                    if (relativePath.Equals("Thing", StringComparison.OrdinalIgnoreCase))
                    {
                        return TreeSelectionKind.ThingFolder;
                    }

                    if (relativePath.Equals(Path.Combine("Thing", "Buildings"), StringComparison.OrdinalIgnoreCase))
                    {
                        return TreeSelectionKind.BuildingsFolder;
                    }

                    if (relativePath.Equals(DynamicFolderRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return TreeSelectionKind.DynamicFolder;
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
            gameData.LoadError ?? Loc.Get("Str.Main.GameDataNotReady"),
            Loc.Get("Str.Main.GameDataTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private int NormalizeWorkbenchId(int raw)
        => gameData.FindWorkbench(raw)?.Id ?? gameData.DefaultWorkbenchId;

    /// <summary>新建傀儡时优先选原版制造床（若表中有），否则与 Buildings 相同用默认工作台。</summary>
    private int PreferFarmingGolemWorkbenchOrDefault()
        => gameData.FindWorkbench(DynamicFieldOptions.FarmingGolemWorkbenchId)?.Id
           ?? gameData.DefaultWorkbenchId;

    private static double ClampVisualScale(double raw)
    {
        if (raw < DynamicFieldOptions.MinVisualScale)
        {
            return DynamicFieldOptions.MinVisualScale;
        }

        return raw > DynamicFieldOptions.MaxVisualScale ? DynamicFieldOptions.MaxVisualScale : raw;
    }

    private int NormalizeProductionLineId(int raw)
        => gameData.FindProductionLine(raw)?.Id ?? gameData.DefaultProductionLineId;

    private int NormalizeSimulateId(string type, int raw)
    {
        if (DynamicFieldOptions.IsFarmingGolem(type))
        {
            return FarmingGolemOptions.IsKnownSimulateId(raw)
                ? raw
                : FarmingGolemOptions.DefaultSimulateId;
        }

        if (BuildingFieldOptions.IsProductionLine(type))
        {
            return NormalizeProductionLineId(raw > 0 ? raw : gameData.DefaultProductionLineId);
        }

        return raw > 0 ? raw : 0;
    }

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
            Directory.CreateDirectory(Path.Combine(targetFolder, "Thing", "Dynamic", "images", "icon"));
            Directory.CreateDirectory(Path.Combine(targetFolder, "Thing", "Dynamic", "images", "parts"));

            ModXmlIO.WriteAllText(Path.Combine(targetFolder, MainXmlRelativePath), dialog.Result.MainXml);
            ModRootAssets.CopyToRoot(dialog.Result.IconSourcePath, targetFolder, ModRootAssets.IconRelativePath);
            ModRootAssets.CopyToRoot(dialog.Result.ScreenshotSourcePath, targetFolder, ModRootAssets.ScreenshotRelativePath);

            var buildingsXmlFolder = Path.Combine(targetFolder, "Thing", "Buildings");
            Directory.CreateDirectory(buildingsXmlFolder);
            ModXmlIO.WriteAllText(Path.Combine(buildingsXmlFolder, "Buildings.xml"), dialog.Result.BuildingsXml);

            var dynamicsXmlFolder = Path.Combine(targetFolder, "Thing", "Dynamic");
            Directory.CreateDirectory(dynamicsXmlFolder);
            ModXmlIO.WriteAllText(Path.Combine(dynamicsXmlFolder, "Dynamics.xml"), dialog.Result.DynamicsXml);

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
                System.Windows.MessageBox.Show(this, string.Join(Environment.NewLine, messages), Loc.Get("Str.Main.ModInfoValidateTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            System.Windows.MessageBox.Show(this, ex.Message, Loc.Get("Str.Main.ModInfoTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UploadWorkshop_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        if (steamSession?.IsAvailable != true)
        {
            System.Windows.MessageBox.Show(
                this,
                Loc.Get("Str.Main.UploadNeedSteam"),
                Loc.Get("Str.Main.UploadWorkshopTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var messages = new List<string>();
        ValidateModMetadata(currentModRoot!, mainXmlText, messages);
        if (messages.Any(message => message.StartsWith("[错误]", StringComparison.Ordinal)))
        {
            Log("上传前校验失败：" + Environment.NewLine + string.Join(Environment.NewLine, messages));
            System.Windows.MessageBox.Show(
                this,
                string.Join(Environment.NewLine, messages),
                Loc.Get("Str.Main.UploadValidateTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dialog = new WorkshopUploadWindow(currentModRoot!, mainXmlText, steamSession.AppId)
        {
            Owner = this
        };
        dialog.ShowDialog();

        if (!string.IsNullOrWhiteSpace(dialog.UpdatedMainXml))
        {
            mainXmlText = dialog.UpdatedMainXml;
            RefreshOverviewList();
            BuildOverviewNavigationTree();
        }

        if (dialog.UploadSucceeded)
        {
            Log($"创意工坊上传成功：PublishedFileId={dialog.ResultPublishedFileId}");
        }
    }

    private void OpenBuildingEditor_Click(object sender, RoutedEventArgs e)
    {
        if (ClassifyCurrentSelection() == TreeSelectionKind.DynamicNode)
        {
            OpenDynamicEditor_Click(sender, e);
            return;
        }

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
            RefreshTileBoardAfterMutation(TreeSelectionKind.BuildingNode);
            SyncBuildingTileSelection(dialog.Result.Id);
            UpdateTopBarState();
            Log($"已更新 Building：{dialog.Result.Id} - {dialog.Result.Name}。");
        }
        catch (Exception ex)
        {
            Log($"打开 Building 编辑失败：{ex.Message}");
        }
    }

    private void OpenDynamicEditor_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen() || !EnsureGameDataReady())
        {
            return;
        }

        try
        {
            var initial = GetEditableDynamicFromSelectionOrDefaults();
            var dialog = new BuildingEditorWindow(initial, currentModRoot!, gameData, isDynamicCategory: true)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            UpsertDynamicToDynamicsXml(dialog.Result);
            SaveDynamicsXmlToDisk();
            ParseDynamicsFromEditor();
            RefreshDynamicNodesInTree();
            RefreshTileBoardAfterMutation(TreeSelectionKind.DynamicNode);
            SyncDynamicTileSelection(dialog.Result.Id);
            UpdateTopBarState();
            Log($"已更新 Dynamic：{dialog.Result.Id} - {dialog.Result.Name}。");
        }
        catch (Exception ex)
        {
            Log($"打开 Dynamic 编辑失败：{ex.Message}");
        }
    }

    private void CleanMeta_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        if (!ConfirmDialog.Show(this, Loc.Get("Str.Main.CleanMeta"), Loc.Get("Str.Main.CleanMetaConfirm")))
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
                ApplyTileBoardForSelection(TreeSelectionKind.ModRoot);
                BuildingTiles.SelectedItem = null;
                break;
            case BuildingTreeTag buildingTag:
                EnsureModActivated(buildingTag.ModRoot);
                ApplyTileBoardForSelection(TreeSelectionKind.BuildingNode);
                SyncBuildingTileSelection(buildingTag.BuildingId);
                break;
            case DynamicTreeTag dynamicTag:
                EnsureModActivated(dynamicTag.ModRoot);
                ApplyTileBoardForSelection(TreeSelectionKind.DynamicNode);
                SyncDynamicTileSelection(dynamicTag.DynamicId);
                break;
            case string path when IsOverviewRootPath(path):
                // 点击顶层 Mod：关闭当前工程并清空右侧 Building 显示（保留树选中，避免重建闪烁）
                CloseCurrentMod(rebuildNavigationTree: false);
                break;
            case string path:
                BuildingTiles.SelectedItem = null;

                if (TryResolveModRootFromPath(path) is { } modRoot)
                {
                    EnsureModActivated(modRoot);
                }

                if (!File.Exists(path) && Directory.Exists(path))
                {
                    // 文件夹节点：按 Buildings / Dynamic 切换右侧列表
                    ApplyTileBoardForSelection(ClassifyTreeItem(item));
                    break;
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
                            ApplyTileBoardForSelection(TreeSelectionKind.BuildingsXml);
                        }
                        else if (relativePath.Equals(DynamicsXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                        {
                            dynamicsXmlText = text;
                            ParseDynamicsFromEditor();
                            ApplyTileBoardForSelection(TreeSelectionKind.DynamicsXml);
                        }
                        else if (relativePath.Equals(MainXmlRelativePath, StringComparison.OrdinalIgnoreCase))
                        {
                            mainXmlText = text;
                            ApplyTileBoardForSelection(TreeSelectionKind.MainXml);
                        }
                        else
                        {
                            buildingsXmlText = text;
                            ApplyTileBoardForSelection(ClassifyTreeItem(item));
                        }

                        Log($"已载入 XML：{relativePath}");
                    }
                    catch (Exception ex)
                    {
                        Log($"载入 XML 失败：{ex.Message}");
                    }
                }
                else
                {
                    ApplyTileBoardForSelection(ClassifyTreeItem(item));
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

        // 点右侧磁贴时，清除左侧树的上次选中痕迹，避免双向残留高亮
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
        if (!ConfirmDialog.Show(this, Loc.Get("Str.Main.DeleteMod"), Loc.Format("Str.Main.DeleteModConfirm", folderName)))
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
            System.Windows.MessageBox.Show(this, ex.Message, Loc.Get("Str.Main.DeleteMod"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteBuilding_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureModOpen())
        {
            return;
        }

        if (ClassifyCurrentSelection() == TreeSelectionKind.DynamicNode)
        {
            DeleteDynamic_Click(sender, e);
            return;
        }

        if (!TryGetSelectedBuildingId(out var selectedId, out var selectedName))
        {
            System.Windows.MessageBox.Show(this, Loc.Get("Str.Main.DeleteBuildingSelectFirst"), Loc.Get("Str.Main.DeleteBuildingTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ConfirmDialog.Show(this, Loc.Get("Str.Main.DeleteBuildingTitle"), Loc.Format("Str.Main.DeleteBuildingConfirm", selectedId, selectedName)))
        {
            return;
        }

        try
        {
            if (!RemoveBuildingFromBuildingsXml(selectedId))
            {
                System.Windows.MessageBox.Show(this, Loc.Format("Str.Main.DeleteBuildingNotFound", selectedId), Loc.Get("Str.Main.DeleteBuildingTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TryDeleteBuildingImageFiles(selectedId);
            SaveBuildingsXmlToDisk();
            BuildingTiles.SelectedItem = null;
            ParseBuildingsFromEditor();
            RefreshBuildingNodesInTree();
            UpdateTopBarState();
            Log($"已删除 Building：id={selectedId} - {selectedName}。");
        }
        catch (Exception ex)
        {
            Log($"删除 Building 失败：{ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, Loc.Get("Str.Main.DeleteBuildingTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteDynamic_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedDynamicId(out var selectedId, out var selectedName))
        {
            System.Windows.MessageBox.Show(this, Loc.Get("Str.Main.DeleteDynamicSelectFirst"), Loc.Get("Str.Main.DeleteDynamicTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ConfirmDialog.Show(this, Loc.Get("Str.Main.DeleteDynamicTitle"), Loc.Format("Str.Main.DeleteDynamicConfirm", selectedId, selectedName)))
        {
            return;
        }

        try
        {
            if (!RemoveDynamicFromDynamicsXml(selectedId))
            {
                System.Windows.MessageBox.Show(this, Loc.Format("Str.Main.DeleteDynamicNotFound", selectedId), Loc.Get("Str.Main.DeleteDynamicTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TryDeleteDynamicImageFiles(selectedId);
            SaveDynamicsXmlToDisk();
            BuildingTiles.SelectedItem = null;
            ParseDynamicsFromEditor();
            RefreshDynamicNodesInTree();
            RefreshTileBoardAfterMutation(TreeSelectionKind.DynamicsXml);
            UpdateTopBarState();
            Log($"已删除 Dynamic：id={selectedId} - {selectedName}。");
        }
        catch (Exception ex)
        {
            Log($"删除 Dynamic 失败：{ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, Loc.Get("Str.Main.DeleteDynamicTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            var preferDynamic = ClassifyCurrentSelection() is TreeSelectionKind.DynamicsXml
                or TreeSelectionKind.DynamicFolder
                or TreeSelectionKind.DynamicNode;
            var (nextId, nextName) = preferDynamic
                ? SuggestNewDynamicDefaults()
                : SuggestNewBuildingDefaults();
            var initial = preferDynamic
                ? new EditableBuilding(
                    nextId,
                    nextName,
                    "FARMING_GOLEM",
                    1,
                    BuildingFieldOptions.DefaultCapbility,
                    PreferFarmingGolemWorkbenchOrDefault(),
                    FarmingGolemOptions.DefaultSimulateId,
                    DynamicFieldOptions.FixedHealth,
                    DynamicFieldOptions.FixedPlaceSizeX,
                    DynamicFieldOptions.FixedPlaceSizeY,
                    DynamicFieldOptions.CreateDefaultMaterials(),
                    DynamicFieldOptions.DefaultBarrier("FARMING_GOLEM"),
                    DynamicFieldOptions.DefaultVisualScale)
                : new EditableBuilding(
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

            var dialog = new BuildingEditorWindow(
                initial,
                currentModRoot!,
                gameData,
                isDynamicCategory: preferDynamic,
                allowCategoryChange: true,
                suggestBuildingDefaults: SuggestNewBuildingDefaults,
                suggestDynamicDefaults: SuggestNewDynamicDefaults,
                preferDynamicWorkbenchId: PreferFarmingGolemWorkbenchOrDefault)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || dialog.Result is null)
            {
                return;
            }

            if (dialog.IsDynamicCategory)
            {
                UpsertDynamicToDynamicsXml(dialog.Result);
                SaveDynamicsXmlToDisk();
                ParseDynamicsFromEditor();
                RefreshDynamicNodesInTree();
                RefreshTileBoardAfterMutation(TreeSelectionKind.DynamicsXml);
                SyncDynamicTileSelection(dialog.Result.Id);
                Log($"已新增 Dynamic：{dialog.Result.Id} - {dialog.Result.Name} ({dialog.Result.Type})。");
            }
            else
            {
                UpsertBuildingToBuildingsXml(dialog.Result);
                SaveBuildingsXmlToDisk();
                ParseBuildingsFromEditor();
                RefreshBuildingNodesInTree();
                RefreshTileBoardAfterMutation(TreeSelectionKind.BuildingsXml);
                SyncBuildingTileSelection(dialog.Result.Id);
                Log($"已新增 Building：{dialog.Result.Id} - {dialog.Result.Name} ({dialog.Result.Type})。");
            }

            UpdateTopBarState();
        }
        catch (Exception ex)
        {
            Log($"新增组件失败：{ex.Message}");
        }
    }

    private void AddDynamic_Click(object sender, RoutedEventArgs e)
        => AddBuilding_Click(sender, e);

    private bool TryGetSelectedBuildingId(out int id, out string name)
    {
        if (BuildingTiles.SelectedItem is BuildingEntry tile && !IsDynamicEntry(tile))
        {
            id = tile.Id;
            name = tile.Name;
            return true;
        }

        if (ModFileTree.SelectedItem is TreeViewItem { Tag: BuildingTreeTag tag })
        {
            id = tag.BuildingId;
            var buildingId = id;
            name = buildings.FirstOrDefault(b => b.Id == buildingId)?.Name ?? $"#{buildingId}";
            return true;
        }

        id = 0;
        name = string.Empty;
        return false;
    }

    private bool TryGetSelectedDynamicId(out int id, out string name)
    {
        if (BuildingTiles.SelectedItem is BuildingEntry tile &&
            (IsShowingDynamicsTiles || IsDynamicEntry(tile)))
        {
            id = tile.Id;
            name = tile.Name;
            return true;
        }

        if (ModFileTree.SelectedItem is TreeViewItem { Tag: DynamicTreeTag tag })
        {
            id = tag.DynamicId;
            var dynamicId = id;
            name = dynamics.FirstOrDefault(d => d.Id == dynamicId)?.Name ?? $"#{dynamicId}";
            return true;
        }

        id = 0;
        name = string.Empty;
        return false;
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
              <supportVersion>1</supportVersion>
              <category>Building</category>
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

        dynamicsXmlText = ReadTextOrTemplate(
            DynamicsXmlRelativePath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfModDynamicXML xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            </ArrayOfModDynamicXML>
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

    private void ReloadDynamicsXmlFromDisk()
    {
        if (!EnsureModOpen(showMessage: false))
        {
            dynamicsXmlText = string.Empty;
            return;
        }

        dynamicsXmlText = ReadTextOrTemplate(
            DynamicsXmlRelativePath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfModDynamicXML xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            </ArrayOfModDynamicXML>
            """);
    }

    private void SaveDynamicsXmlToDisk()
    {
        if (!EnsureModOpen())
        {
            return;
        }

        var messages = new List<string>();
        ValidateDynamicsXml(messages);
        if (messages.Any(message => message.StartsWith("[错误]", StringComparison.Ordinal)))
        {
            Log("Dynamics.xml 写盘前校验发现问题：" + Environment.NewLine + string.Join(Environment.NewLine, messages));
        }

        var path = GetFullPath(DynamicsXmlRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ModXmlIO.WriteAllText(path, dynamicsXmlText);
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
                ValidateSimulateId(element, id, typeValue, messages);
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

    private void ValidateSimulateId(XElement parent, int buildingId, string typeValue, List<string> messages)
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == "simulateId")?.Value;
        if (!int.TryParse(value, out var simulateId) || simulateId <= 0)
        {
            messages.Add($"[错误] 建筑 {buildingId} 的 building.simulateId 需要是正整数。");
            return;
        }

        if (DynamicFieldOptions.IsFarmingGolem(typeValue))
        {
            if (!FarmingGolemOptions.IsKnownSimulateId(simulateId))
            {
                messages.Add($"[错误] 条目 {buildingId} 的 simulateId 不是有效农业傀儡角色（20191/20192/20193）：{simulateId}。");
            }

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

        if (tileBoardMode == TileBoardMode.Combined)
        {
            RebuildCombinedTiles();
        }
    }

    private void ParseDynamicsFromEditor()
    {
        dynamics.Clear();

        try
        {
            var document = XDocument.Parse(dynamicsXmlText);
            if (document.Root?.Name.LocalName != "ArrayOfModDynamicXML")
            {
                if (tileBoardMode == TileBoardMode.Combined)
                {
                    RebuildCombinedTiles();
                }

                return;
            }

            foreach (var element in document.Root.Elements().Where(e => e.Name.LocalName == "ModDynamicXML"))
            {
                var idText = element.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
                if (!int.TryParse(idText, out var id))
                {
                    continue;
                }

                var nameRaw = element.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value;
                var typeRaw = element.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value;
                var nameStored = nameRaw ?? string.Empty;
                var typeStored = typeRaw ?? string.Empty;
                var iconPath = EnsureModOpen(showMessage: false)
                    ? GetFullPath(Path.Combine(DynamicIconsRelativePath, $"{id}.png"))
                    : string.Empty;
                dynamics.Add(new BuildingEntry(id, nameStored, typeStored, "Dynamic", iconPath));
            }
        }
        catch
        {
            // External editing may leave the XML temporarily invalid; validation reports the details.
        }

        if (tileBoardMode == TileBoardMode.Combined)
        {
            RebuildCombinedTiles();
        }
    }

    private void ValidateDynamicsXml(List<string> messages)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(dynamicsXmlText, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            messages.Add($"[错误] Dynamics.xml 不是合法 XML：{ex.Message}");
            return;
        }

        if (document.Root?.Name.LocalName != "ArrayOfModDynamicXML")
        {
            messages.Add($"[错误] Dynamics.xml 根节点应为 <ArrayOfModDynamicXML>，当前为 <{document.Root?.Name.LocalName ?? "空"}>。");
            return;
        }

        var seenIds = new HashSet<int>();
        foreach (var element in document.Root.Elements().Where(e => e.Name.LocalName == "ModDynamicXML"))
        {
            var idText = element.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
            if (!int.TryParse(idText, out var id))
            {
                messages.Add("[错误] 存在缺少有效 <id> 的 ModDynamicXML。");
                continue;
            }

            if (!seenIds.Add(id))
            {
                messages.Add($"[错误] Dynamic id 重复：{id}。");
            }

            ValidatePositiveInt(element, "direction", id, messages);
            var typeValue = element.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value ?? string.Empty;
            ValidatePositiveInt(element, "workbenchId", id, messages);
            if (DynamicFieldOptions.RequiresSimulateId(typeValue))
            {
                ValidateSimulateId(element, id, typeValue, messages);
            }

            if (DynamicFieldOptions.IsFarmingGolem(typeValue) && currentModRoot is not null)
            {
                var sheetError = FarmingGolemPartsSheet.ValidateExportedAssets(currentModRoot, id);
                if (sheetError is not null)
                {
                    messages.Add($"[错误] Dynamic {id}：{sheetError}");
                }
            }

            ValidatePositiveInt(element, "health", id, messages);
            ValidateOptionalBool(element, "barrier", id, messages);

            var size = element.Elements().FirstOrDefault(e => e.Name.LocalName == "size");
            if (size is null)
            {
                messages.Add($"[错误] Dynamic {id} 缺少 <size>。");
            }
            else
            {
                ValidatePositiveInt(size, "x", id, messages, "size");
                ValidatePositiveInt(size, "y", id, messages, "size");
            }

            if (DynamicFieldOptions.ShowsVisualScale(typeValue))
            {
                var scaleText = element.Elements().FirstOrDefault(e => e.Name.LocalName == "scale")?.Value;
                var scale = DynamicFieldOptions.DefaultVisualScale;
                if (!string.IsNullOrWhiteSpace(scaleText))
                {
                    if (!double.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) ||
                        scale < DynamicFieldOptions.MinVisualScale ||
                        scale > DynamicFieldOptions.MaxVisualScale)
                    {
                        messages.Add(
                            $"[错误] Dynamic {id} 的 scale 须在 {DynamicFieldOptions.MinVisualScale}–{DynamicFieldOptions.MaxVisualScale} 之间。");
                    }
                    else if (size is not null && DynamicFieldOptions.UsesScaledPlaceSize(typeValue))
                    {
                        var expected = DynamicFieldOptions.ComputeScaledPlaceSize(scale);
                        var sxOk = int.TryParse(size.Elements().FirstOrDefault(e => e.Name.LocalName == "x")?.Value, out var sx);
                        var syOk = int.TryParse(size.Elements().FirstOrDefault(e => e.Name.LocalName == "y")?.Value, out var sy);
                        if (sxOk && sx != expected.X)
                        {
                            messages.Add(
                                $"[错误] Dynamic {id} 的 size.x 应按原版占地×scale 为 {expected.X}，当前为 {sx}。");
                        }

                        if (syOk && sy != expected.Y)
                        {
                            messages.Add(
                                $"[错误] Dynamic {id} 的 size.y 应按原版占地×scale 为 {expected.Y}，当前为 {sy}。");
                        }
                    }
                }
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

        messages.Add($"[信息] 识别到 {seenIds.Count} 个 Dynamic 条目。");
    }

    private void RefreshDynamicNodesInTree()
    {
        if (!EnsureModOpen(showMessage: false))
        {
            return;
        }

        var dynamicsXmlPath = GetFullPath(DynamicsXmlRelativePath);
        if (TryFindTreeViewItemByTag(ModFileTree, dynamicsXmlPath) is not TreeViewItem dynamicsXmlNode)
        {
            BuildOverviewNavigationTree();
            return;
        }

        dynamicsXmlNode.Items.Clear();
        foreach (var d in dynamics.OrderBy(d => d.Id))
        {
            dynamicsXmlNode.Items.Add(new TreeViewItem
            {
                Header = d.DisplayName,
                Tag = new DynamicTreeTag(currentModRoot!, d.Id),
                IsExpanded = false
            });
        }
    }

    private static BitmapImage? LoadBitmapIfExists(string path)
        => ImagePreviewLoader.Load(path);

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

        if (left is DynamicTreeTag leftDynamic && right is DynamicTreeTag rightDynamic)
        {
            return leftDynamic.DynamicId == rightDynamic.DynamicId &&
                   string.Equals(leftDynamic.ModRoot, rightDynamic.ModRoot, StringComparison.OrdinalIgnoreCase);
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
            case DynamicTreeTag dynamicTag:
                key = $"dynamic:{dynamicTag.ModRoot}:{dynamicTag.DynamicId}";
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
        var source = BuildingTiles.ItemsSource as IEnumerable<BuildingEntry> ?? buildings;
        var match = source.FirstOrDefault(b => b.Id == buildingId && !IsDynamicEntry(b));
        if (match is not null)
        {
            BuildingTiles.SelectedItem = match;
            BuildingTiles.ScrollIntoView(match);
        }
    }

    private void SyncDynamicTileSelection(int dynamicId)
    {
        var source = BuildingTiles.ItemsSource as IEnumerable<BuildingEntry> ?? dynamics;
        var match = source.FirstOrDefault(d => d.Id == dynamicId && IsDynamicEntry(d));
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

    private static IReadOnlyList<BuildingEntry> ReadDynamicEntriesFromMod(string modRoot)
    {
        TryMigrateRootDynamicFolderIntoThing(modRoot);

        var entries = new List<BuildingEntry>();
        var path = Path.Combine(modRoot, DynamicsXmlRelativePath);
        if (!File.Exists(path))
        {
            return entries;
        }

        try
        {
            var document = XDocument.Parse(ModXmlIO.ReadAllText(path));
            if (document.Root?.Name.LocalName != "ArrayOfModDynamicXML")
            {
                return entries;
            }

            foreach (var element in document.Root.Elements().Where(e => e.Name.LocalName == "ModDynamicXML"))
            {
                var idText = element.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
                if (!int.TryParse(idText, out var id))
                {
                    continue;
                }

                var nameRaw = element.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value;
                var typeRaw = element.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value;
                var nameStored = nameRaw ?? string.Empty;
                var typeStored = typeRaw ?? string.Empty;
                var iconPath = Path.Combine(modRoot, DynamicIconsRelativePath, $"{id}.png");
                entries.Add(new BuildingEntry(id, nameStored, typeStored, "Dynamic", iconPath));
            }
        }
        catch
        {
            // ignore
        }

        return entries;
    }

    private static void AppendModExplorerChildren(
        TreeViewItem modNode,
        string modRoot,
        IReadOnlyList<BuildingEntry> buildingEntries,
        IReadOnlyList<BuildingEntry> dynamicEntries)
    {
        TryMigrateRootDynamicFolderIntoThing(modRoot);

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
        var buildingsFolderPath = Path.Combine(modRoot, "Thing", "Buildings");
        var dynamicFolderPath = Path.Combine(modRoot, DynamicFolderRelativePath);
        var buildingsXmlPath = Path.Combine(modRoot, BuildingsXmlRelativePath);
        var dynamicsXmlPath = Path.Combine(modRoot, DynamicsXmlRelativePath);
        if (!Directory.Exists(thingPath) && !File.Exists(buildingsXmlPath) && !File.Exists(dynamicsXmlPath))
        {
            return;
        }

        var thingNode = new TreeViewItem
        {
            Header = "Thing",
            Tag = thingPath,
            IsExpanded = true
        };

        var buildingsFolderNode = new TreeViewItem
        {
            Header = "Buildings",
            Tag = buildingsFolderPath,
            IsExpanded = true
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

        buildingsFolderNode.Items.Add(buildingsXmlNode);

        var dynamicFolderNode = new TreeViewItem
        {
            Header = "Dynamic",
            Tag = dynamicFolderPath,
            IsExpanded = true
        };

        var dynamicsXmlNode = new TreeViewItem
        {
            Header = File.Exists(dynamicsXmlPath) ? "Dynamics.xml" : "Dynamics.xml（未创建）",
            Tag = dynamicsXmlPath,
            IsExpanded = false
        };

        foreach (var d in dynamicEntries.OrderBy(d => d.Id))
        {
            dynamicsXmlNode.Items.Add(new TreeViewItem
            {
                Header = d.DisplayName,
                Tag = new DynamicTreeTag(modRoot, d.Id),
                IsExpanded = false
            });
        }

        dynamicFolderNode.Items.Add(dynamicsXmlNode);

        thingNode.Items.Add(buildingsFolderNode);
        thingNode.Items.Add(dynamicFolderNode);
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
            Tag = Path.GetFullPath(modsRoot),
            IsExpanded = true
        };

        foreach (var entry in OrderedOverviewMods())
        {
            var modNode = CreateOverviewModTreeItem(entry, isExpanded: false);
            var buildingEntries = ReadBuildingEntriesFromMod(entry.FullPath);
            var dynamicEntries = ReadDynamicEntriesFromMod(entry.FullPath);
            AppendModExplorerChildren(modNode, entry.FullPath, buildingEntries, dynamicEntries);
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
            Log(Loc.Get("Str.Main.OpenModFirst"));
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
        bool Barrier,
        double Scale = 1.0);

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
            new XElement("barrier",
                (BuildingFieldOptions.IsCarpet(b.Type) ? false : b.Barrier) ? "true" : "false"));

    private static XElement CreateModDynamicXmlElement(EditableBuilding b) =>
        new(
            "ModDynamicXML",
            new XElement("id", b.Id),
            new XElement("name", b.Name),
            new XElement("type", b.Type),
            new XElement("direction", b.Direction),
            DynamicFieldOptions.ShowsCapbility(b.Type)
                ? new XElement("capbility", b.Capbility)
                : null,
            new XElement("workbenchId", b.WorkbenchId),
            DynamicFieldOptions.RequiresSimulateId(b.Type) && b.SimulateId > 0
                ? new XElement("simulateId", b.SimulateId)
                : null,
            new XElement("health", b.Health),
            new XElement(
                "size",
                new XElement("x", b.SizeX),
                new XElement("y", b.SizeY)),
            DynamicFieldOptions.ShowsVisualScale(b.Type)
                ? new XElement("scale", b.Scale.ToString("0.###", CultureInfo.InvariantCulture))
                : null,
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

    private (int nextId, string nextName) SuggestNewDynamicDefaults()
    {
        var usedIds = dynamics.Select(d => d.Id).ToHashSet();
        var nextId = 1;
        while (usedIds.Contains(nextId))
        {
            nextId++;
        }

        return (nextId, $"NewDynamic_{nextId}");
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
            => HasMainXml ? FolderName : $"{FolderName}（未创建 main.xml）";
    }

    private EditableBuilding GetEditableBuildingFromSelectionOrDefaults()
    {
        if (BuildingTiles.SelectedItem is BuildingEntry selected && !IsDynamicEntry(selected))
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

        if (ModFileTree.SelectedItem is TreeViewItem { Tag: BuildingTreeTag buildingTag })
        {
            var existing = TryReadEditableBuildingById(buildingTag.BuildingId);
            if (existing is not null)
            {
                return existing;
            }
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

            var rawSimulateId = ReadInt("simulateId", 0);
            var simulateId = NormalizeSimulateId(typeValue, rawSimulateId);

            return new EditableBuilding(
                id,
                nameValue,
                typeValue,
                ReadInt("direction", 1),
                NormalizeCapbility(ReadInt("capbility", BuildingFieldOptions.DefaultCapbility)),
                NormalizeWorkbenchId(ReadInt("workbenchId", gameData.DefaultWorkbenchId)),
                simulateId,
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

    private void TryDeleteDynamicImageFiles(int id)
    {
        foreach (var rel in new[]
                 {
                     Path.Combine(DynamicImagesRelativePath, $"{id}.png"),
                     Path.Combine(DynamicIconsRelativePath, $"{id}.png")
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
                // ignore
            }
        }

        if (!string.IsNullOrWhiteSpace(currentModRoot))
        {
            FarmingGolemPartsSheet.TryDeleteAssets(currentModRoot, id);
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

    private void UpsertDynamicToDynamicsXml(EditableBuilding b)
    {
        EnsureDynamicsXmlLoaded();
        var doc = XDocument.Parse(dynamicsXmlText, LoadOptions.PreserveWhitespace);
        if (doc.Root?.Name.LocalName != "ArrayOfModDynamicXML")
        {
            throw new InvalidOperationException("Dynamics.xml 根节点不是 ArrayOfModDynamicXML。");
        }

        var existing = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "ModDynamicXML" &&
                                                               int.TryParse(e.Elements().FirstOrDefault(x => x.Name.LocalName == "id")?.Value, out var id) &&
                                                               id == b.Id);

        var element = CreateModDynamicXmlElement(b);
        if (existing is null)
        {
            doc.Root.Add(element);
        }
        else
        {
            existing.ReplaceWith(element);
        }

        dynamicsXmlText = ModXmlFormatter.Serialize(doc);
        Directory.CreateDirectory(GetFullPath(DynamicImagesRelativePath));
        Directory.CreateDirectory(GetFullPath(DynamicIconsRelativePath));
    }

    private bool RemoveDynamicFromDynamicsXml(int id)
    {
        EnsureDynamicsXmlLoaded();
        var doc = XDocument.Parse(dynamicsXmlText, LoadOptions.PreserveWhitespace);
        if (doc.Root?.Name.LocalName != "ArrayOfModDynamicXML")
        {
            throw new InvalidOperationException("Dynamics.xml 根节点不是 ArrayOfModDynamicXML。");
        }

        var existing = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "ModDynamicXML" &&
                                                               int.TryParse(e.Elements().FirstOrDefault(x => x.Name.LocalName == "id")?.Value, out var elId) &&
                                                               elId == id);
        if (existing is null)
        {
            return false;
        }

        existing.Remove();
        dynamicsXmlText = ModXmlFormatter.Serialize(doc);
        return true;
    }

    private void EnsureDynamicsXmlLoaded()
    {
        if (!string.IsNullOrWhiteSpace(dynamicsXmlText))
        {
            return;
        }

        ReloadDynamicsXmlFromDisk();
    }

    private EditableBuilding GetEditableDynamicFromSelectionOrDefaults()
    {
        if (BuildingTiles.SelectedItem is BuildingEntry selected &&
            (IsShowingDynamicsTiles || IsDynamicEntry(selected)))
        {
            var fromTile = TryReadEditableDynamicById(selected.Id);
            if (fromTile is not null)
            {
                return fromTile;
            }
        }

        if (ModFileTree.SelectedItem is TreeViewItem { Tag: DynamicTreeTag tag })
        {
            var existing = TryReadEditableDynamicById(tag.DynamicId);
            if (existing is not null)
            {
                return existing;
            }

            var entry = dynamics.FirstOrDefault(d => d.Id == tag.DynamicId);
            var nameForEdit = entry is null || string.IsNullOrWhiteSpace(entry.Name)
                ? $"Dynamic_{tag.DynamicId}"
                : entry.Name;
            return new EditableBuilding(
                tag.DynamicId,
                nameForEdit,
                "FARMING_GOLEM",
                1,
                BuildingFieldOptions.DefaultCapbility,
                PreferFarmingGolemWorkbenchOrDefault(),
                FarmingGolemOptions.DefaultSimulateId,
                DynamicFieldOptions.FixedHealth,
                DynamicFieldOptions.FixedPlaceSizeX,
                DynamicFieldOptions.FixedPlaceSizeY,
                DynamicFieldOptions.CreateDefaultMaterials(),
                DynamicFieldOptions.DefaultBarrier("FARMING_GOLEM"),
                DynamicFieldOptions.DefaultVisualScale);
        }

        var (nextId, nextName) = SuggestNewDynamicDefaults();
        return new EditableBuilding(
            nextId,
            nextName,
            "FARMING_GOLEM",
            1,
            BuildingFieldOptions.DefaultCapbility,
            PreferFarmingGolemWorkbenchOrDefault(),
            FarmingGolemOptions.DefaultSimulateId,
            DynamicFieldOptions.FixedHealth,
            DynamicFieldOptions.FixedPlaceSizeX,
            DynamicFieldOptions.FixedPlaceSizeY,
            DynamicFieldOptions.CreateDefaultMaterials(),
            DynamicFieldOptions.DefaultBarrier("FARMING_GOLEM"),
            DynamicFieldOptions.DefaultVisualScale);
    }

    private EditableBuilding? TryReadEditableDynamicById(int id)
    {
        try
        {
            EnsureDynamicsXmlLoaded();
            var doc = XDocument.Parse(dynamicsXmlText);
            if (doc.Root?.Name.LocalName != "ArrayOfModDynamicXML")
            {
                return null;
            }

            var el = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "ModDynamicXML" &&
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

            var nameValue = el.Elements().FirstOrDefault(x => x.Name.LocalName == "name")?.Value ?? $"Dynamic_{id}";
            var typeValue = el.Elements().FirstOrDefault(x => x.Name.LocalName == "type")?.Value ?? "FARMING_GOLEM";
            var size = el.Elements().FirstOrDefault(x => x.Name.LocalName == "size");
            var sx = 1;
            var sy = 1;
            if (size is not null)
            {
                sx = int.TryParse(size.Elements().FirstOrDefault(x => x.Name.LocalName == "x")?.Value, out var vx) ? vx : 1;
                sy = int.TryParse(size.Elements().FirstOrDefault(x => x.Name.LocalName == "y")?.Value, out var vy) ? vy : 1;
            }

            var simulateId = NormalizeSimulateId(typeValue, ReadInt("simulateId", 0));
            var workbenchId = NormalizeWorkbenchId(ReadInt("workbenchId", gameData.DefaultWorkbenchId));
            var scaleText = el.Elements().FirstOrDefault(x => x.Name.LocalName == "scale")?.Value;
            var scale = DynamicFieldOptions.DefaultVisualScale;
            if (!string.IsNullOrWhiteSpace(scaleText) &&
                double.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale))
            {
                scale = ClampVisualScale(parsedScale);
            }

            if (DynamicFieldOptions.UsesScaledPlaceSize(typeValue))
            {
                (sx, sy) = DynamicFieldOptions.ComputeScaledPlaceSize(scale);
            }

            return new EditableBuilding(
                id,
                nameValue,
                typeValue,
                ReadInt("direction", 1),
                BuildingFieldOptions.DefaultCapbility,
                workbenchId,
                simulateId,
                DynamicFieldOptions.FixedHealth,
                sx,
                sy,
                ReadMaterials(el),
                ReadBool("barrier", DynamicFieldOptions.DefaultBarrier(typeValue)),
                scale);
        }
        catch
        {
            return null;
        }
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