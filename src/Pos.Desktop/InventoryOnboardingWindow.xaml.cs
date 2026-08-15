using System.Windows;

namespace Pos.Desktop;

public partial class InventoryOnboardingWindow : Window
{
    public InventoryOnboardingWindow() => InitializeComponent();

    private void OnImportLaterClick(object sender, RoutedEventArgs e)
    {
        var window = new ProductImportWindow { Owner = this };
        window.ShowDialog();
        MessageText.Text = "Puedes importar otro archivo más adelante desde Inventario.";
    }

    private void OnManualClick(object sender, RoutedEventArgs e)
    {
        MessageText.Text = "La configuración inicial quedó lista. Agrega productos desde Productos cuando termines este asistente.";
        DialogResult = true;
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
