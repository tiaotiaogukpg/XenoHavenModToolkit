using System.Windows;

namespace XenoHavenModToolkit;

public partial class SettingsWindow : Window
{
    internal AppSettings Settings { get; }

    internal SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        OpenLastModCheck.IsChecked = settings.OpenLastModOnStartup;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.OpenLastModOnStartup = OpenLastModCheck.IsChecked == true;
        DialogResult = true;
    }
}
