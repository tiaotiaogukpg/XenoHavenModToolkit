using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace XenoHavenModToolkit;

public partial class SettingsWindow : Window
{
    internal AppSettings Settings { get; }

    internal SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        OverviewRootBox.Text = settings.ModsOverviewRoot ?? string.Empty;
        OpenLastModCheck.IsChecked = settings.OpenLastModOnStartup;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择 Mod 总览目录（该目录下每个子文件夹都是一个 Mod）",
            UseDescriptionForTitle = true
        };

        if (Directory.Exists(OverviewRootBox.Text.Trim()))
        {
            dialog.SelectedPath = OverviewRootBox.Text.Trim();
        }

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        OverviewRootBox.Text = dialog.SelectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.ModsOverviewRoot = string.IsNullOrWhiteSpace(OverviewRootBox.Text) ? null : OverviewRootBox.Text.Trim();
        Settings.OpenLastModOnStartup = OpenLastModCheck.IsChecked == true;
        DialogResult = true;
    }
}

