using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using WpfBinding = System.Windows.Data.Binding;
using WpfUpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace XenoHavenModToolkit;

public partial class BuildingEditorWindow : Window
{
    private const int MaxMaterialCount = 200;

    private static readonly string BuildingImagesRelativePath = Path.Combine("Thing", "Buildings", "images");
    private static readonly string BuildingIconsRelativePath = Path.Combine("Thing", "Buildings", "images", "icon");
    private static readonly string DynamicImagesRelativePath = Path.Combine("Thing", "Dynamic", "images");
    private static readonly string DynamicIconsRelativePath = Path.Combine("Thing", "Dynamic", "images", "icon");

    private readonly string modRoot;
    private int buildingId;
    private readonly GameDataCatalog gameData;
    private readonly bool allowCategoryChange;
    private readonly Func<(int id, string name)>? suggestBuildingDefaults;
    private readonly Func<(int id, string name)>? suggestDynamicDefaults;
    private readonly Func<int>? preferDynamicWorkbenchId;
    private bool isDynamicCategory;
    private bool suppressCategoryChanged;
    private readonly ObservableCollection<MainWindow.CraftMaterial> materials;
    private readonly List<LabeledIdOption> materialOptions;

    private string ImagesRelativePath => isDynamicCategory ? DynamicImagesRelativePath : BuildingImagesRelativePath;
    private string IconsRelativePath => isDynamicCategory ? DynamicIconsRelativePath : BuildingIconsRelativePath;

    internal MainWindow.EditableBuilding? Result { get; private set; }

    internal bool IsDynamicCategory => isDynamicCategory;

    internal BuildingEditorWindow(MainWindow.EditableBuilding initial, string modRoot, GameDataCatalog gameData)
        : this(initial, modRoot, gameData, isDynamicCategory: false)
    {
    }

    internal BuildingEditorWindow(
        MainWindow.EditableBuilding initial,
        string modRoot,
        GameDataCatalog gameData,
        bool isDynamicCategory)
        : this(initial, modRoot, gameData, isDynamicCategory, allowCategoryChange: false)
    {
    }

    internal BuildingEditorWindow(
        MainWindow.EditableBuilding initial,
        string modRoot,
        GameDataCatalog gameData,
        bool isDynamicCategory,
        bool allowCategoryChange,
        Func<(int id, string name)>? suggestBuildingDefaults = null,
        Func<(int id, string name)>? suggestDynamicDefaults = null,
        Func<int>? preferDynamicWorkbenchId = null)
    {
        InitializeComponent();
        this.modRoot = modRoot;
        this.gameData = gameData;
        this.allowCategoryChange = allowCategoryChange;
        this.suggestBuildingDefaults = suggestBuildingDefaults;
        this.suggestDynamicDefaults = suggestDynamicDefaults;
        this.preferDynamicWorkbenchId = preferDynamicWorkbenchId;
        this.isDynamicCategory = isDynamicCategory;
        buildingId = initial.Id;
        materialOptions = BuildMaterialOptions(initial.Materials);
        materials = new ObservableCollection<MainWindow.CraftMaterial>(
            initial.Materials.Select(m => new MainWindow.CraftMaterial(m.Id, m.Count)));
        IdBox.Text = buildingId.ToString(CultureInfo.InvariantCulture);
        NameBox.Text = initial.Name;
        FieldPicker.InitializeWorkbenches(gameData);
        FieldPicker.InitializeProductionLines(gameData);
        ApplyCategoryMode(isDynamicCategory, resetIdentity: false, applyFieldDefaults: false);
        FieldPicker.SetValues(initial.Type, initial.Direction, initial.WorkbenchId, initial.SimulateId);
        FieldPicker.TypeChanged += (_, _) =>
        {
            ApplyCapbilityVisibility();
            ApplyBarrierVisibility();
            ApplyWorldImageUiForType();
            ApplyFarmingGolemLayoutUi();
            BarrierToggle.IsChecked = isDynamicCategory
                ? DynamicFieldOptions.DefaultBarrier(FieldPicker.SelectedType)
                : BuildingFieldOptions.DefaultBarrier(FieldPicker.SelectedType);
        };
        CapbilityBox.Text = ClampCapbility(initial.Capbility).ToString(CultureInfo.InvariantCulture);
        ApplyCapbilityVisibility();
        ApplyBarrierVisibility();
        ApplyWorldImageUiForType();
        BarrierToggle.IsChecked = initial.Barrier;
        HealthBox.Text = (isDynamicCategory ? DynamicFieldOptions.FixedHealth : BuildingFieldOptions.FixedHealth)
            .ToString(CultureInfo.InvariantCulture);
        SizeXBox.Text = initial.SizeX.ToString(CultureInfo.InvariantCulture);
        SizeYBox.Text = initial.SizeY.ToString(CultureInfo.InvariantCulture);
        ScaleBox.Text = initial.Scale.ToString("0.###", CultureInfo.InvariantCulture);
        ScaleBox.TextChanged += (_, _) => RefreshScaledPlaceSizeFromScaleBox();
        ApplyFarmingGolemLayoutUi();
        ConfigureMaterialsGrid();
        MaterialsGrid.ItemsSource = materials;
        RefreshMaterialsEmptyState();
        RefreshImagePreview();
        SetupCategorySelector(isDynamicCategory);
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void SetupCategorySelector(bool selectedDynamic)
    {
        if (!allowCategoryChange)
        {
            CategoryLabel.Visibility = Visibility.Collapsed;
            CategoryCombo.Visibility = Visibility.Collapsed;
            return;
        }

        CategoryLabel.Visibility = Visibility.Visible;
        CategoryCombo.Visibility = Visibility.Visible;
        Title = Loc.Get("Str.AddBuilding.Title");

        suppressCategoryChanged = true;
        CategoryCombo.Items.Clear();
        CategoryCombo.Items.Add(Loc.Get("Str.BuildingEditor.CategoryBuildings"));
        CategoryCombo.Items.Add(Loc.Get("Str.BuildingEditor.CategoryDynamic"));
        CategoryCombo.SelectedIndex = selectedDynamic ? 1 : 0;
        suppressCategoryChanged = false;
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressCategoryChanged || !allowCategoryChange)
        {
            return;
        }

        var wantDynamic = CategoryCombo.SelectedIndex == 1;
        if (wantDynamic == isDynamicCategory)
        {
            return;
        }

        ApplyCategoryMode(wantDynamic, resetIdentity: true, applyFieldDefaults: true);
    }

