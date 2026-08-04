namespace XenoHavenModToolkit;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private void App_Startup(object sender, System.Windows.StartupEventArgs e)
    {
        var settings = AppSettings.Load();
        LocalizationManager.ApplyLanguage(LocalizationManager.Resolve(settings.Language));
        ThemeManager.ApplyTheme(AppTheme.Dark);

        var window = new MainWindow();
        window.InitializeOnStartup();
        window.Show();
    }
}

