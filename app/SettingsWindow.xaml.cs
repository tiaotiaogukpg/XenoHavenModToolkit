using System.Windows;

namespace XenoHavenModToolkit;

public partial class SettingsWindow : Window
{
    private readonly AppSettings settings;

    internal bool DynamicDevFeaturesChanged { get; private set; }

    internal SettingsWindow(AppSettings settings, bool showAdvanced)
    {
        InitializeComponent();
        this.settings = settings;
        OpenLastModCheck.IsChecked = settings.OpenLastModOnStartup;
        EnableDynamicDevCheck.IsChecked = settings.EnableDynamicDevFeatures;

        // 已开启过，或按住 Alt 打开设置：显示隐藏的开发开关
        if (showAdvanced || settings.EnableDynamicDevFeatures)
        {
            AdvancedPanel.Visibility = Visibility.Visible;
            UnlockHintText.Visibility = Visibility.Collapsed;
        }
        else
        {
            AdvancedPanel.Visibility = Visibility.Collapsed;
            UnlockHintText.Visibility = Visibility.Visible;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        settings.OpenLastModOnStartup = OpenLastModCheck.IsChecked == true;
        var nextDynamic = EnableDynamicDevCheck.IsChecked == true;
        DynamicDevFeaturesChanged = nextDynamic != settings.EnableDynamicDevFeatures;
        settings.EnableDynamicDevFeatures = nextDynamic;
        DialogResult = true;
    }
}
