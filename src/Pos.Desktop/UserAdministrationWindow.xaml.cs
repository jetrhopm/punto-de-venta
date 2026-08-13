using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class UserAdministrationWindow : Window
{
    private static readonly (string Code, string Label)[] PermissionOptions =
    [
        ("Sell", "Vender"), ("ViewProducts", "Consultar productos"), ("ManageProducts", "Crear y editar productos"), ("ChangeSalePrice", "Cambiar precio"),
        ("ApplyDiscounts", "Aplicar descuentos"), ("UseWholesalePrice", "Usar mayoreo"), ("ViewInventory", "Consultar inventario"), ("AdjustInventory", "Ajustar inventario"),
        ("ViewCostsAndProfit", "Ver costos y utilidad"), ("CancelSaleLines", "Cancelar partidas"), ("CancelSales", "Cancelar ventas"), ("ProcessReturns", "Procesar devoluciones"),
        ("ReprintTickets", "Reimprimir tickets"), ("OpenCashDrawer", "Abrir cajón"), ("RecordCashMovements", "Movimientos de efectivo"), ("ViewSalesHistory", "Historial de ventas"),
        ("OpenShift", "Abrir turno"), ("CloseShift", "Realizar corte"), ("ViewPreviousShifts", "Ver cortes anteriores"), ("ViewReports", "Ver reportes"),
        ("ManageCustomersAndCredit", "Clientes y crédito"), ("ManageSuppliersAndPurchases", "Proveedores y compras"), ("ProcessServicePayments", "Recargas y servicios"),
        ("ConfigurePrinters", "Configurar impresoras"), ("ConfigureStore", "Configurar tienda"), ("ManageUsers", "Administrar usuarios"), ("ImportOrExportData", "Importar, exportar y respaldar")
    ];

    public UserAdministrationWindow()
    {
        InitializeComponent();
        NewAdminBox.IsEnabled = SessionContext.IsAdministrator;
        foreach (var permission in PermissionOptions) PermissionsPanel.Children.Add(new CheckBox { Content = permission.Label, Tag = permission.Code, Width = 220, Margin = new Thickness(0, 3, 4, 3) });
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
        foreach (CheckBox box in PermissionsPanel.Children) box.IsChecked = user.IsAdministrator || user.Permissions.Contains((string)box.Tag);
        foreach (CheckBox box in PermissionsPanel.Children) box.IsEnabled = canModify && !user.IsAdministrator;
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
        var permissions = PermissionsPanel.Children.OfType<CheckBox>().Where(item => item.IsChecked == true).Select(item => (string)item.Tag).ToArray();
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

    private sealed record UserRow(Guid Id, string UserName, string DisplayName, bool IsAdministrator, bool IsActive, List<string> Permissions);
}