    private void ApplyCategoryMode(bool dynamic, bool resetIdentity, bool applyFieldDefaults)
    {
        isDynamicCategory = dynamic;
        if (dynamic)
        {
            FieldPicker.UseDynamicFarmingGolemMode();
            if (!allowCategoryChange)
            {
                Title = Loc.Get("Str.DynamicEditor.Title");
            }
        }
        else
        {
            FieldPicker.UseBuildingMode();
            if (!allowCategoryChange)
            {
                Title = Loc.Get("Str.BuildingEditor.Title");
            }
        }

        if (resetIdentity)
        {
            var suggest = dynamic ? suggestDynamicDefaults : suggestBuildingDefaults;
            if (suggest is not null)
            {
                var (nextId, nextName) = suggest();
                buildingId = nextId;
                IdBox.Text = buildingId.ToString(CultureInfo.InvariantCulture);
                NameBox.Text = nextName;
            }
        }

        if (applyFieldDefaults)
        {
            materials.Clear();
            if (dynamic)
            {
                foreach (var m in DynamicFieldOptions.CreateDefaultMaterials())
                {
                    materials.Add(m);
                }

                var workbenchId = preferDynamicWorkbenchId?.Invoke()
                    ?? gameData.DefaultWorkbenchId;
                FieldPicker.SetValues(
                    "FARMING_GOLEM",
                    1,
                    workbenchId,
                    FarmingGolemOptions.DefaultSimulateId);
                CapbilityBox.Text = BuildingFieldOptions.DefaultCapbility.ToString(CultureInfo.InvariantCulture);
                HealthBox.Text = DynamicFieldOptions.FixedHealth.ToString(CultureInfo.InvariantCulture);
                ScaleBox.Text = DynamicFieldOptions.DefaultVisualScale.ToString("0.###", CultureInfo.InvariantCulture);
                BarrierToggle.IsChecked = DynamicFieldOptions.DefaultBarrier("FARMING_GOLEM");
                RefreshScaledPlaceSizeFromScaleBox();
            }
            else
            {
                FieldPicker.SetValues(
                    "BOX",
                    1,
                    gameData.DefaultWorkbenchId,
                    0);
                CapbilityBox.Text = BuildingFieldOptions.DefaultCapbility.ToString(CultureInfo.InvariantCulture);
                HealthBox.Text = BuildingFieldOptions.FixedHealth.ToString(CultureInfo.InvariantCulture);
                SizeXBox.Text = "1";
                SizeYBox.Text = "1";
                ScaleBox.Text = DynamicFieldOptions.DefaultVisualScale.ToString("0.###", CultureInfo.InvariantCulture);
                BarrierToggle.IsChecked = BuildingFieldOptions.DefaultBarrier("BOX");
            }

            RefreshMaterialsEmptyState();
            ApplyCapbilityVisibility();
            ApplyBarrierVisibility();
            ApplyWorldImageUiForType();
            ApplyFarmingGolemLayoutUi();
            RefreshImagePreview();
        }
    }

