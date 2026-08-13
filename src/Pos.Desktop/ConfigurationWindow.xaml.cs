using System.Windows;

namespace Pos.Desktop;

public partial class ConfigurationWindow : Window
{
    public ConfigurationWindow()
    {
        InitializeComponent();
        StoreButton.IsEnabled = SessionContext.HasPermission("ConfigureStore");
        UsersButton.IsEnabled = SessionContext.IsAdministrator || SessionContext.HasPermission("ManageUsers");
        PrinterButton.IsEnabled = SessionContext.HasPermission("ConfigurePrinters");
        TicketButton.IsEnabled = SessionContext.HasPermission("ConfigureStore");
        BackupButton.IsEnabled = SessionContext.HasPermission("ImportOrExportData");
        ImportButton.IsEnabled = SessionContext.HasPermission("ImportOrExportData");
        StatusText.Text = "Las opciones deshabilitadas requieren un permiso del administrador.";
    }

    private void OnStoreClick(object sender, RoutedEventArgs e) => new StoreSettingsWindow { Owner = this }.ShowDialog();
    private void OnUsersClick(object sender, RoutedEventArgs e) => new UserAdministrationWindow { Owner = this }.ShowDialog();
    private void OnPrinterClick(object sender, RoutedEventArgs e) => new PrinterSettingsWindow { Owner = this }.ShowDialog();
    private void OnTicketClick(object sender, RoutedEventArgs e) => new TicketSettingsWindow { Owner = this }.ShowDialog();
    private void OnBackupClick(object sender, RoutedEventArgs e) => new BackupWindow { Owner = this }.ShowDialog();
    private void OnImportClick(object sender, RoutedEventArgs e) => new ProductImportWindow { Owner = this }.ShowDialog();
}
