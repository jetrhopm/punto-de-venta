using System.Net.Http.Json;
using System.Net.Http;
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
            ShowAuthorizationError("Selecciona el usuario que autoriza esta acción.", MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ShowAuthorizationError("Escribe la contraseña del usuario que autoriza.", MessageBoxImage.Warning);
            PasswordBox.Focus();
            return;
        }

        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/auth/temporary-permission", new { userName = user.UserName, password = PasswordBox.Password, permission = _permission });
            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadAuthorizationErrorAsync(response);
                var image = response.StatusCode == System.Net.HttpStatusCode.Forbidden ? MessageBoxImage.Warning : MessageBoxImage.Error;
                ShowAuthorizationError(message, image);
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
            ShowAuthorizationError(ConnectionHelp.FromException(exception, "No se pudo validar la autorización"), MessageBoxImage.Error);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } }

    private static async Task<string> ReadAuthorizationErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<AuthorizationError>();
            if (!string.IsNullOrWhiteSpace(error?.Detail)) return error.Detail;
        }
        catch (System.Text.Json.JsonException) { }
        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "La contraseña del usuario que autoriza no es correcta.",
            System.Net.HttpStatusCode.Forbidden => "Ese usuario no tiene el permiso requerido para autorizar esta acción.",
            System.Net.HttpStatusCode.NotFound => "El usuario que autoriza ya no está disponible.",
            _ => "No se pudo validar la autorización temporal. Intenta nuevamente."
        };
    }

    private void ShowAuthorizationError(string message, MessageBoxImage image)
    {
        StatusText.Text = message;
        MessageBox.Show(message, "Autorización no concedida", MessageBoxButton.OK, image);
    }

    private sealed record ActiveUser(string UserName, string DisplayName)
    {
        public string DisplayText => $"{DisplayName} ({UserName})";
    }

    private sealed record AuthorizationError(string? Detail);

    public sealed record TemporaryAuthorizationResponse(Guid? GrantId, DateTimeOffset ExpiresAtUtc, string AuthorizedBy);
}
