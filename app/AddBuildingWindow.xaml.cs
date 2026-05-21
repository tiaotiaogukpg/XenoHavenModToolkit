using System.Globalization;
using System.Windows;

namespace XenoHavenModToolkit;

public partial class AddBuildingWindow : Window
{
    private static class WpfMessageBox
    {
        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
            => System.Windows.MessageBox.Show(owner, messageBoxText, caption, button, icon);
    }

    internal MainWindow.NewBuilding? Result { get; private set; }

    public AddBuildingWindow(int suggestedId, string suggestedName)
    {
        InitializeComponent();
        IdBox.Text = suggestedId.ToString(CultureInfo.InvariantCulture);
        NameBox.Text = suggestedName;
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            WpfMessageBox.Show(this, "id 必须是正整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            WpfMessageBox.Show(this, "name 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = TypeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(type))
        {
            WpfMessageBox.Show(this, "type 不能为空。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(SizeXBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sx) || sx <= 0 ||
            !int.TryParse(SizeYBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sy) || sy <= 0)
        {
            WpfMessageBox.Show(this, "size.x / size.y 必须是正整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new MainWindow.NewBuilding(id, name, type, sx, sy, BuildingFieldOptions.FixedHealth);
        DialogResult = true;
    }
}