    private void ConfigureMaterialsGrid()
    {
        var comboBoxStyle = (Style)FindResource("Theme.ComboBox");
        var materialColumn = new DataGridComboBoxColumn
        {
            Header = "材料",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ItemsSource = materialOptions,
            DisplayMemberPath = nameof(LabeledIdOption.Display),
            SelectedValuePath = nameof(LabeledIdOption.Id),
            SelectedValueBinding = new WpfBinding(nameof(MainWindow.CraftMaterial.Id))
            {
                UpdateSourceTrigger = WpfUpdateSourceTrigger.PropertyChanged
            },
            ElementStyle = comboBoxStyle,
            EditingElementStyle = comboBoxStyle
        };

        var countColumn = new DataGridTextColumn
        {
            Header = "数量",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new WpfBinding(nameof(MainWindow.CraftMaterial.Count))
            {
                UpdateSourceTrigger = WpfUpdateSourceTrigger.PropertyChanged
            },
            ElementStyle = (Style)FindResource("MaterialsGridTextStyle"),
            EditingElementStyle = (Style)FindResource("MaterialsGridEditingTextBoxStyle")
        };

        MaterialsGrid.Columns.Clear();
        MaterialsGrid.Columns.Add(materialColumn);
        MaterialsGrid.Columns.Add(countColumn);
    }

