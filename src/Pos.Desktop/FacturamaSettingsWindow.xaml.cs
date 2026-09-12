using System.Diagnostics;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class FacturamaSettingsWindow : Window
{
    public FacturamaSettingsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<SettingsResult>("api/integrations/facturama/settings");
            if (settings is null) return;
            EnabledCheck.IsChecked = settings.Enabled;
            EnvironmentBox.SelectedValue = settings.Environment;
            if (EnvironmentBox.SelectedIndex < 0) EnvironmentBox.SelectedIndex = 0;
            AccountEmailBox.Text = settings.AccountEmail;
            StatusText.Text = string.IsNullOrWhiteSpace(settings.LastVerificationMessage)
                ? "Guarda las credenciales de la cuenta de esta tienda y luego prueba la conexión."
                : settings.LastVerificationMessage;
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la configuración de Facturama"); }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var command = new
        {
            enabled = EnabledCheck.IsChecked == true,
            environment = EnvironmentBox.SelectedValue?.ToString() ?? "Sandbox",
            accountEmail = AccountEmailBox.Text,
            password = PasswordBox.Password
        };
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/integrations/facturama/settings", command);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo guardar la configuración de Facturama.");
                OperationFeedback.Show(this, "Facturación", StatusText.Text, OperationResultKind.Warning);
                return;
            }
            PasswordBox.Clear();
            StatusText.Text = "Configuración guardada. Prueba la conexión antes de usar solicitudes de factura.";
            OperationFeedback.Show(this, "Configuración guardada", StatusText.Text, OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar la configuración de Facturama");
            OperationFeedback.Show(this, "Facturación", StatusText.Text, OperationResultKind.Error);
        }
    }

    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/integrations/facturama/test", null);
            var result = await response.Content.ReadFromJsonAsync<TestResult>();
            StatusText.Text = result?.Message ?? await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo probar la conexión.");
            OperationFeedback.Show(this, result?.Connected == true ? "Conexión correcta" : "No se pudo conectar", StatusText.Text, result?.Connected == true ? OperationResultKind.Success : OperationResultKind.Warning);
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo probar la conexión con Facturama");
            OperationFeedback.Show(this, "No se pudo conectar", StatusText.Text, OperationResultKind.Error);
        }
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void OnOpenSandboxRegistrationClick(object sender, RoutedEventArgs e) => Open("https://dev.facturama.mx/api/registro");
    private void OnOpenProductionRegistrationClick(object sender, RoutedEventArgs e) => Open("https://app.facturama.mx/api/registro");

    private sealed record SettingsResult(bool Enabled, string Environment, string AccountEmail, bool CredentialsStored, DateTimeOffset? LastVerifiedAtUtc, string LastVerificationMessage);
    private sealed record TestResult(bool Connected, string Message, DateTimeOffset CheckedAtUtc);
}
