using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        new("UseWholesalePrice", "Aplicar precio de mayoreo manual (F11)", PermissionGroup.Sales),
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

    private static readonly HashSet<string> CashierPermissions = new(StringComparer.Ordinal)
    {
        "Sell", "UseCommonProduct", "ViewProducts", "CancelSaleLines", "ReprintTickets", "OpenCashDrawer",
        "RecordCashMovements", "ViewSalesHistory", "OpenShift"
    };

    private readonly Dictionary<string, CheckBox> _permissionBoxes = new(StringComparer.Ordinal);
    private UserRow? _editingUser;
    private bool _changingRole;

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
                Width = 360,
                Margin = new Thickness(0, 5, 12, 5)
            };
            PanelFor(permission.Group).Children.Add(box);
            _permissionBoxes.Add(permission.Code, box);
        }

        SetListActions(null);
        Loaded += async (_, _) => await LoadUsersAsync();
    }

    private async Task LoadUsersAsync(Guid? selectUserId = null)
    {
        try
        {
            var users = await ApiClient.Client.GetFromJsonAsync<List<UserRow>>("api/users") ?? [];
            UsersGrid.ItemsSource = users;
            UsersGrid.SelectedItem = selectUserId is null ? null : users.FirstOrDefault(item => item.Id == selectUserId);
            if (UsersGrid.SelectedItem is null) SetListActions(null);
        }
        catch (Exception exception)
        {
            ListStatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar los usuarios");
        }
    }

    private void OnUserSelected(object sender, SelectionChangedEventArgs e)
    {
        var user = UsersGrid.SelectedItem as UserRow;
        SetListActions(user);
        if (user is not null) ListStatusText.Text = $"Seleccionado: {user.DisplayName}.";
    }

    private void OnUserDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow user) return;
        if (!CanModify(user))
        {
            ListStatusText.Text = "Solo un administrador puede modificar a otro administrador.";
            return;
        }
        ShowEditEditor(user);
        e.Handled = true;
    }

    private void SetListActions(UserRow? user)
    {
        var canModify = user is not null && (SessionContext.IsAdministrator || !user.IsAdministrator);
        EditButton.IsEnabled = canModify;
        StatusButton.IsEnabled = canModify;
        StatusButtonText.Text = user?.IsActive == false ? "Activar usuario seleccionado" : "Desactivar usuario seleccionado";
    }

    private void OnCreateClick(object sender, RoutedEventArgs e) => ShowCreateEditor();

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow user) { ListStatusText.Text = "Selecciona un usuario para editar."; return; }
        if (!CanModify(user)) { ListStatusText.Text = "Solo un administrador puede modificar a otro administrador."; return; }
        ShowEditEditor(user);
    }

    private void ShowCreateEditor()
    {
        _editingUser = null;
        EditorTitleText.Text = "Crear usuario";
        EditorDescriptionText.Text = "La nueva cuenta inicia activa con el perfil predeterminado de cajero.";
        NewUserBox.Clear();
        NewNameBox.Clear();
        NewPasswordBox.Clear();
        PasswordLabelText.Text = "Contraseña";
        PasswordHintText.Text = "La contraseña es obligatoria para crear la cuenta.";
        SetAdministrator(false);
        EditorStatusText.Text = string.Empty;
        ListPanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        NewUserBox.Focus();
    }

    private void ShowEditEditor(UserRow user)
    {
        _editingUser = user;
        EditorTitleText.Text = $"Editar usuario: {user.DisplayName}";
        EditorDescriptionText.Text = "Actualiza los datos y los permisos de esta cuenta.";
        NewUserBox.Text = user.UserName;
        NewNameBox.Text = user.DisplayName;
        NewPasswordBox.Clear();
        PasswordLabelText.Text = "Nueva contraseña (opcional)";
        PasswordHintText.Text = "Déjala vacía para conservar la contraseña actual.";
        SetAdministrator(user.IsAdministrator, user.Permissions);
        EditorStatusText.Text = string.Empty;
        ListPanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        NewNameBox.Focus();
    }

    private void OnAdministratorChanged(object sender, RoutedEventArgs e)
    {
        if (_changingRole) return;
        SetPermissionSelection(NewAdminBox.IsChecked == true ? PermissionOptions.Select(item => item.Code) : CashierPermissions, NewAdminBox.IsChecked != true);
        PermissionHintText.Text = NewAdminBox.IsChecked == true
            ? "Los administradores tienen acceso completo. Todos los permisos quedan seleccionados."
            : "Perfil inicial de cajero: ventas, búsqueda, tickets, cajón, movimientos de efectivo y corte del turno propio. Ajusta lo necesario.";
    }

    private void SetAdministrator(bool isAdministrator, IEnumerable<string>? permissions = null)
    {
        _changingRole = true;
        NewAdminBox.IsChecked = isAdministrator;
        _changingRole = false;
        SetPermissionSelection(isAdministrator ? PermissionOptions.Select(item => item.Code) : permissions ?? CashierPermissions, !isAdministrator);
        PermissionHintText.Text = isAdministrator
            ? "Los administradores tienen acceso completo. Todos los permisos quedan seleccionados."
            : "Perfil inicial de cajero: ventas, búsqueda, tickets, cajón, movimientos de efectivo y corte del turno propio. Ajusta lo necesario.";
    }

    private void SetPermissionSelection(IEnumerable<string> selected, bool enabled)
    {
        var selectedPermissions = selected.ToHashSet(StringComparer.Ordinal);
        foreach (var (code, box) in _permissionBoxes)
        {
            box.IsChecked = selectedPermissions.Contains(code);
            box.IsEnabled = enabled;
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveEditorAsync();

    private async Task SaveEditorAsync()
    {
        var isAdministrator = NewAdminBox.IsChecked == true;
        var password = NewPasswordBox.Password;
        if (_editingUser is null && string.IsNullOrEmpty(password))
        {
            EditorStatusText.Text = "Escribe una contraseña para crear el usuario.";
            OperationFeedback.Show(this, "Usuario y permisos", EditorStatusText.Text, OperationResultKind.Warning);
            NewPasswordBox.Focus();
            return;
        }

        try
        {
            Guid userId;
            if (_editingUser is null)
            {
                using var response = await ApiClient.Client.PostAsJsonAsync("api/users", new { userName = NewUserBox.Text, password, displayName = NewNameBox.Text, isAdministrator });
                if (!response.IsSuccessStatusCode)
                {
                    EditorStatusText.Text = await ReadErrorAsync(response);
                    OperationFeedback.Show(this, "Usuario no guardado", EditorStatusText.Text, OperationResultKind.Error);
                    return;
                }
                var created = await response.Content.ReadFromJsonAsync<UserRow>();
                if (created is null)
                {
                    EditorStatusText.Text = "El servidor no devolvió el usuario creado.";
                    OperationFeedback.Show(this, "Usuario no guardado", EditorStatusText.Text, OperationResultKind.Error);
                    return;
                }
                userId = created.Id;
            }
            else
            {
                using var response = await ApiClient.Client.PutAsJsonAsync($"api/users/{_editingUser.Id}", new { userName = NewUserBox.Text, displayName = NewNameBox.Text, isAdministrator, password = string.IsNullOrEmpty(password) ? null : password });
                if (!response.IsSuccessStatusCode)
                {
                    EditorStatusText.Text = await ReadErrorAsync(response);
                    OperationFeedback.Show(this, "Usuario no guardado", EditorStatusText.Text, OperationResultKind.Error);
                    return;
                }
                userId = _editingUser.Id;
            }

            var permissions = _permissionBoxes.Where(item => item.Value.IsChecked == true).Select(item => item.Key).ToArray();
            using var permissionsResponse = await ApiClient.Client.PutAsJsonAsync($"api/users/{userId}/permissions", new { permissions });
            if (!permissionsResponse.IsSuccessStatusCode)
            {
                EditorStatusText.Text = await ReadErrorAsync(permissionsResponse);
                OperationFeedback.Show(this, "Permisos no guardados", EditorStatusText.Text, OperationResultKind.Error);
                return;
            }

            var message = _editingUser is null ? "Usuario creado correctamente." : "Usuario actualizado correctamente.";
            await ShowListAsync(userId, message);
            OperationFeedback.Show(this, "Usuario y permisos", message, OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            EditorStatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar el usuario");
            OperationFeedback.Show(this, "Usuario no guardado", EditorStatusText.Text, OperationResultKind.Error);
        }
    }

    private async void OnToggleStatusClick(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow user) { ListStatusText.Text = "Selecciona un usuario."; return; }
        if (!CanModify(user)) { ListStatusText.Text = "Solo un administrador puede modificar a otro administrador."; return; }
        var action = user.IsActive ? "desactivar" : "activar";
        if (MessageBox.Show($"¿Deseas {action} a {user.DisplayName}?", "Estado de usuario", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync($"api/users/{user.Id}/status", new { isActive = !user.IsActive });
            if (!response.IsSuccessStatusCode)
            {
                ListStatusText.Text = await ReadErrorAsync(response);
                OperationFeedback.Show(this, "Estado de usuario", ListStatusText.Text, OperationResultKind.Error);
                return;
            }
            await LoadUsersAsync(user.Id);
            ListStatusText.Text = user.IsActive ? "Usuario desactivado." : "Usuario activado.";
            OperationFeedback.Show(this, "Estado de usuario", ListStatusText.Text, OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            ListStatusText.Text = ConnectionHelp.FromException(exception, "No se pudo actualizar el estado");
            OperationFeedback.Show(this, "Estado de usuario", ListStatusText.Text, OperationResultKind.Error);
        }
    }

    private async void OnCancelEditorClick(object sender, RoutedEventArgs e) => await ShowListAsync();

    private async Task ShowListAsync(Guid? selectUserId = null, string? message = null)
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Visibility.Visible;
        await LoadUsersAsync(selectUserId);
        if (!string.IsNullOrWhiteSpace(message)) ListStatusText.Text = message;
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && EditorPanel.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            await ShowListAsync();
        }
    }

    private static Task<string> ReadErrorAsync(HttpResponseMessage response) => ConfigurationFeedback.ReadErrorAsync(response, "No se pudo completar la operación.");

    private static bool CanModify(UserRow user) => SessionContext.IsAdministrator || !user.IsAdministrator;

    private WrapPanel PanelFor(PermissionGroup group) => group switch
    {
        PermissionGroup.Sales => SalesPermissionsPanel,
        PermissionGroup.Customers => CustomerPermissionsPanel,
        PermissionGroup.Products => ProductPermissionsPanel,
        PermissionGroup.Inventory => InventoryPermissionsPanel,
        PermissionGroup.Other => OtherPermissionsPanel,
        _ => throw new ArgumentOutOfRangeException(nameof(group))
    };

    private sealed record UserRow(Guid Id, string UserName, string DisplayName, bool IsAdministrator, bool IsActive, List<string> Permissions)
    {
        public string Role => IsAdministrator ? "Administrador" : "Cajero";
        public string State => IsActive ? "Activo" : "Inactivo";
    }

    private sealed record PermissionOption(string Code, string Label, PermissionGroup Group);
    private enum PermissionGroup { Sales, Customers, Products, Inventory, Other }
}
