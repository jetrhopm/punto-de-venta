using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class LoginWindow : Window
{
    private const string UnavailableMessage = "No hay conexión con el servidor local. PostgreSQL o PuntoDeVentaApi no iniciaron correctamente. Revisa C:\\ProgramData\\PuntoDeVenta\\logs\\api-startup.log.";
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
        MessageText.Text = "Comprobando los servicios locales...";
        var available = await ApiClient.WaitUntilAvailableAsync((attempt, maximum) =>
            MessageText.Text = $"Esperando al servidor local ({attempt}/{maximum})...");
        if (!available)
        {
            MessageText.Text = UnavailableMessage;
            LoginButton.IsEnabled = true;
            return;
        }

        var configured = await EnsureInitialSetupAsync();
        if (configured) MessageText.Text = "Ingresa tus datos para continuar.";
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
        LoginButton.IsEnabled = false;
        try
        {
            MessageText.Text = "Comprobando conexión con el servidor...";
            if (!await ApiClient.WaitUntilAvailableAsync())
            {
                MessageText.Text = UnavailableMessage;
                return;
            }
            if (!await EnsureInitialSetupAsync()) return;

            MessageText.Text = "Validando usuario y contraseña...";
            using var response = await Client.PostAsJsonAsync("api/auth/login", new { userName = UserNameTextBox.Text, password = PasswordBox.Password });
            if (!response.IsSuccessStatusCode)
            {
                MessageText.Text = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Usuario o contraseña incorrectos.",
                    HttpStatusCode.ServiceUnavailable => "PostgreSQL no está disponible. Espera unos segundos y vuelve a intentar.",
                    _ => $"El servidor respondió con error {(int)response.StatusCode}. Revisa C:\\ProgramData\\PuntoDeVenta\\logs\\api-startup.log."
                };
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (result is null) throw new InvalidOperationException("El servidor devolvió una sesión vacía.");
            SessionContext.AccessToken = result.AccessToken;
            SessionContext.DisplayName = result.DisplayName;
            SessionContext.IsAdministrator = result.IsAdministrator;
            SessionContext.Permissions.Clear();
            SessionContext.Permissions.UnionWith(result.Permissions);
            var mainWindow = new MainWindow();
            System.Windows.Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
        catch (HttpRequestException) { MessageText.Text = UnavailableMessage; }
        catch (TaskCanceledException) { MessageText.Text = "El servidor tardó demasiado en responder. Espera unos segundos y vuelve a intentar."; }
        catch (Exception exception) { MessageText.Text = $"No se pudo iniciar sesión: {exception.Message}"; }
        finally { LoginButton.IsEnabled = true; }
    }

    private async Task<bool> EnsureInitialSetupAsync()
    {
        try
        {
            using var setupResponse = await Client.GetAsync("api/setup/status");
            if (!setupResponse.IsSuccessStatusCode)
            {
                MessageText.Text = $"El servidor respondió con error {(int)setupResponse.StatusCode}. Revisa el servicio local.";
                return false;
            }

            var setup = await setupResponse.Content.ReadFromJsonAsync<SetupStatus>();
            if (setup?.Configured == true) return true;

            var setupWindow = new InitialSetupWindow { Owner = this };
            if (setupWindow.ShowDialog() != true) return false;
            new InventoryOnboardingWindow { Owner = this }.ShowDialog();
            MessageText.Text = "Tienda creada. Ahora inicia sesión con tus datos.";
            return true;
        }
        catch (HttpRequestException)
        {
            MessageText.Text = UnavailableMessage;
            return false;
        }
        catch (TaskCanceledException)
        {
            MessageText.Text = "El servidor tardó demasiado en responder. Espera unos segundos y vuelve a intentar.";
            return false;
        }
    }

    private sealed record SetupStatus(bool Configured, string? StoreName);
    private sealed record LoginResponse(Guid SessionId, string AccessToken, Guid UserId, string DisplayName, bool IsAdministrator, DateTimeOffset ExpiresAtUtc, List<string> Permissions);
}
