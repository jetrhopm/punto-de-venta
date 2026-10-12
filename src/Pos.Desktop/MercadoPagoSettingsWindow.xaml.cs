using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

// Configuración de Mercado Pago Point para el comercio: 1) conectar la cuenta con
// OAuth (sin claves ni tokens) y 2) elegir la terminal de esta caja. Activar el modo
// PDV, guardar la terminal y habilitar el cobro se hacen con un solo botón.
public partial class MercadoPagoSettingsWindow : Window
{
    private const string HelpUrl = "https://www.mercadopago.com.mx/blog/configurar-sucursales-terminal-point";
    private SettingsResult? _settings;
    private CancellationTokenSource? _oauthPolling;
    private bool _busy;

    public MercadoPagoSettingsWindow() { InitializeComponent(); Loaded += async (_, _) => await LoadAsync(); }

    private async Task LoadAsync()
    {
        try
        {
            _settings = await ApiClient.Client.GetFromJsonAsync<SettingsResult>("api/integrations/mercado-pago/settings");
            if (_settings is null) return;
            var registerName = FriendlyRegisterName(_settings.RegisterName);
            HeaderText.Text = $"Conecta la cuenta donde recibes tus ventas y elige la terminal de {registerName}.";
            TestTokenPanel.Visibility = _settings.TestTokenAllowed ? Visibility.Visible : Visibility.Collapsed;

            if (_settings.AccountConnected)
            {
                ConnectionTitle.Text = _settings.Environment == "Test" ? "Cuenta de prueba conectada" : "Cuenta de Mercado Pago conectada";
                ConnectionMessage.Text = _settings.AccountUserId is long userId
                    ? $"Los cobros con tarjeta se depositan en la cuenta {userId}. La cuenta aplica a todas las cajas de la tienda."
                    : "La cuenta aplica a todas las cajas de la tienda.";
                AccountStepText.Text = "✓";
                AccountStep.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x13, 0x73, 0x33));
                AuthorizeButton.Content = "Cambiar de cuenta";
                AuthorizeButton.Style = (Style)FindResource("LinkButton");
                DisconnectButton.Visibility = Visibility.Visible;
                TerminalPanel.IsEnabled = true;
                TerminalPanel.Opacity = 1;
                await RefreshTerminalsAsync();
            }
            else
            {
                ConnectionTitle.Text = "Conecta tu cuenta de Mercado Pago";
                ConnectionMessage.Text = "Inicia sesión con la cuenta donde quieres recibir los cobros con tarjeta. No necesitas copiar claves.";
                AccountStepText.Text = "1";
                AccountStep.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x9E, 0xE3));
                AuthorizeButton.Content = "Conectar con Mercado Pago";
                AuthorizeButton.Style = (Style)FindResource("PrimaryButton");
                DisconnectButton.Visibility = Visibility.Collapsed;
                TerminalPanel.IsEnabled = false;
                TerminalPanel.Opacity = 0.55;
                TerminalBox.ItemsSource = null;
                TerminalHelp.Text = "Primero conecta tu cuenta.";
                ActiveBanner.Visibility = Visibility.Collapsed;
                EnabledButton.Visibility = Visibility.Collapsed;
            }

            AuthorizeButton.IsEnabled = _settings.OAuthAvailable;
            if (!_settings.OAuthAvailable) StatusText.Text = "La conexión con Mercado Pago no está disponible en este servidor. Contacta a soporte de JetVenta.";
            UpdateActiveState();
        }
        catch (Exception exception)
        {
            // Sin datos de la conexión no se sabe si la cuenta está conectada: no se deja conectar a ciegas.
            ConnectionTitle.Text = "No se pudo revisar la conexión";
            ConnectionMessage.Text = "Cierra esta ventana y vuelve a abrirla. Si sigue igual, reinicia JetVenta en la caja principal.";
            AuthorizeButton.IsEnabled = false;
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo consultar Mercado Pago");
        }
    }

    // ---------- Paso 1: cuenta ----------

    private async void OnAuthorizeClick(object sender, RoutedEventArgs e)
    {
        _oauthPolling?.Cancel();
        _oauthPolling = new CancellationTokenSource();
        var cancellationToken = _oauthPolling.Token;
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/integrations/mercado-pago/oauth/start", null);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo abrir Mercado Pago.");
                OperationFeedback.Show(this, "Mercado Pago", StatusText.Text, OperationResultKind.Error);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<OAuthStartResult>();
            if (result is not null) Process.Start(new ProcessStartInfo(result.Url) { UseShellExecute = true });
            AuthorizeButton.IsEnabled = false;
            StatusText.Text = "Se abrió Mercado Pago en tu navegador. Inicia sesión y pulsa Permitir; JetVenta continuará solo.";
            await WaitForOAuthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo abrir Mercado Pago");
            OperationFeedback.Show(this, "Mercado Pago", StatusText.Text, OperationResultKind.Error);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) AuthorizeButton.IsEnabled = _settings?.OAuthAvailable ?? true;
        }
    }

    // El servidor de la tienda recoge la autorización del intermediario una sola vez.
    // El intento vence a los 10 minutos.
    private async Task WaitForOAuthAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            HttpResponseMessage response;
            try { response = await ApiClient.Client.GetAsync("api/integrations/mercado-pago/oauth/status", cancellationToken); }
            catch (HttpRequestException) { StatusText.Text = "Sin conexión con el servidor de JetVenta. Se volverá a intentar."; continue; }
            using var _ = response;
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "Esperando la confirmación de Mercado Pago...");
                continue;
            }
            var status = await response.Content.ReadFromJsonAsync<OAuthStatusResult>(cancellationToken);
            switch (status?.Status)
            {
                case "pending":
                    continue;
                case "completed":
                    StatusText.Text = "Cuenta conectada. Ahora elige la terminal de esta caja.";
                    await LoadAsync();
                    return;
                default:
                    StatusText.Text = status?.Message ?? "La conexión no se completó. Vuelve a intentarlo.";
                    OperationFeedback.Show(this, "Mercado Pago", StatusText.Text, OperationResultKind.Warning);
                    return;
            }
        }

        StatusText.Text = "Se agotó el tiempo para autorizar. Pulsa Conectar con Mercado Pago para intentarlo de nuevo.";
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "JetVenta dejará de cobrar con terminal en todas las cajas de esta tienda y olvidará la cuenta conectada.\n\nTus ventas y cobros anteriores no se borran. Para retirar por completo el permiso, hazlo también desde tu cuenta de Mercado Pago.\n\n¿Deseas desconectar la cuenta?",
            "Desconectar Mercado Pago", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/integrations/mercado-pago/disconnect", null);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo desconectar la cuenta.");
                OperationFeedback.Show(this, "Mercado Pago", StatusText.Text, OperationResultKind.Error);
                return;
            }
            StatusText.Text = "Cuenta desconectada.";
            await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo desconectar la cuenta"); }
    }

    // ---------- Paso 2: terminal ----------

    private async void OnRefreshTerminalsClick(object sender, RoutedEventArgs e) => await RefreshTerminalsAsync();

    private async Task<IReadOnlyList<TerminalResult>> RefreshTerminalsAsync()
    {
        try
        {
            var terminals = await ApiClient.Client.GetFromJsonAsync<List<TerminalResult>>("api/integrations/mercado-pago/terminals") ?? [];
            var previous = (TerminalBox.SelectedItem as TerminalResult)?.Id;
            TerminalBox.ItemsSource = terminals;
            TerminalBox.SelectedItem = terminals.FirstOrDefault(item => item.Id == previous)
                ?? terminals.FirstOrDefault(item => item.Selected)
                ?? terminals.FirstOrDefault(item => item.IsPdv)
                ?? terminals.FirstOrDefault();
            TerminalHelp.Text = terminals.Count switch
            {
                0 => "No encontramos terminales en tu cuenta. Vincula la terminal desde la app de Mercado Pago (Tu negocio > Point) y asígnala a una sucursal y caja; después pulsa Actualizar lista.",
                1 => "Encontramos tu terminal. Pulsa Usar esta terminal.",
                _ => $"Encontramos {terminals.Count} terminales. Elige la que está junto a esta caja; el número de serie está en la etiqueta de la terminal."
            };
            UseTerminalButton.IsEnabled = terminals.Count > 0;
            UpdateActiveState();
            return terminals;
        }
        catch (Exception exception)
        {
            TerminalHelp.Text = ConnectionHelp.FromException(exception, "No se pudieron consultar las terminales");
            UseTerminalButton.IsEnabled = false;
            return [];
        }
    }

    private void OnTerminalSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActiveState();

    // Un solo botón: activa el modo PDV si hace falta, guarda la terminal y habilita el cobro.
    private async void OnUseTerminalClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (TerminalBox.SelectedItem is not TerminalResult terminal)
        {
            StatusText.Text = "Elige una terminal.";
            return;
        }
        _busy = true;
        UseTerminalButton.IsEnabled = false;
        try
        {
            if (!terminal.IsPdv)
            {
                var confirmation = MessageBox.Show(
                    "Para cobrar desde JetVenta, la terminal debe pasar a modo punto de venta (PDV).\n\nLa terminal se reiniciará y, a partir de entonces, los cobros se iniciarán desde JetVenta.\n\n¿Continuar?",
                    "Preparar terminal", MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (confirmation != MessageBoxResult.OK) return;

                using (var activate = await ApiClient.Client.PostAsJsonAsync("api/integrations/mercado-pago/terminal/pdv", new { terminalId = terminal.Id }))
                {
                    if (!activate.IsSuccessStatusCode)
                    {
                        StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(activate, "Mercado Pago no pudo preparar la terminal.");
                        OperationFeedback.Show(this, "Preparar terminal", StatusText.Text, OperationResultKind.Error);
                        return;
                    }
                }

                // Mercado Pago confirma el cambio cuando la terminal termina de reiniciarse.
                TerminalResult? ready = null;
                for (var attempt = 1; attempt <= 18 && ready is null; attempt++)
                {
                    StatusText.Text = "Preparando la terminal. Si no se reinicia sola, apágala y enciéndela...";
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    ready = (await RefreshTerminalsAsync()).FirstOrDefault(item => item.Id == terminal.Id && item.IsPdv);
                }
                if (ready is null)
                {
                    StatusText.Text = "La terminal aún no confirma el cambio. Cuando termine de reiniciarse, pulsa Usar esta terminal otra vez.";
                    OperationFeedback.Show(this, "Terminal en preparación", StatusText.Text, OperationResultKind.Information);
                    return;
                }
                terminal = ready;
            }

            using var save = await ApiClient.Client.PutAsJsonAsync("api/integrations/mercado-pago/terminal", new { terminalId = terminal.Id, label = terminal.DisplayName });
            if (!save.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(save, "No se pudo guardar la terminal.");
                OperationFeedback.Show(this, "Mercado Pago", StatusText.Text, OperationResultKind.Error);
                return;
            }
            StatusText.Text = string.Empty;
            await LoadAsync();
            OperationFeedback.Show(this, "Listo", "Esta caja ya cobra con tarjeta usando la terminal Mercado Pago. Al cobrar, elige Tarjeta y el importe aparecerá en la terminal.", OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo configurar la terminal");
            OperationFeedback.Show(this, "Mercado Pago", StatusText.Text, OperationResultKind.Error);
        }
        finally
        {
            _busy = false;
            UseTerminalButton.IsEnabled = TerminalBox.Items.Count > 0;
        }
    }

    private async void OnEnabledClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        var enable = !_settings.Enabled;
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/integrations/mercado-pago/enabled", new { enabled = enable });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo cambiar el cobro con terminal.");
                OperationFeedback.Show(this, "Mercado Pago", StatusText.Text, OperationResultKind.Error);
                return;
            }
            StatusText.Text = enable ? "Cobro con terminal reanudado en esta caja." : "Cobro con terminal en pausa en esta caja. La cuenta y la terminal se conservan.";
            await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cambiar el cobro con terminal"); }
    }

    private void UpdateActiveState()
    {
        if (_settings is null || !_settings.AccountConnected || string.IsNullOrWhiteSpace(_settings.TerminalId))
        {
            ActiveBanner.Visibility = Visibility.Collapsed;
            EnabledButton.Visibility = Visibility.Collapsed;
            UseTerminalButton.Content = "Usar esta terminal";
            return;
        }
        var saved = (TerminalBox.ItemsSource as IEnumerable<TerminalResult>)?.FirstOrDefault(item => item.Id == _settings.TerminalId);
        var name = saved?.DisplayName ?? TerminalResult.FriendlyName(_settings.TerminalId);
        ActiveBanner.Visibility = Visibility.Visible;
        ActiveText.Text = _settings.Enabled ? $"Esta caja cobra con {name}." : $"Cobro con {name} en pausa.";
        EnabledButton.Visibility = Visibility.Visible;
        EnabledButton.Content = _settings.Enabled ? "Pausar cobro con terminal" : "Reanudar cobro con terminal";
        var sameTerminal = (TerminalBox.SelectedItem as TerminalResult)?.Id == _settings.TerminalId;
        UseTerminalButton.Content = sameTerminal ? "Volver a guardar esta terminal" : "Usar esta terminal";
    }

    // ---------- Desarrollo (oculto en producción) ----------

    private async void OnSaveTestTokenClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TestTokenBox.Password)) { StatusText.Text = "Escribe el Access Token de prueba."; return; }
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/integrations/mercado-pago/test-token", new { accessToken = TestTokenBox.Password });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo validar el token de prueba.");
                return;
            }
            TestTokenBox.Clear();
            await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo validar el token de prueba"); }
    }

    // ---------- General ----------

    private void OnOpenHelpClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(HelpUrl) { UseShellExecute = true }); }
        catch (Exception exception) { StatusText.Text = "No se pudo abrir la ayuda: " + exception.Message; }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _oauthPolling?.Cancel();
        _oauthPolling?.Dispose();
    }

    // Algunas cajas conservan como nombre su identificador interno; no se muestra al comercio.
    private static string FriendlyRegisterName(string name) =>
        string.IsNullOrWhiteSpace(name) || Regex.IsMatch(name, @"[0-9a-fA-F]{12,}") ? "esta caja" : name;

    private sealed record SettingsResult(bool Enabled, string Environment, bool AccountConnected, long? AccountUserId, string TerminalId, string TerminalLabel, bool OAuthAvailable, bool WebhookConfigured, string RegisterName, string Message, bool TestTokenAllowed);
    private sealed record OAuthStatusResult(string Status, string Message);
    private sealed record OAuthStartResult(string Url);

    private sealed record TerminalResult(string Id, string Label, string OperatingMode, bool Selected)
    {
        public bool IsPdv => string.Equals(OperatingMode, "PDV", StringComparison.OrdinalIgnoreCase);
        public string DisplayName => FriendlyName(Id) + (IsPdv ? string.Empty : "  (se preparará al usarla)");

        // "NEWLAND_N950__N950NCB801293324" -> "Point Smart · serie N950NCB801293324"
        public static string FriendlyName(string id)
        {
            var parts = id.Split("__", 2);
            if (parts.Length != 2) return id;
            var model = parts[0].ToUpperInvariant() switch
            {
                "NEWLAND_N950" => "Point Smart",
                "PAX_A910" => "Point PAX A910",
                var other => other.Replace('_', ' ')
            };
            return $"{model} · serie {parts[1]}";
        }
    }
}
