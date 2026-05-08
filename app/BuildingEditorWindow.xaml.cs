using System.Globalization;
using System.Windows;

namespace XenoHavenModToolkit;

public partial class BuildingEditorWindow : Window
{
    internal MainWindow.EditableBuilding? Result { get; private set; }

    internal BuildingEditorWindow(MainWindow.EditableBuilding initial)
    {
        InitializeComponent();
        IdBox.Text = initial.Id.ToString(CultureInfo.InvariantCulture);
        NameBox.Text = initial.Name;
        TypeBox.Text = initial.Type;
        DirectionBox.Text = initial.Direction.ToString(CultureInfo.InvariantCulture);
        CapbilityBox.Text = initial.Capbility.ToString(CultureInfo.InvariantCulture);
        WorkbenchIdBox.Text = initial.WorkbenchId.ToString(CultureInfo.InvariantCulture);
        HealthBox.Text = initial.Health.ToString(CultureInfo.InvariantCulture);
        SizeXBox.Text = initial.SizeX.ToString(CultureInfo.InvariantCulture);
        SizeYBox.Text = initial.SizeY.ToString(CultureInfo.InvariantCulture);
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

