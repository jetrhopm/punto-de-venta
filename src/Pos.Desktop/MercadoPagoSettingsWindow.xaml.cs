using System.Diagnostics;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class MercadoPagoSettingsWindow : Window
{
    private bool _enabled;
    private CancellationTokenSource? _oauthPolling;
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
            DisconnectButton.Visibility = settings.AccountConnected ? Visibility.Visible : Visibility.Collapsed;
            DisconnectButton.ToolTip = "Quita de JetVenta los tokens, la cuenta y las terminales asociadas. No borra ventas ni cobros históricos.";
            AuthorizeButton.IsEnabled = true;
            AuthorizeButton.ToolTip = settings.OAuthAvailable ? "Abrir Mercado Pago para autorizar JetVenta" : "La API todavía necesita la aplicación OAuth y su callback HTTPS";
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
        _oauthPolling?.Cancel();
        _oauthPolling = new CancellationTokenSource();
        var cancellationToken = _oauthPolling.Token;
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/integrations/mercado-pago/oauth/start", null);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<OAuthStartResult>();
            if (result is not null) Process.Start(new ProcessStartInfo(result.Url) { UseShellExecute = true });
            AuthorizeButton.IsEnabled = false;
            StatusText.Text = "Autoriza tu cuenta en el navegador. JetVenta cargará las terminales automáticamente al terminar.";
            await WaitForOAuthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { StatusText.Text = exception.Message; }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) AuthorizeButton.IsEnabled = true;
        }
    }

    private async Task WaitForOAuthAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 90; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            var settings = await ApiClient.Client.GetFromJsonAsync<SettingsResult>("api/integrations/mercado-pago/settings", cancellationToken);
            if (settings?.AccountConnected == true && string.Equals(settings.Environment, "Production", StringComparison.OrdinalIgnoreCase))
            {
                ConnectionTitle.Text = "Cuenta conectada (Producción)";
                ConnectionMessage.Text = "Cuenta autorizada. Cargando las terminales asociadas...";
                await RefreshTerminalsAsync(cancellationToken);
                StatusText.Text = "Cuenta autorizada. Selecciona la terminal de esta caja.";
                return;
            }
        }

        StatusText.Text = "La autorización no terminó todavía. Si ya aceptaste en Mercado Pago, espera unos segundos y pulsa Actualizar terminales.";
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

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "Se eliminarán de JetVenta los tokens cifrados, la cuenta autorizada y las terminales asociadas a esta tienda.\n\nLas ventas, cobros, devoluciones e historial no se eliminarán. Esta acción tampoco revoca el permiso en Mercado Pago; para revocarlo debes hacerlo desde tu cuenta de Mercado Pago.\n\n¿Deseas continuar?",
            "Eliminar datos de Mercado Pago",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            using var response = await ApiClient.Client.PostAsync("api/integrations/mercado-pago/disconnect", null);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            TerminalBox.ItemsSource = null;
            StatusText.Text = "Los datos de Mercado Pago se eliminaron de JetVenta. La información histórica se conservó.";
            await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = "No se pudieron eliminar los datos: " + exception.Message; }
    }

    private async void OnRefreshTerminalsClick(object sender, RoutedEventArgs e) => await RefreshTerminalsAsync();
    private async Task RefreshTerminalsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var terminals = await ApiClient.Client.GetFromJsonAsync<List<TerminalResult>>("api/integrations/mercado-pago/terminals", cancellationToken) ?? [];
            TerminalBox.ItemsSource = terminals;
            TerminalBox.SelectedItem = terminals.FirstOrDefault(item => item.Selected) ?? terminals.FirstOrDefault();
            TerminalHelp.Text = terminals.Count == 0 ? "No se encontraron terminales. Vincúlala a una sucursal y caja en Mercado Pago y activa el modo PDV." : $"{terminals.Count} terminal(es) encontradas. Solo las que indiquen PDV pueden guardarse.";
        }
        catch (Exception exception) { StatusText.Text = "No se pudieron listar terminales: " + exception.Message; }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _oauthPolling?.Cancel();
        _oauthPolling?.Dispose();
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

    private async void OnActivatePdvClick(object sender, RoutedEventArgs e)
    {
        if (TerminalBox.SelectedItem is not TerminalResult terminal) { StatusText.Text = "Selecciona una terminal."; return; }
        if (string.Equals(terminal.OperatingMode, "PDV", StringComparison.OrdinalIgnoreCase)) { StatusText.Text = "La terminal ya está configurada en modo PDV."; return; }
        if (!terminal.Id.StartsWith("NEWLAND_N950__", StringComparison.OrdinalIgnoreCase) && !terminal.Id.StartsWith("PAX_A910__", StringComparison.OrdinalIgnoreCase)) { StatusText.Text = "Este modelo no es compatible con PDV integrado. Mercado Pago solo permite NEWLAND_N950 y PAX_A910."; return; }
        var confirmation = MessageBox.Show("JetVenta solicitará a Mercado Pago activar esta terminal en modo Punto de Venta (PDV). La terminal dejará de operar como terminal independiente.", "Activar modo PDV", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.OK) return;
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/integrations/mercado-pago/terminal/pdv", new { terminalId = terminal.Id });
            StatusText.Text = response.IsSuccessStatusCode ? "Modo PDV solicitado. Reinicia la terminal y actualiza la lista." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) await RefreshTerminalsAsync();
        }
        catch (Exception exception) { StatusText.Text = "No se pudo activar el modo PDV: " + exception.Message; }
    }

    private sealed record SettingsResult(bool Enabled, string Environment, bool AccountConnected, long? AccountUserId, string TerminalId, string TerminalLabel, bool OAuthAvailable, string Message);
    private sealed record TerminalResult(string Id, string Label, string OperatingMode, bool Selected);
    private sealed record OAuthStartResult(string Url);
}
