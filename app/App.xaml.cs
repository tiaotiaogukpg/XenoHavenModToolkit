namespace XenoHavenModToolkit;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private void App_Startup(object sender, System.Windows.StartupEventArgs e)
    {
        var settings = AppSettings.Load();
        ThemeManager.ApplyTheme(ThemeManager.Parse(settings.Theme));

        var window = new MainWindow();
        window.InitializeOnStartup();
        window.Show();
    }
}

