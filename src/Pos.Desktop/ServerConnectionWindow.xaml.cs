using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class ServerConnectionWindow : Window
{
    public bool RepairRequested { get; private set; }

    public ServerConnectionWindow()
    {
        InitializeComponent();
        var uri = new Uri(ApiClient.BaseUrl);
        HostTextBox.Text = uri.Host;
        PortTextBox.Text = uri.Port.ToString();
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var host, out var port)) return;
        SetBusy(true);
        try { MessageText.Text = (await TestConnectionAsync(host, port)).Message; }
        finally { SetBusy(false); }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var host, out var port)) return;
        SetBusy(true);
        try
        {
            var result = await TestConnectionAsync(host, port);
            MessageText.Text = result.Message;
            if (!result.IsReady)
            {
                MessageBox.Show("La conexión no se guardó porque JetVenta no pudo comprobar el servicio y la base de datos en la dirección indicada. Corrige la conexión o repara los servicios antes de continuar.", "Conexión sin verificar", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApiClient.SetServer(host, port);
            MessageBox.Show("La conexión se verificó y quedó guardada para esta caja.", "Conexión guardada", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        finally { SetBusy(false); }
    }

    private void OnRepairClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var host, out _)) return;
        if (!IsLocalHost(host))
        {
            MessageText.Text = "La reparación automática solo está disponible para el servidor de esta computadora. Para otro equipo, revisa que esté encendido y que JetVenta esté instalado allí.";
            return;
        }

        if (MessageBox.Show(ConnectionHelp.LocalRepairConfirmation, "Reparar servicios", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        RepairRequested = true;
        MessageText.Text = "JetVenta cerrará esta ventana y revisará PostgreSQL, la API y las migraciones. No se borrarán productos ni ventas.";
        DialogResult = true;
    }

    private static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("::1", StringComparison.OrdinalIgnoreCase);

    private static string? TryReadErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<ConnectionTestResult> TestConnectionAsync(string host, int port)
    {
        var previousAddress = ApiClient.BaseUrl;
        try
        {
            ApiClient.SetServer(host, port, persist: false);
            using var health = await ApiClient.Client.GetAsync("health");
            if (!health.IsSuccessStatusCode)
            {
                return new(false, $"El servicio respondió {health.StatusCode}. Revisa la dirección y el puerto.");
            }

            using var setup = await ApiClient.Client.GetAsync("api/setup/status");
            if (setup.IsSuccessStatusCode)
            {
                return new(true, "Conexión correcta: el servicio y la base de datos responden.");
            }

            var errorBody = await setup.Content.ReadAsStringAsync();
            var code = TryReadErrorCode(errorBody);
            return new(false, code switch
            {
                "pending_migrations" => "El servidor responde, pero necesita actualizar la base de datos. Usa Reparar servicios en esta ventana.",
                "database_unavailable" => "El servidor responde, pero su base de datos no está lista. Usa Reparar servicios en esta ventana.",
                _ => "El servidor responde, pero no se pudo consultar la tienda. Usa Reparar servicios o revisa PostgreSQL en el servidor."
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException or ArgumentException)
        {
            return new(false, "No se pudo conectar. Revisa IP, puerto y Firewall de Windows.");
        }
        finally
        {
            ApiClient.SetServerUrl(previousAddress, persist: false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        TestButton.IsEnabled = !isBusy;
        RepairButton.IsEnabled = !isBusy;
        SaveButton.IsEnabled = !isBusy;
    }

    private bool TryRead(out string host, out int port)
    {
        host = HostTextBox.Text.Trim();
        if (!int.TryParse(PortTextBox.Text, out port) || port is < 1 or > 65535)
        {
            MessageText.Text = "Escribe un puerto valido entre 1 y 65535.";
            return false;
        }
        if (host.Length == 0) { MessageText.Text = "Escribe la IP o el nombre del servidor."; return false; }
        return true;
    }

    private sealed record ConnectionTestResult(bool IsReady, string Message);
}
