using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class UserAdministrationWindow : Window
{
    private static readonly PermissionOption[] PermissionOptions =
    [
        new("Sell", "Vender y cobrar tickets", PermissionGroup.Sales),
        new("SellOnCredit", "Cobrar a crédito", PermissionGroup.Sales),
        new("UseCommonProduct", "Utilizar producto común", PermissionGroup.Sales),
        new("ChangeSalePrice", "Cambiar precio durante la venta", PermissionGroup.Sales),
        new("ApplyDiscounts", "Aplicar descuentos", PermissionGroup.Sales),
        new("UseWholesalePrice", "Aplicar precio de mayoreo", PermissionGroup.Sales),
        new("CancelSaleLines", "Eliminar productos del ticket", PermissionGroup.Sales),
        new("CancelSales", "Cancelar ventas confirmadas", PermissionGroup.Sales),
        new("ProcessReturns", "Procesar devoluciones", PermissionGroup.Sales),
        new("ReprintTickets", "Reimprimir tickets", PermissionGroup.Sales),
        new("OpenCashDrawer", "Abrir cajón de dinero", PermissionGroup.Sales),
        new("RecordCashMovements", "Registrar entrada F7 y salida F8 de efectivo", PermissionGroup.Sales),
        new("ViewSalesHistory", "Revisar historial de ventas", PermissionGroup.Sales),

        new("ManageCustomersAndCredit", "Crear, modificar, desactivar clientes; asignar clientes, crédito, cuenta, abonos y reportes", PermissionGroup.Customers),

        new("ViewProducts", "Consultar catálogo de productos", PermissionGroup.Products),
        new("ManageProducts", "Crear, modificar y eliminar productos; administrar departamentos, kits y promociones", PermissionGroup.Products),

        new("ViewInventory", "Ver existencias, mínimos, máximos y movimientos de inventario", PermissionGroup.Inventory),
        new("AdjustInventory", "Ajustar inventario y modificar mínimos o máximos", PermissionGroup.Inventory),
        new("ViewCostsAndProfit", "Ver costos, utilidad y valor del inventario", PermissionGroup.Inventory),
        new("ImportOrExportData", "Importar o exportar inventario y crear respaldos", PermissionGroup.Inventory),

        new("OpenShift", "Abrir turno", PermissionGroup.Other),
        new("CloseShift", "Realizar corte del turno propio y ver efectivo esperado", PermissionGroup.Other),
        new("ViewPreviousShifts", "Consultar cortes de cajeros y corte consolidado del día", PermissionGroup.Other),
        new("ManageSuppliersAndPurchases", "Administrar proveedores y compras", PermissionGroup.Other),
        new("ProcessServicePayments", "Realizar recargas y pagos de servicios", PermissionGroup.Other),
        new("ViewReports", "Acceder a reportes de ventas, ganancias y análisis", PermissionGroup.Other),
        new("ConfigurePrinters", "Configurar impresoras y formato de ticket", PermissionGroup.Other),
        new("ConfigureStore", "Configurar datos de la tienda", PermissionGroup.Other),
        new("ManageUsers", "Administrar usuarios y permisos", PermissionGroup.Other)
    ];

    private readonly Dictionary<string, CheckBox> _permissionBoxes = new(StringComparer.Ordinal);

    public UserAdministrationWindow()
    {
        InitializeComponent();
        NewAdminBox.IsEnabled = SessionContext.IsAdministrator;
        foreach (var permission in PermissionOptions)
        {
            var box = new CheckBox
            {
                Content = permission.Label,
                Tag = permission.Code,
                ToolTip = permission.Label,
                Width = 265,
                Margin = new Thickness(0, 4, 8, 4)
            };
            PanelFor(permission.Group).Children.Add(box);
            _permissionBoxes.Add(permission.Code, box);
        }
        Loaded += async (_, _) => await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        try { UsersGrid.ItemsSource = await ApiClient.Client.GetFromJsonAsync<List<UserRow>>("api/users") ?? []; }
        catch (Exception exception) { StatusText.Text = $"No se pudieron cargar los usuarios: {exception.Message}"; }
    }

    private void OnUserSelected(object sender, SelectionChangedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow user) return;
        var canModify = SessionContext.IsAdministrator || !user.IsAdministrator;
        foreach (var box in _permissionBoxes.Values) box.IsChecked = user.IsAdministrator || user.Permissions.Contains((string)box.Tag);
        foreach (var box in _permissionBoxes.Values) box.IsEnabled = canModify && !user.IsAdministrator;
        StatusButton.Content = user.IsActive ? "Desactivar usuario seleccionado" : "Activar usuario seleccionado";
        StatusText.Text = canModify ? $"Seleccionado: {user.DisplayName}" : "Solo un administrador puede modificar a otro administrador.";
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/users", new { userName = NewUserBox.Text, password = NewPasswordBox.Password, displayName = NewNameBox.Text, isAdministrator = NewAdminBox.IsChecked == true });
            StatusText.Text = response.IsSuccessStatusCode ? "Usuario creado correctamente." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) { NewUserBox.Clear(); NewNameBox.Clear(); NewPasswordBox.Clear(); NewAdminBox.IsChecked = false; await LoadUsersAsync(); }
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void OnToggleStatusClick(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow user) { StatusText.Text = "Selecciona un usuario."; return; }
        using var response = await ApiClient.Client.PutAsJsonAsync($"api/users/{user.Id}/status", new { isActive = !user.IsActive });
        StatusText.Text = response.IsSuccessStatusCode ? "Estado actualizado." : await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode) await LoadUsersAsync();
    }

    private async void OnSavePermissionsClick(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow user) { StatusText.Text = "Selecciona un usuario."; return; }
        var permissions = _permissionBoxes.Values.Where(item => item.IsChecked == true).Select(item => (string)item.Tag).ToArray();
        using var response = await ApiClient.Client.PutAsJsonAsync($"api/users/{user.Id}/permissions", new { permissions });
        StatusText.Text = response.IsSuccessStatusCode ? "Permisos guardados." : await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode) await LoadUsersAsync();
    }

    private async void OnResetPasswordClick(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow user) { StatusText.Text = "Selecciona un usuario."; return; }
        using var response = await ApiClient.Client.PutAsJsonAsync($"api/users/{user.Id}/password", new { password = ResetPasswordBox.Password });
        StatusText.Text = response.IsSuccessStatusCode ? "Contraseña actualizada." : await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode) ResetPasswordBox.Clear();
    }

    private WrapPanel PanelFor(PermissionGroup group) => group switch
    {
        PermissionGroup.Sales => SalesPermissionsPanel,
        PermissionGroup.Customers => CustomerPermissionsPanel,
        PermissionGroup.Products => ProductPermissionsPanel,
        PermissionGroup.Inventory => InventoryPermissionsPanel,
        PermissionGroup.Other => OtherPermissionsPanel,
        _ => throw new ArgumentOutOfRangeException(nameof(group))
    };

    private sealed record UserRow(Guid Id, string UserName, string DisplayName, bool IsAdministrator, bool IsActive, List<string> Permissions);
    private sealed record PermissionOption(string Code, string Label, PermissionGroup Group);
    private enum PermissionGroup { Sales, Customers, Products, Inventory, Other }
}
