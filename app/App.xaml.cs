namespace XenoHavenModToolkit;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private void App_Startup(object sender, System.Windows.StartupEventArgs e)
    {
        var window = new MainWindow();
        window.PromptOpenModFolderOnStartup();
        window.Show();
    }
}

