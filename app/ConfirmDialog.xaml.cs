using System.Windows;

namespace XenoHavenModToolkit;

public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    internal static bool Show(Window owner, string title, string message)
    {
        var dialog = new ConfirmDialog(title, message)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
