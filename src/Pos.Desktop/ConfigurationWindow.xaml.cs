using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class ConfigurationWindow : UserControl
{
    public ConfigurationWindow()
    {
        InitializeComponent();
        if (!InstallationRoleContext.IsServer)
        {
            BackupButton.Visibility = Visibility.Collapsed;
            RegistersButton.Visibility = Visibility.Collapsed;
            ConnectRegisterButton.Visibility = Visibility.Collapsed;
            FacturamaButton.Visibility = Visibility.Collapsed;
            StatusText.Text = "Caja adicional: los respaldos, PostgreSQL y administración de cajas se realizan desde el servidor.";
        }
        else StatusText.Text = "Las acciones con acceso restringido solicitarán autorización temporal.";
    }

    private async void OnStoreClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Modificar los datos de la tienda", () => new StoreSettingsWindow());
    private async void OnFoliosClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Modificar los folios", () => new SaleFolioSettingsWindow());
    private async void OnMeasureClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Modificar unidades de medida", () => new MeasureSettingsWindow());
    private async void OnCurrencyClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Modificar el símbolo de moneda", () => new CurrencySettingsWindow());
    private async void OnPaymentMethodsClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar formas de pago", () => new PaymentMethodSettingsWindow());
    private async void OnMercadoPagoClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar Mercado Pago Point", () => new MercadoPagoSettingsWindow());
    private async void OnCutClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar cortes de caja", () => new CutSettingsWindow());
    private async void OnOptionsClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Modificar opciones habilitadas", () => new StoreOptionsWindow());
    private async void OnUsersClick(object sender, RoutedEventArgs e) => await OpenAsync("ManageUsers", "Administrar cajeros y permisos", () => new UserAdministrationWindow());
    private async void OnRegistersClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Administrar cajas y equipos", () => new RegisterAdministrationWindow());
    private async void OnConnectRegisterClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Conectar una caja adicional", () => new PairingCodeWindow());
    private async void OnPrinterClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigurePrinters", "Configurar impresoras", () => new PrinterSettingsWindow());
    private async void OnScannerClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar lector de códigos", () => new BarcodeScannerSettingsWindow());
    private async void OnTicketClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar tickets", () => new TicketSettingsWindow());
    private async void OnCashDrawerClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar cajón de dinero", () => new CashDrawerSettingsWindow());
    private async void OnScaleClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar báscula", () => new ScaleSettingsWindow());
    private async void OnSystemDetailsClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Consultar detalles del sistema", () => new SystemDetailsWindow());
    private async void OnDiagnosticClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Abrir diagnóstico", () => new DiagnosticWindow());
    private async void OnBackupClick(object sender, RoutedEventArgs e)
    {
        if (!InstallationRoleContext.IsServer) { StatusText.Text = "Los respaldos y restauraciones sólo se administran desde la caja principal / servidor."; return; }
        await OpenAsync("ImportOrExportData", "Administrar respaldos", () => new BackupWindow());
    }
    private async void OnImportClick(object sender, RoutedEventArgs e) => await OpenAsync("ImportOrExportData", "Importar productos", () => new ProductImportWindow());
    private async void OnLicenseClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Administrar la licencia", () => new LicenseWindow());
    private async void OnAutomaticStartClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar inicio automático", () => new AutomaticStartSettingsWindow());
    private async void OnFacturamaClick(object sender, RoutedEventArgs e) => await OpenAsync("ConfigureStore", "Configurar la cuenta Facturama", () => new FacturamaSettingsWindow());

    private async Task OpenAsync(string permission, string action, Func<Window> createWindow)
    {
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        await using var authorization = await PermissionAuthorization.RequestAsync(owner, permission, $"{action} requiere autorización.");
        if (authorization is null)
        {
            StatusText.Text = $"No tienes permiso para {PermissionAuthorization.NameFor(permission).ToLowerInvariant()}.";
            return;
        }
        var window = createWindow();
        window.Owner = owner;
        window.ShowDialog();
    }

    private void OnPendingClick(object sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag?.ToString() ?? "Esta función";
        StatusText.Text = $"{name}: función en preparación. Se habilitará cuando el módulo esté validado para operación real.";
        MessageBox.Show($"{name} aún está en preparación. JetVenta no simula integraciones ni operaciones que todavía no estén implementadas.", "Función en preparación", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
