using System.Windows;

namespace XenoHavenModToolkit;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ProductNameText.Text = AppVersion.ProductName;
        VersionText.Text = Loc.Format("Str.About.Version", AppVersion.Display);
        RuntimeText.Text = Loc.Format("Str.About.Runtime", Environment.Version);
        RunModeText.Text = Loc.Format("Str.About.RunMode", AppPaths.RunModeLabel);
    }

    internal static void Show(Window owner)
    {
        var dialog = new AboutWindow
        {
            Owner = owner
        };
        dialog.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
