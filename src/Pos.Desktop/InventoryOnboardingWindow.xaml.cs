using System.Windows;

namespace Pos.Desktop;

public partial class InventoryOnboardingWindow : Window
{
    public InventoryOnboardingWindow() => InitializeComponent();

    private void OnImportLaterClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnManualClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
