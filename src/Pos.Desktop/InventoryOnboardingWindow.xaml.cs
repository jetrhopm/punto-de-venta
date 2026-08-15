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
        new ProductCatalogWindow { Owner = this }.ShowDialog();
        MessageText.Text = "Puedes continuar agregando productos más adelante desde Productos.";
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
