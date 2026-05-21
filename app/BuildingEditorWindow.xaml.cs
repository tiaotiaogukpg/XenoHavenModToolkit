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

    private readonly string modRoot;
    private readonly string uuid;
    private readonly GameDataCatalog gameData;
    private readonly ObservableCollection<MainWindow.CraftMaterial> materials;
    private readonly List<LabeledIdOption> materialOptions;

    internal MainWindow.EditableBuilding? Result { get; private set; }

    internal BuildingEditorWindow(MainWindow.EditableBuilding initial, string modRoot, GameDataCatalog gameData)
    {
        InitializeComponent();
        this.modRoot = modRoot;
        this.gameData = gameData;
        uuid = initial.Uuid;
        materialOptions = BuildMaterialOptions(initial.Materials);
        materials = new ObservableCollection<MainWindow.CraftMaterial>(
            initial.Materials.Select(m => new MainWindow.CraftMaterial(m.Id, m.Count)));
        IdBox.Text = initial.Id.ToString(CultureInfo.InvariantCulture);
        HashIdBox.Text = ModBuildingHash.GetHashId(uuid).ToString(CultureInfo.InvariantCulture);
        NameBox.Text = initial.Name;
        FieldPicker.InitializeWorkbenches(gameData);
        FieldPicker.SetValues(initial.Type, initial.Direction, initial.WorkbenchId);
        CapbilityBox.Text = initial.Capbility.ToString(CultureInfo.InvariantCulture);
        HealthBox.Text = initial.Health.ToString(CultureInfo.InvariantCulture);
        SizeXBox.Text = initial.SizeX.ToString(CultureInfo.InvariantCulture);
        SizeYBox.Text = initial.SizeY.ToString(CultureInfo.InvariantCulture);
        ConfigureMaterialsGrid();
        MaterialsGrid.ItemsSource = materials;
        RefreshMaterialsEmptyState();
        RefreshImagePreview();
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void ConfigureMaterialsGrid()
    {
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
            }
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
        if (!TryReadPositiveInt(IdBox.Text, out var id, "id"))
        {
            return;
        }

        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(this, "name 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!FieldPicker.TryValidate(out var fieldError))
        {
            System.Windows.MessageBox.Show(this, fieldError, "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = FieldPicker.SelectedType;
        var direction = FieldPicker.SelectedDirection;
        var workbenchId = FieldPicker.SelectedWorkbenchId;

        if (!TryReadPositiveInt(CapbilityBox.Text, out var capbility, "capbility") ||
            !TryReadPositiveInt(HealthBox.Text, out var health, "health") ||
            !TryReadPositiveInt(SizeXBox.Text, out var sx, "size.x") ||
            !TryReadPositiveInt(SizeYBox.Text, out var sy, "size.y") ||
            !TryReadMaterials(out var materialSnapshot))
        {
            return;
        }

        Result = new MainWindow.EditableBuilding(id, name, uuid, type, direction, capbility, workbenchId, health, sx, sy, materialSnapshot);
        DialogResult = true;
    }

    private void AddMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (gameData.Materials.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "材料表为空，无法新增材料。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        ImportImageToCurrentBuilding(BuildingImagesRelativePath, "地图显示图");
    }

    private void ImportIconImage_Click(object sender, RoutedEventArgs e)
    {
        ImportImageToCurrentBuilding(BuildingIconsRelativePath, "物品图标");
    }

    private void WorldImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ImportImageToCurrentBuilding(BuildingImagesRelativePath, "地图显示图");
    }

    private void IconImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ImportImageToCurrentBuilding(BuildingIconsRelativePath, "物品图标");
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

    private void ImportImageToCurrentBuilding(string targetRelativeFolder, string slotName, string? sourceImagePath = null)
    {
        if (!TryReadPositiveInt(IdBox.Text, out var id, "id"))
        {
            return;
        }

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
                System.Windows.MessageBox.Show(this, $"{slotName}导入失败：文件不存在。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var targetFolder = Path.Combine(modRoot, targetRelativeFolder);
            Directory.CreateDirectory(targetFolder);
            File.Copy(sourceImagePath, Path.Combine(targetFolder, $"{id}.png"), overwrite: true);
            RefreshImagePreview();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"{slotName}导入失败：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private static string? TryGetSingleImagePathFromDrag(WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop) ||
            e.Data.GetData(WpfDataFormats.FileDrop) is not string[] files ||
            files.Length != 1)
        {
            return null;
        }

        var file = files[0];
        var ext = Path.GetExtension(file);
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
        if (!int.TryParse(IdBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return;
        }

        var worldPath = Path.Combine(modRoot, BuildingImagesRelativePath, $"{id}.png");
        var iconPath = Path.Combine(modRoot, BuildingIconsRelativePath, $"{id}.png");
        WorldImagePathText.Text = $"地图显示图：{Path.GetRelativePath(modRoot, worldPath)}";
        IconImagePathText.Text = $"物品图标：{Path.GetRelativePath(modRoot, iconPath)}";
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
                System.Windows.MessageBox.Show(this, "制造公式里的材料必须从列表中选择。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                result = [];
                return false;
            }

            if (gameData.FindMaterial(material.Id) is null)
            {
                System.Windows.MessageBox.Show(this, $"材料 {material.Id} 不在可用材料表中，请重新选择。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                result = [];
                return false;
            }

            if (material.Count <= 0 || material.Count > MaxMaterialCount)
            {
                System.Windows.MessageBox.Show(this, $"制造公式里的材料数量必须是 1 到 {MaxMaterialCount} 之间的整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                result = [];
                return false;
            }

            snapshot.Add(new MainWindow.CraftMaterial(material.Id, material.Count));
        }

        result = snapshot;
        return true;
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

    private bool TryReadPositiveInt(string text, out int value, string field)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            System.Windows.MessageBox.Show(this, $"{field} 必须是正整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }
}
