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
            ShowStatus(ConnectionHelp.FromException(exception, "No se pudieron consultar los usuarios activos"));
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
            ShowStatus("Selecciona el usuario que autoriza esta acción.");
            return;
        }
        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            ShowStatus("Escribe la contraseña del usuario que autoriza.");
            PasswordBox.Focus();
            return;
        }

        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/auth/temporary-permission", new { userName = user.UserName, password = PasswordBox.Password, permission = _permission });
            if (!response.IsSuccessStatusCode)
            {
                var failure = await response.Content.ReadFromJsonAsync<AuthorizationFailure>();
                var message = failure?.Message ?? "No se pudo validar la autorización solicitada.";
                ShowStatus(message, string.Equals(failure?.Code, "permission_missing", StringComparison.OrdinalIgnoreCase));
                if (string.Equals(failure?.Code, "permission_missing", StringComparison.OrdinalIgnoreCase))
                {
                    UserComboBox.Focus();
                }
                else
                {
                    PasswordBox.SelectAll();
                    PasswordBox.Focus();
                }
                return;
            }

            Authorization = await response.Content.ReadFromJsonAsync<TemporaryAuthorizationResponse>();
            if (Authorization is null) throw new InvalidOperationException("La autorización no devolvió un resultado válido.");
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ShowStatus(ConnectionHelp.FromException(exception, "No se pudo validar la autorización"));
        }
    }

    private void ShowStatus(string message, bool warning = false)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(warning ? "#FFF7E2" : "#FDEBEA"));
        StatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(warning ? "#E9C770" : "#E8BBB7"));
        StatusIcon.Kind = warning ? MahApps.Metro.IconPacks.PackIconMaterialKind.AlertOutline : MahApps.Metro.IconPacks.PackIconMaterialKind.AlertCircleOutline;
        StatusIcon.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(warning ? "#A96300" : "#B42318"));
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(warning ? "#6B5200" : "#8D2E27"));
        StatusText.Text = message;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } }

    private sealed record ActiveUser(string UserName, string DisplayName)
    {
        public string DisplayText => $"{DisplayName} ({UserName})";
    }

    public sealed record TemporaryAuthorizationResponse(Guid? GrantId, DateTimeOffset ExpiresAtUtc, string AuthorizedBy);
    private sealed record AuthorizationFailure(string? Code, string? Message);
}
