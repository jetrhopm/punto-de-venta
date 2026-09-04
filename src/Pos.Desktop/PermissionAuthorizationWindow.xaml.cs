using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class PermissionAuthorizationWindow : Window
{
    private readonly string _permission;
    private List<ActiveUser> _users = [];

    public PermissionAuthorizationWindow(string permission, string action)
    {
        InitializeComponent();
        _permission = permission;
        ActionText.Text = action;
        RequiredPermissionText.Text = $"Se requiere: {PermissionAuthorization.NameFor(permission)}.";
        Loaded += OnLoaded;
    }

    public TemporaryAuthorizationResponse? Authorization { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _users = await ApiClient.Client.GetFromJsonAsync<List<ActiveUser>>("api/auth/active-users") ?? [];
            RefreshUsers();
            PasswordBox.Focus();
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron consultar los usuarios activos");
        }
    }

    private void OnUserFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshUsers();

    private void RefreshUsers()
    {
        var filter = UserFilterTextBox.Text.Trim();
        var items = _users.Where(user => string.IsNullOrWhiteSpace(filter) || user.DisplayText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        UserComboBox.ItemsSource = items;
        UserComboBox.SelectedIndex = items.Count == 1 ? 0 : -1;
    }

    private async void OnAuthorizeClick(object sender, RoutedEventArgs e)
    {
        if (UserComboBox.SelectedItem is not ActiveUser user)
        {
            StatusText.Text = "Selecciona el usuario que autoriza esta acción.";
            return;
        }
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            StatusText.Text = "Escribe la contraseña del usuario que autoriza.";
            PasswordBox.Focus();
            return;
        }

        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/auth/temporary-permission", new { userName = user.UserName, password = PasswordBox.Password, permission = _permission });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = "La cuenta o contraseña no son válidas, o esa cuenta no tiene el permiso requerido.";
                PasswordBox.SelectAll();
                PasswordBox.Focus();
                return;
            }

            Authorization = await response.Content.ReadFromJsonAsync<TemporaryAuthorizationResponse>();
            if (Authorization is null) throw new InvalidOperationException("La autorización no devolvió un resultado válido.");
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo validar la autorización");
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } }

    private sealed record ActiveUser(string UserName, string DisplayName)
    {
        public string DisplayText => $"{DisplayName} ({UserName})";
    }

    public sealed record TemporaryAuthorizationResponse(Guid? GrantId, DateTimeOffset ExpiresAtUtc, string AuthorizedBy);
}
