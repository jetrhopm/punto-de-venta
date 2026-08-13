using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;

namespace Pos.Desktop;

public partial class LoginWindow : Window
{
    private static HttpClient Client => ApiClient.Client;

    public LoginWindow()
    {
        InitializeComponent();
        ServerText.Text = $"Servidor: {ApiClient.BaseUrl}";
        PasswordBox.Focus();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        MessageText.Text = "Comprobando la configuración inicial...";
        var configured = await EnsureInitialSetupAsync();
        if (configured) MessageText.Text = "Ingresa los datos del administrador para continuar.";
        LoginButton.IsEnabled = true;
    }

    private void OnServerClick(object sender, RoutedEventArgs e)
    {
        var window = new ServerConnectionWindow { Owner = this };
        if (window.ShowDialog() == true) ServerText.Text = $"Servidor: {ApiClient.BaseUrl}";
    }

    private void OnPairClick(object sender, RoutedEventArgs e) => new JoinServerWindow { Owner = this }.ShowDialog();

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        MessageText.Text = "";
        LoginButton.IsEnabled = false;
        try
        {
            if (!await EnsureInitialSetupAsync()) return;
            var response = await Client.PostAsJsonAsync("/api/auth/login", new { userName = UserNameTextBox.Text, password = PasswordBox.Password });
            if (!response.IsSuccessStatusCode)
            {
                MessageText.Text = response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Usuario o contraseña incorrectos." : "No se pudo iniciar sesion.";
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            SessionContext.AccessToken = result?.AccessToken;
            SessionContext.DisplayName = result?.DisplayName;
            SessionContext.IsAdministrator = result?.IsAdministrator == true;
            SessionContext.Permissions.Clear();
            if (result?.Permissions is not null) SessionContext.Permissions.UnionWith(result.Permissions);
            var mainWindow = new MainWindow();
            System.Windows.Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
        catch (HttpRequestException) { MessageText.Text = "La API local no esta disponible. Verifica el servicio PuntoDeVentaApi."; }
        catch (TaskCanceledException) { MessageText.Text = "La API tardo demasiado en responder. Verifica PostgreSQL y el servicio PuntoDeVentaApi."; }
        finally { LoginButton.IsEnabled = true; }
    }

    private async Task<bool> EnsureInitialSetupAsync()
    {
        try
        {
            using var setupResponse = await Client.GetAsync("/api/setup/status");
            if (!setupResponse.IsSuccessStatusCode)
            {
                MessageText.Text = $"La API respondio con {(int)setupResponse.StatusCode}. Revisa el servicio local.";
                return false;
            }

            var setup = await setupResponse.Content.ReadFromJsonAsync<SetupStatus>();
            if (setup?.Configured == true) return true;

            var setupWindow = new InitialSetupWindow { Owner = this };
            if (setupWindow.ShowDialog() == true)
            {
                var inventoryWindow = new InventoryOnboardingWindow { Owner = this };
                inventoryWindow.ShowDialog();
                MessageText.Text = "Tienda creada. Ahora inicia sesión con tus datos.";
                return true;
            }
            return false;
        }
        catch (HttpRequestException)
        {
            MessageText.Text = "La API local no está disponible. Verifica el servicio PuntoDeVentaApi.";
            return false;
        }
        catch (TaskCanceledException)
        {
            MessageText.Text = "La API tardó demasiado en responder. Verifica PostgreSQL y el servicio PuntoDeVentaApi.";
            return false;
        }
    }

    private sealed record SetupStatus(bool Configured, string? StoreName);
    private sealed record LoginResponse(Guid SessionId, string AccessToken, Guid UserId, string DisplayName, bool IsAdministrator, DateTimeOffset ExpiresAtUtc, List<string> Permissions);
}
