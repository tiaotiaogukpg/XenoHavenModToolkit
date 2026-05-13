using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace XenoHavenModToolkit;

public partial class BuildingEditorWindow : Window
{
    private static readonly string BuildingImagesRelativePath = Path.Combine("Thing", "Buildings", "images");
    private static readonly string BuildingIconsRelativePath = Path.Combine("Thing", "Buildings", "images", "icon");

    private readonly string modRoot;

    internal MainWindow.EditableBuilding? Result { get; private set; }

    internal BuildingEditorWindow(MainWindow.EditableBuilding initial, string modRoot)
    {
        InitializeComponent();
        this.modRoot = modRoot;
        IdBox.Text = initial.Id.ToString(CultureInfo.InvariantCulture);
        NameBox.Text = initial.Name;
        TypeBox.Text = initial.Type;
        DirectionBox.Text = initial.Direction.ToString(CultureInfo.InvariantCulture);
        CapbilityBox.Text = initial.Capbility.ToString(CultureInfo.InvariantCulture);
        WorkbenchIdBox.Text = initial.WorkbenchId.ToString(CultureInfo.InvariantCulture);
        HealthBox.Text = initial.Health.ToString(CultureInfo.InvariantCulture);
        SizeXBox.Text = initial.SizeX.ToString(CultureInfo.InvariantCulture);
        SizeYBox.Text = initial.SizeY.ToString(CultureInfo.InvariantCulture);
        RefreshImagePreview();
        NameBox.SelectAll();
        NameBox.Focus();
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

        var type = TypeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(type))
        {
            System.Windows.MessageBox.Show(this, "type 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadPositiveInt(DirectionBox.Text, out var direction, "direction") ||
            !TryReadPositiveInt(CapbilityBox.Text, out var capbility, "capbility") ||
            !TryReadPositiveInt(WorkbenchIdBox.Text, out var workbenchId, "workbenchId") ||
            !TryReadPositiveInt(HealthBox.Text, out var health, "health") ||
            !TryReadPositiveInt(SizeXBox.Text, out var sx, "size.x") ||
            !TryReadPositiveInt(SizeYBox.Text, out var sy, "size.y"))
        {
            return;
        }

        Result = new MainWindow.EditableBuilding(id, name, type, direction, capbility, workbenchId, health, sx, sy);
        DialogResult = true;
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

