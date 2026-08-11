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
        try
        {
            ApiClient.SetServer(host, port);
            using var response = await ApiClient.Client.GetAsync("health");
            MessageText.Text = response.IsSuccessStatusCode ? "Conexion correcta con el servidor." : $"El servidor respondio {response.StatusCode}.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            MessageText.Text = "No se pudo conectar. Revisa IP, puerto y Firewall de Windows.";
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
