using System.Net.Http;
using System.Windows;

namespace Pos.Desktop;

public partial class ServerConnectionWindow : Window
{
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
        var previous = new Uri(ApiClient.BaseUrl);
        try
        {
            ApiClient.SetServer(host, port, persist: false);
            using var health = await ApiClient.Client.GetAsync("health");
            if (!health.IsSuccessStatusCode)
            {
                MessageText.Text = $"El servicio respondió {health.StatusCode}. Revisa la dirección y el puerto.";
                return;
            }

            using var setup = await ApiClient.Client.GetAsync("api/setup/status");
            MessageText.Text = setup.IsSuccessStatusCode
                ? "Conexión correcta: el servicio y la base de datos responden."
                : "El servicio responde, pero no se pudo consultar la tienda. Revisa PostgreSQL y las migraciones en el servidor.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            MessageText.Text = "No se pudo conectar. Revisa IP, puerto y Firewall de Windows.";
        }
        finally
        {
            ApiClient.SetServer(previous.Host, previous.Port, persist: false);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var host, out var port)) return;
        ApiClient.SetServer(host, port);
        DialogResult = true;
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
}
