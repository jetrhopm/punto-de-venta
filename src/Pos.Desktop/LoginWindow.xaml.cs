using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace Pos.Desktop;

public partial class LoginWindow : Window
{
    private const string UnavailableMessage = "JetVenta no está listo para iniciar sesión. Espera unos segundos y vuelve a intentar. Si continúa, reinicia la computadora principal.";
    private static readonly Brush DefaultStatusBackground = new SolidColorBrush(Color.FromRgb(234, 243, 248));
    private static readonly Brush DefaultStatusBorder = new SolidColorBrush(Color.FromRgb(203, 216, 225));
    private static readonly Brush SuccessStatusBackground = new SolidColorBrush(Color.FromRgb(232, 247, 238));
    private static readonly Brush SuccessStatusBorder = new SolidColorBrush(Color.FromRgb(166, 214, 184));
    private static readonly Brush ErrorStatusBackground = new SolidColorBrush(Color.FromRgb(251, 232, 230));
    private static readonly Brush ErrorStatusBorder = new SolidColorBrush(Color.FromRgb(232, 187, 183));
    private static HttpClient Client => ApiClient.Client;
    private bool _isBusy;

    public LoginWindow()
    {
        InitializeComponent();
        ServerText.Text = $"Conexión: {ApiClient.BaseUrl}";
        VersionText.Text = $"Versión {GetApplicationVersion()}";
        Loaded += OnLoaded;
        Activated += (_, _) => UpdateCapsLockWarning();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Revisando que JetVenta esté listo...");
        var available = await ApiClient.WaitUntilAvailableAsync((attempt, maximum) =>
            SetStatus($"Preparando JetVenta ({attempt}/{maximum})...", StatusKind.Progress));

        if (!available)
        {
            SetBusy(false);
            SetStatus(UnavailableMessage, StatusKind.Error);
            return;
        }

        var configured = await EnsureInitialSetupAsync();
        SetBusy(false);
        if (configured)
        {
            SetStatus("JetVenta está listo. Ingresa tus datos para continuar.", StatusKind.Success);
            PasswordBox.Focus();
        }
    }

    private void OnPasswordPreviewKeyUp(object sender, KeyEventArgs e) => UpdateCapsLockWarning();

    private void UpdateCapsLockWarning() =>
        CapsLockPanel.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;

    private void OnServerClick(object sender, RoutedEventArgs e)
    {
        var window = new ServerConnectionWindow { Owner = this };
        if (window.ShowDialog() == true)
        {
            ServerText.Text = $"Conexión: {ApiClient.BaseUrl}";
            SetStatus("Servidor actualizado. JetVenta comprobará la conexión al iniciar sesión.", StatusKind.Information);
        }
    }

    private void OnPairClick(object sender, RoutedEventArgs e) => new JoinServerWindow { Owner = this }.ShowDialog();

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        if (string.IsNullOrWhiteSpace(UserNameTextBox.Text))
        {
            SetStatus("Escribe el usuario para continuar.", StatusKind.Error);
            UserNameTextBox.Focus();
            return;
        }

        if (string.IsNullOrEmpty(PasswordBox.Password))
        {
            SetStatus("Escribe la contraseña para continuar.", StatusKind.Error);
            PasswordBox.Focus();
            return;
        }

