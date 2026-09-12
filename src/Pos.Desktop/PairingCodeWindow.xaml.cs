using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;

namespace Pos.Desktop;

public partial class PairingCodeWindow : Window
{
    private readonly string? _existingCode;
    private readonly string? _description;

    public PairingCodeWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            LoadAddresses();
            await GenerateAsync();
        };
    }

    public PairingCodeWindow(string code, string description)
    {
        _existingCode = code;
        _description = description;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadAddresses();
            CodeText.Text = _existingCode;
            CodeHintText.Text = _description;
            GenerateButton.Visibility = Visibility.Collapsed;
        };
    }

    private void LoadAddresses()
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(network =>
            {
                var properties = network.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork);
                return properties.UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(address.Address))
                .Select(address => new ServerAddress(address.Address.ToString(), $"{address.Address} ({network.Name})", hasGateway));
            })
            .OrderByDescending(address => address.HasGateway)
            .ThenBy(address => address.Address, StringComparer.Ordinal)
            .ToList();

        AddressBox.ItemsSource = addresses;
        AddressBox.SelectedIndex = addresses.Count > 0 ? 0 : -1;
        StatusText.Text = addresses.Count > 0
            ? "Usa la dirección de la red donde está conectada la segunda computadora. El puerto es 5000."
            : "No se encontró una dirección privada. Conecta esta computadora al router y verifica que la red de Windows sea Privada.";
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }

    private void OnRefreshAddressesClick(object sender, RoutedEventArgs e) => LoadAddresses();

    private void OnAddressChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AddressBox.SelectedItem is ServerAddress address)
            StatusText.Text = $"En la segunda computadora escribe {address.Address} como dirección del servidor y conserva el puerto 5000.";
    }

    private void OnCopyAddressClick(object sender, RoutedEventArgs e)
    {
        if (AddressBox.SelectedItem is not ServerAddress address)
        {
            OperationFeedback.Show(this, "No hay dirección disponible", "Conecta la caja principal a una red privada y vuelve a actualizar las direcciones.", OperationResultKind.Warning);
            return;
        }
        Clipboard.SetDataObject(address.Address, true);
        StatusText.Text = $"Se copió {address.Address}.";
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e) => await GenerateAsync();

    private async Task GenerateAsync()
    {
        if (!InstallationRoleContext.IsServer)
        {
            GenerateButton.Visibility = Visibility.Collapsed;
            CodeHintText.Text = "El código debe generarse desde la caja principal / servidor.";
            StatusText.Text = "Esta computadora es una caja adicional; usa la configuración del servidor para agregar otra caja.";
            return;
        }

        ApiClient.ApplySession(SessionContext.AccessToken);
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/lan/pairing-codes", new { });
            if (!response.IsSuccessStatusCode)
            {
                var message = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo generar el código de emparejamiento.");
                StatusText.Text = message;
                OperationFeedback.Show(this, "Código no generado", message, OperationResultKind.Error);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<PairingCodeResult>();
            CodeText.Text = result?.Code ?? "------";
            CodeHintText.Text = result is null ? "No se recibió un código válido." : $"Vence a las {result.ExpiresAtUtc.ToLocalTime():HH:mm} y sólo se puede usar una vez.";
            StatusText.Text = result is null ? "Intenta generar el código nuevamente." : "Código listo. Continúa con la instalación de la segunda computadora.";
        }
        catch (HttpRequestException)
        {
            const string message = "JetVenta no respondió. Revisa que esta caja principal tenga la API activa.";
            StatusText.Text = message;
            OperationFeedback.Show(this, "Código no generado", message, OperationResultKind.Error);
        }
    }

    private sealed record ServerAddress(string Address, string Display, bool HasGateway);
    private sealed record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);
}