    private List<LabeledIdOption> BuildMaterialOptions(IReadOnlyList<MainWindow.CraftMaterial> existing)
    {
        var options = gameData.Materials.ToList();
        foreach (var material in existing)
        {
            if (options.All(option => option.Id != material.Id))
            {
                options.Insert(0, new LabeledIdOption($"未知({material.Id})", material.Id, isKnown: false));
            }
        }

        return options;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(this, Loc.Get("Str.Validate.NameRequired"), Loc.Get("Str.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!FieldPicker.TryValidate(out var fieldError))
        {
            System.Windows.MessageBox.Show(this, fieldError, Loc.Get("Str.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = FieldPicker.SelectedType;
        var direction = FieldPicker.EffectiveDirection;
        var workbenchId = FieldPicker.SelectedWorkbenchId;
        var simulateId = FieldPicker.SelectedSimulateId;
        var capbilityVisible = isDynamicCategory
            ? DynamicFieldOptions.ShowsCapbility(type)
            : BuildingFieldOptions.ShowsCapbility(type);

        var capbility = BuildingFieldOptions.DefaultCapbility;
        if (capbilityVisible && !TryReadCapbility(CapbilityBox.Text, out capbility, "capbility"))
        {
            return;
        }

        int sx;
        int sy;
        var scale = DynamicFieldOptions.DefaultVisualScale;
        if (isDynamicCategory && DynamicFieldOptions.ShowsVisualScale(type))
        {
            if (!TryReadVisualScale(ScaleBox.Text, out scale))
            {
                return;
            }
        }

        if (isDynamicCategory && DynamicFieldOptions.UsesScaledPlaceSize(type))
        {
            (sx, sy) = DynamicFieldOptions.ComputeScaledPlaceSize(scale);
        }
        else if (!TryReadPositiveInt(SizeXBox.Text, out sx, "size.x") ||
                 !TryReadPositiveInt(SizeYBox.Text, out sy, "size.y"))
        {
            return;
        }

        if (!TryReadMaterials(out var materialSnapshot))
        {
            return;
        }

        if (isDynamicCategory && DynamicFieldOptions.IsFarmingGolem(type))
        {
            var sheetError = FarmingGolemPartsSheet.ValidateExportedAssets(modRoot, buildingId);
            if (sheetError is not null)
            {
                System.Windows.MessageBox.Show(this, sheetError, Loc.Get("Str.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var health = isDynamicCategory ? DynamicFieldOptions.FixedHealth : BuildingFieldOptions.FixedHealth;
        var barrier = isDynamicCategory || BuildingFieldOptions.ShowsBarrier(type)
            ? BarrierToggle.IsChecked == true
            : false;
        Result = new MainWindow.EditableBuilding(
            buildingId, name, type, direction, capbility, workbenchId, simulateId, health, sx, sy, materialSnapshot,
            barrier, scale);
        DialogResult = true;
    }

    private void ApplyCapbilityVisibility()
    {
        var shows = isDynamicCategory
            ? DynamicFieldOptions.ShowsCapbility(FieldPicker.SelectedType)
            : BuildingFieldOptions.ShowsCapbility(FieldPicker.SelectedType);
        var visibility = shows ? Visibility.Visible : Visibility.Collapsed;
        CapbilityLabel.Visibility = visibility;
        CapbilityBox.Visibility = visibility;
    }

    private void ApplyBarrierVisibility()
    {
        var shows = isDynamicCategory || BuildingFieldOptions.ShowsBarrier(FieldPicker.SelectedType);
        var visibility = shows ? Visibility.Visible : Visibility.Collapsed;
        BarrierLabel.Visibility = visibility;
        BarrierToggle.Visibility = visibility;
    }

    private void ApplyFarmingGolemLayoutUi()
    {
        var farming = isDynamicCategory && DynamicFieldOptions.IsFarmingGolem(FieldPicker.SelectedType);
        var scaleVisibility = farming && DynamicFieldOptions.ShowsVisualScale(FieldPicker.SelectedType)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScaleLabel.Visibility = scaleVisibility;
        ScaleBox.Visibility = scaleVisibility;

        // 农业傀儡：size 只读展示，由原版基准占地 × scale 推导。
        if (farming && DynamicFieldOptions.UsesScaledPlaceSize(FieldPicker.SelectedType))
        {
            SizeXLabel.Visibility = Visibility.Visible;
            SizeXBox.Visibility = Visibility.Visible;
            SizeYLabel.Visibility = Visibility.Visible;
            SizeYBox.Visibility = Visibility.Visible;
            SizeXBox.IsReadOnly = true;
            SizeYBox.IsReadOnly = true;
            SizeXLabel.ToolTip = Loc.Get("Str.DynamicEditor.SizeScaledTip");
            SizeYLabel.ToolTip = Loc.Get("Str.DynamicEditor.SizeScaledTip");
            SizeXBox.ToolTip = Loc.Get("Str.DynamicEditor.SizeScaledTip");
            SizeYBox.ToolTip = Loc.Get("Str.DynamicEditor.SizeScaledTip");
            BarrierLabel.ToolTip = DynamicFieldOptions.FarmingGolemColliderHint;
            RefreshScaledPlaceSizeFromScaleBox();
        }
        else
        {
            SizeXLabel.Visibility = Visibility.Visible;
            SizeXBox.Visibility = Visibility.Visible;
            SizeYLabel.Visibility = Visibility.Visible;
            SizeYBox.Visibility = Visibility.Visible;
            SizeXBox.IsReadOnly = false;
            SizeYBox.IsReadOnly = false;
            SizeXLabel.ToolTip = null;
            SizeYLabel.ToolTip = null;
            SizeXBox.ToolTip = null;
            SizeYBox.ToolTip = null;
            BarrierLabel.ToolTip = "是否具备实体碰撞（不可穿过）";
        }
    }

    private void RefreshScaledPlaceSizeFromScaleBox()
    {
        if (!isDynamicCategory || !DynamicFieldOptions.UsesScaledPlaceSize(FieldPicker.SelectedType))
        {
            return;
        }

        var scale = DynamicFieldOptions.DefaultVisualScale;
        if (double.TryParse(ScaleBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            scale = parsed;
        }

        var (sx, sy) = DynamicFieldOptions.ComputeScaledPlaceSize(scale);
        SizeXBox.Text = sx.ToString(CultureInfo.InvariantCulture);
        SizeYBox.Text = sy.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyWorldImageUiForType()
    {
        var farming = isDynamicCategory && DynamicFieldOptions.IsFarmingGolem(FieldPicker.SelectedType);
        WorldImageLabel.Text = farming
            ? Loc.Get("Str.BuildingEditor.FarmingGolemSheet")
            : Loc.Get("Str.BuildingEditor.BuildingImage");
        WorldImageImportButton.Content = farming
            ? Loc.Get("Str.BuildingEditor.ImportFarmingGolemSheet")
            : Loc.Get("Str.BuildingEditor.ImportBuildingImage");
    }

    private void AddMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (gameData.Materials.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "材料表为空，无法新增材料。", Loc.Get("Str.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var defaultId = gameData.Materials[0].Id;
        var material = new MainWindow.CraftMaterial(defaultId, 1);
        materials.Add(material);
        MaterialsGrid.SelectedItem = material;
        RefreshMaterialsEmptyState();
    }

    private void DeleteMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (MaterialsGrid.SelectedItem is MainWindow.CraftMaterial material)
        {
            materials.Remove(material);
            RefreshMaterialsEmptyState();
        }
    }

    private void ImportWorldImage_Click(object sender, RoutedEventArgs e)
    {
        if (isDynamicCategory && DynamicFieldOptions.IsFarmingGolem(FieldPicker.SelectedType))
        {
            ImportFarmingGolemSheet();
            return;
        }

        ImportImageToCurrentBuilding(ImagesRelativePath, "组件图片");
    }

    private void ImportIconImage_Click(object sender, RoutedEventArgs e)
    {
        ImportImageToCurrentBuilding(IconsRelativePath, "物品栏图标");
    }

    private void WorldImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (isDynamicCategory && DynamicFieldOptions.IsFarmingGolem(FieldPicker.SelectedType))
        {
            ImportFarmingGolemSheet();
            return;
        }

        ImportImageToCurrentBuilding(ImagesRelativePath, "组件图片");
    }

    private void IconImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ImportImageToCurrentBuilding(IconsRelativePath, "物品栏图标");
    }

    private void WorldImage_DragEnter(object sender, WpfDragEventArgs e)
    {
        HandleImageDragEnter(
            e,
            WorldImageDropBorder,
            allowPsd: isDynamicCategory && DynamicFieldOptions.IsFarmingGolem(FieldPicker.SelectedType));
    }

    private void WorldImage_DragLeave(object sender, WpfDragEventArgs e)
    {
        ResetDropBorder(WorldImageDropBorder);
    }

    private void WorldImage_Drop(object sender, WpfDragEventArgs e)
    {
        if (isDynamicCategory && DynamicFieldOptions.IsFarmingGolem(FieldPicker.SelectedType))
        {
            try
            {
                var path = TryGetSingleImagePathFromDrag(e, allowPsd: true);
                if (path is not null)
                {
                    ImportFarmingGolemSheet(path);
                }
            }
            finally
            {
                ResetDropBorder(WorldImageDropBorder);
            }

            return;
        }

        HandleImageDrop(e, ImagesRelativePath, "组件图片", WorldImageDropBorder);
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
        HandleImageDrop(e, IconsRelativePath, "物品栏图标", IconImageDropBorder);
    }

    private void ImportFarmingGolemSheet(string? sourceImagePath = null)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.Get("Str.BuildingEditor.ImportFarmingGolemSheet"),
                Filter = FarmingGolemPartsSheet.DialogFilter
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            sourceImagePath = dialog.FileName;
        }

        try
        {
            // 先释放预览对 PNG 的引用，避免覆盖写出时文件被占用。
            WorldImagePreview.Source = null;
            FarmingGolemPartsSheet.ImportAndExport(sourceImagePath, modRoot, buildingId);
            RefreshImagePreview();
        }
        catch (Exception ex)
        {
            RefreshImagePreview();
            System.Windows.MessageBox.Show(
                this,
                $"农业傀儡拆分图导入失败：{ex.Message}",
                Loc.Get("Str.Validate.ImportFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ImportImageToCurrentBuilding(string targetRelativeFolder, string slotName, string? sourceImagePath = null)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"选择{slotName}",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|PNG 文件|*.png|所有文件|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            sourceImagePath = dialog.FileName;
        }

        try
        {
            if (!File.Exists(sourceImagePath))
            {
                System.Windows.MessageBox.Show(this, $"{slotName}导入失败：文件不存在。", Loc.Get("Str.Validate.ImportFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(targetRelativeFolder, ImagesRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                WorldImagePreview.Source = null;
            }
            else if (string.Equals(targetRelativeFolder, IconsRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                IconImagePreview.Source = null;
            }

            var targetFolder = Path.Combine(modRoot, targetRelativeFolder);
            Directory.CreateDirectory(targetFolder);
            var targetPath = Path.Combine(targetFolder, $"{buildingId}.png");
            File.Copy(sourceImagePath, targetPath, overwrite: true);
            RefreshImagePreview();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"{slotName}导入失败：{ex.Message}", Loc.Get("Str.Validate.ImportFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void HandleImageDragEnter(WpfDragEventArgs e, Border border, bool allowPsd = false)
    {
        if (TryGetSingleImagePathFromDrag(e, allowPsd) is not null)
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
            if (path is not null)
            {
                ImportImageToCurrentBuilding(targetRelativeFolder, slotName, path);
            }
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

    private static string? TryGetSingleImagePathFromDrag(WpfDragEventArgs e, bool allowPsd = false)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop) ||
            e.Data.GetData(WpfDataFormats.FileDrop) is not string[] files ||
            files.Length != 1)
        {
            return null;
        }

        var file = files[0];
        var ext = Path.GetExtension(file);
        if (allowPsd && string.Equals(ext, ".psd", StringComparison.OrdinalIgnoreCase))
        {
            return file;
        }

        return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase)
            ? file
            : null;
    }

    private void RefreshImagePreview()
    {
        var worldPath = Path.Combine(modRoot, ImagesRelativePath, $"{buildingId}.png");
        var iconPath = Path.Combine(modRoot, IconsRelativePath, $"{buildingId}.png");
        WorldImagePathText.Text = Path.GetRelativePath(modRoot, worldPath);
        IconImagePathText.Text = Path.GetRelativePath(modRoot, iconPath);
        WorldImagePreview.Source = LoadBitmapIfExists(worldPath);
        IconImagePreview.Source = LoadBitmapIfExists(iconPath);
    }

    private void RefreshMaterialsEmptyState()
    {
        NoMaterialsText.Visibility = materials.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool TryReadMaterials(out IReadOnlyList<MainWindow.CraftMaterial> result)
    {
        MaterialsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        MaterialsGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var snapshot = new List<MainWindow.CraftMaterial>();
        foreach (var material in materials)
        {
            if (material.Id <= 0)
            {
                System.Windows.MessageBox.Show(this, "制造公式里的材料必须从列表中选择。", Loc.Get("Str.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                result = [];
                return false;
            }

            if (gameData.FindMaterial(material.Id) is null)
            {
                System.Windows.MessageBox.Show(this, $"材料 {material.Id} 不在可用材料表中，请重新选择。", Loc.Get("Str.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                result = [];
                return false;
            }

            if (material.Count <= 0 || material.Count > MaxMaterialCount)
            {
                System.Windows.MessageBox.Show(this, $"制造公式里的材料数量必须是 1 到 {MaxMaterialCount} 之间的整数。", Loc.Get("Str.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
                result = [];
                return false;
            }

            snapshot.Add(new MainWindow.CraftMaterial(material.Id, material.Count));
        }

        result = snapshot;
        return true;
    }

    private static BitmapImage? LoadBitmapIfExists(string path)
        => ImagePreviewLoader.Load(path);

    private bool TryReadPositiveInt(string text, out int value, string field)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            System.Windows.MessageBox.Show(this, $"{field} 必须是正整数。", Loc.Get("Str.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private bool TryReadVisualScale(string text, out double value)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            value < DynamicFieldOptions.MinVisualScale ||
            value > DynamicFieldOptions.MaxVisualScale)
        {
            System.Windows.MessageBox.Show(
                this,
                Loc.Format(
                    "Str.Validate.ScaleRange",
                    DynamicFieldOptions.MinVisualScale.ToString(CultureInfo.InvariantCulture),
                    DynamicFieldOptions.MaxVisualScale.ToString(CultureInfo.InvariantCulture)),
                Loc.Get("Str.InputError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            value = DynamicFieldOptions.DefaultVisualScale;
            return false;
        }

        return true;
    }

    private bool TryReadCapbility(string text, out int value, string field)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            System.Windows.MessageBox.Show(
                this,
                $"{field}（容量）必须是 {BuildingFieldOptions.MinCapbility}–{BuildingFieldOptions.MaxCapbility} 之间的整数。",
                Loc.Get("Str.InputError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (value < BuildingFieldOptions.MinCapbility || value > BuildingFieldOptions.MaxCapbility)
        {
            System.Windows.MessageBox.Show(
                this,
                $"{field}（容量）必须在 {BuildingFieldOptions.MinCapbility}–{BuildingFieldOptions.MaxCapbility} 之间。",
                Loc.Get("Str.InputError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private static int ClampCapbility(int value)
    {
        if (value < BuildingFieldOptions.MinCapbility)
        {
            return BuildingFieldOptions.DefaultCapbility;
        }

        if (value > BuildingFieldOptions.MaxCapbility)
        {
            return BuildingFieldOptions.MaxCapbility;
        }

        return value;
    }
}
