using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;

namespace Pos.Desktop;

public partial class MobilePairingWindow : Window
{
    public MobilePairingWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadAddresses();
    }

    private void LoadAddresses()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses.Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(address.Address)).Select(address => new ServerAddress(address.Address.ToString(), $"{address.Address} ({network.Name})")))
            .OrderBy(item => item.Address, StringComparer.Ordinal).ToList();
        AddressBox.ItemsSource = addresses;
        AddressBox.SelectedIndex = addresses.Count > 0 ? 0 : -1;
        StatusText.Text = addresses.Count > 0 ? "Genera un código cuando tengas listo el teléfono." : "Conecta el servidor a una red privada para mostrar una dirección válida.";
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        ApiClient.ApplySession(SessionContext.AccessToken);
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/mobile/pairing-codes", null);
            if (!response.IsSuccessStatusCode)
            {
                var message = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo generar el código del teléfono.");
                StatusText.Text = message;
                OperationFeedback.Show(this, "Código no generado", message, OperationResultKind.Error);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<PairingCodeResult>();
            CodeText.Text = result?.Code ?? "------";
            CodeHintText.Text = result is null ? "No se recibió un código válido." : $"Vence a las {result.ExpiresAtUtc.ToLocalTime():HH:mm} y se usa una sola vez.";
            StatusText.Text = "Código listo. Escribe la dirección y este código en JetVenta Móvil.";
        }
        catch (HttpRequestException)
        {
            const string message = "JetVenta no respondió. Revisa que la API del servidor esté activa.";
            StatusText.Text = message;
            OperationFeedback.Show(this, "Código no generado", message, OperationResultKind.Error);
        }
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }

    private sealed record ServerAddress(string Address, string Display);
    private sealed record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);
}
