using System.Diagnostics;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class MercadoPagoSettingsWindow : Window
{
    private bool _enabled;
    public MercadoPagoSettingsWindow() { InitializeComponent(); Loaded += async (_, _) => await LoadAsync(); }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<SettingsResult>("api/integrations/mercado-pago/settings");
            if (settings is null) return;
            ConnectionTitle.Text = settings.AccountConnected ? $"Cuenta conectada ({settings.Environment})" : "Cuenta sin autorizar";
            ConnectionMessage.Text = settings.Message;
            _enabled = settings.Enabled;
            EnabledButton.Content = _enabled ? "Desactivar Point" : "Activar Point";
            EnabledButton.IsEnabled = settings.AccountConnected;
            AuthorizeButton.IsEnabled = settings.OAuthAvailable;
            AuthorizeButton.ToolTip = settings.OAuthAvailable ? "Abrir Mercado Pago para autorizar JetVenta" : "Requiere registrar la aplicación OAuth y su callback HTTPS";
            StatusText.Text = settings.TerminalLabel;
            if (settings.AccountConnected) await RefreshTerminalsAsync();
        }
        catch (Exception exception) { StatusText.Text = "No se pudo consultar Mercado Pago: " + exception.Message; }
    }

    private async void OnSaveTestTokenClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TestTokenBox.Password)) { StatusText.Text = "Escribe el Access Token de prueba."; return; }
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/integrations/mercado-pago/test-token", new { accessToken = TestTokenBox.Password });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            TestTokenBox.Clear(); StatusText.Text = "Cuenta de prueba validada. Selecciona la terminal."; await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void OnAuthorizeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/integrations/mercado-pago/oauth/start", null);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<OAuthStartResult>();
            if (result is not null) Process.Start(new ProcessStartInfo(result.Url) { UseShellExecute = true });
            StatusText.Text = "Autoriza la cuenta en el navegador y después pulsa Actualizar terminales.";
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void OnEnabledClick(object sender, RoutedEventArgs e)
    {
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/integrations/mercado-pago/enabled", new { enabled = !_enabled });
            StatusText.Text = response.IsSuccessStatusCode ? (!_enabled ? "Mercado Pago Point quedó activo." : "Mercado Pago Point quedó desactivado; las credenciales se conservaron.") : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void OnRefreshTerminalsClick(object sender, RoutedEventArgs e) => await RefreshTerminalsAsync();
    private async Task RefreshTerminalsAsync()
    {
        try
        {
            var terminals = await ApiClient.Client.GetFromJsonAsync<List<TerminalResult>>("api/integrations/mercado-pago/terminals") ?? [];
            TerminalBox.ItemsSource = terminals;
            TerminalBox.SelectedItem = terminals.FirstOrDefault(item => item.Selected) ?? terminals.FirstOrDefault();
            TerminalHelp.Text = terminals.Count == 0 ? "No se encontraron terminales. Vincúlala a una sucursal y caja en Mercado Pago y activa el modo PDV." : $"{terminals.Count} terminal(es) encontradas. Solo las que indiquen PDV pueden guardarse.";
        }
        catch (Exception exception) { StatusText.Text = "No se pudieron listar terminales: " + exception.Message; }
    }

    private async void OnSaveTerminalClick(object sender, RoutedEventArgs e)
    {
        if (TerminalBox.SelectedItem is not TerminalResult terminal) { StatusText.Text = "Selecciona una terminal."; return; }
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/integrations/mercado-pago/terminal", new { terminalId = terminal.Id, label = terminal.Label });
            StatusText.Text = response.IsSuccessStatusCode ? "Terminal guardada. Los cobros con tarjeta se enviarán a Point." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private sealed record SettingsResult(bool Enabled, string Environment, bool AccountConnected, long? AccountUserId, string TerminalId, string TerminalLabel, bool OAuthAvailable, string Message);
    private sealed record TerminalResult(string Id, string Label, string OperatingMode, bool Selected);
    private sealed record OAuthStartResult(string Url);
}
