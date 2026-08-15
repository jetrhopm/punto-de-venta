using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class ConfigurationWindow : UserControl
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

    private void OnStoreClick(object sender, RoutedEventArgs e) => new StoreSettingsWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    private void OnUsersClick(object sender, RoutedEventArgs e) => new UserAdministrationWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    private void OnPrinterClick(object sender, RoutedEventArgs e) => new PrinterSettingsWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    private void OnTicketClick(object sender, RoutedEventArgs e) => new TicketSettingsWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    private void OnBackupClick(object sender, RoutedEventArgs e) => new BackupWindow { Owner = Window.GetWindow(this) }.ShowDialog();
    private void OnImportClick(object sender, RoutedEventArgs e) => new ProductImportWindow { Owner = Window.GetWindow(this) }.ShowDialog();
}