        SetBusy(true, "Verificando tus datos...");
        try
        {
            if (!await ApiClient.WaitUntilAvailableAsync())
            {
                SetStatus(UnavailableMessage, StatusKind.Error);
                return;
            }

            if (!await EnsureInitialSetupAsync()) return;

            using var response = await Client.PostAsJsonAsync("api/auth/login", new
            {
                userName = UserNameTextBox.Text.Trim(),
                password = PasswordBox.Password
            });
            if (!response.IsSuccessStatusCode)
            {
                SetStatus(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "El usuario o la contraseña son incorrectos.",
                    HttpStatusCode.ServiceUnavailable => "Los datos de la tienda aún no están disponibles. Espera unos segundos y vuelve a intentar.",
                    _ => $"No se pudo iniciar sesión. Código {(int)response.StatusCode}."
                }, StatusKind.Error);
                PasswordBox.SelectAll();
                PasswordBox.Focus();
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (result is null) throw new InvalidOperationException("El servidor devolvió una sesión vacía.");
            SessionContext.AccessToken = result.AccessToken;
            SessionContext.DisplayName = result.DisplayName;
            SessionContext.IsAdministrator = result.IsAdministrator;
            SessionContext.Permissions.Clear();
            SessionContext.Permissions.UnionWith(result.Permissions);
            // La consulta de licencia requiere la sesión recién creada.
            ApiClient.ApplySession(result.AccessToken);
            if (!await EnsureLicenseActiveAsync()) return;
            var mainWindow = new MainWindow();
            System.Windows.Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
        catch (HttpRequestException)
        {
            SetStatus(UnavailableMessage, StatusKind.Error);
        }
        catch (TaskCanceledException)
        {
            SetStatus("La conexión tardó demasiado en responder. Espera unos segundos y vuelve a intentar.", StatusKind.Error);
        }
        catch (Exception exception)
        {
            SetStatus($"No se pudo iniciar sesión: {exception.Message}", StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> EnsureLicenseActiveAsync()
    {
        try
        {
            var status = await Client.GetFromJsonAsync<LicenseStatus>("api/license/status");
            if (status?.IsActive == true) return true;

            var license = new LicenseWindow { Owner = this };
            license.ShowDialog();
            status = await Client.GetFromJsonAsync<LicenseStatus>("api/license/status");
            if (status?.IsActive == true) return true;

            SetStatus("JetVenta requiere una licencia válida. Un administrador debe cargar el archivo licencia.jv para este equipo.", StatusKind.Error);
            return false;
        }
        catch (HttpRequestException)
        {
            SetStatus("No se pudo consultar la licencia de JetVenta. Revisa la conexión e intenta de nuevo.", StatusKind.Error);
            return false;
        }
    }

    private async Task<bool> EnsureInitialSetupAsync()
    {
        try
        {
            using var setupResponse = await Client.GetAsync("api/setup/status");
            if (!setupResponse.IsSuccessStatusCode)
            {
                SetStatus($"JetVenta no pudo revisar la configuración inicial. Código {(int)setupResponse.StatusCode}.", StatusKind.Error);
                return false;
            }

            var setup = await setupResponse.Content.ReadFromJsonAsync<SetupStatus>();
            if (setup?.Configured == true)
            {
                ApplyStoreName(setup.StoreName);
                return true;
            }

            var setupWindow = new InitialSetupWindow { Owner = this };
            if (setupWindow.ShowDialog() != true)
            {
                SetStatus("La configuración inicial está pendiente. Complétala para iniciar sesión.", StatusKind.Information);
                return false;
            }
            new InventoryOnboardingWindow { Owner = this }.ShowDialog();

            using var refreshedResponse = await Client.GetAsync("api/setup/status");
            if (refreshedResponse.IsSuccessStatusCode)
            {
                var refreshedSetup = await refreshedResponse.Content.ReadFromJsonAsync<SetupStatus>();
                ApplyStoreName(refreshedSetup?.StoreName);
            }

            SetStatus("Tienda creada. Ahora inicia sesión con tus datos.", StatusKind.Success);
            return true;
        }
        catch (HttpRequestException)
        {
            SetStatus(UnavailableMessage, StatusKind.Error);
            return false;
        }
        catch (TaskCanceledException)
        {
            SetStatus("La conexión tardó demasiado en responder. Espera unos segundos y vuelve a intentar.", StatusKind.Error);
            return false;
        }
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        _isBusy = isBusy;
        LoginButton.IsEnabled = !isBusy;
        ServerButton.IsEnabled = !isBusy;
        PairButton.IsEnabled = !isBusy;
        BusyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        LoginButtonIcon.Kind = isBusy ? PackIconMaterialKind.ProgressClock : PackIconMaterialKind.LoginVariant;
        LoginButtonText.Text = isBusy ? "Espera un momento" : "Iniciar sesión";
        if (!string.IsNullOrWhiteSpace(message)) SetStatus(message, StatusKind.Progress);
    }

    private void SetStatus(string message, StatusKind kind)
    {
        MessageText.Text = message;
        StatusBorder.Background = kind switch
        {
            StatusKind.Success => SuccessStatusBackground,
            StatusKind.Error => ErrorStatusBackground,
            _ => DefaultStatusBackground
        };
        StatusBorder.BorderBrush = kind switch
        {
            StatusKind.Success => SuccessStatusBorder,
            StatusKind.Error => ErrorStatusBorder,
            _ => DefaultStatusBorder
        };
        StatusIcon.Kind = kind switch
        {
            StatusKind.Success => PackIconMaterialKind.CheckCircleOutline,
            StatusKind.Error => PackIconMaterialKind.AlertCircleOutline,
            StatusKind.Information => PackIconMaterialKind.InformationOutline,
            _ => PackIconMaterialKind.ProgressClock
        };
        StatusIcon.Foreground = kind switch
        {
            StatusKind.Success => (Brush)FindResource("SuccessBrush"),
            StatusKind.Error => (Brush)FindResource("DangerBrush"),
            _ => (Brush)FindResource("PrimaryBrush")
        };
    }

    private void ApplyStoreName(string? storeName) =>
        StoreNameText.Text = string.IsNullOrWhiteSpace(storeName) ? "Mi tienda" : storeName.Trim();

    private static string GetApplicationVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private enum StatusKind
    {
        Progress,
        Information,
        Success,
        Error
    }

    private sealed record SetupStatus(bool Configured, string? StoreName);
    private sealed record LicenseStatus(bool IsActive);
    private sealed record LoginResponse(Guid SessionId, string AccessToken, Guid UserId, string DisplayName, bool IsAdministrator, DateTimeOffset ExpiresAtUtc, List<string> Permissions);
}
