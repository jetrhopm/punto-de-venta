using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;

namespace Pos.Desktop;

public partial class JoinServerWindow : Window
{
    public JoinServerWindow()
    {
        InitializeComponent();
        DeviceBox.Text = Environment.MachineName;
    }

    private async void OnPairClick(object sender, RoutedEventArgs e)
    {
        if (CodeBox.Text.Trim().Length != 6 || string.IsNullOrWhiteSpace(DeviceBox.Text) || string.IsNullOrWhiteSpace(RegisterBox.Text)) { MessageText.Text = "Completa el codigo, equipo y caja."; return; }
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/lan/pair", new { code = CodeBox.Text.Trim(), deviceName = DeviceBox.Text.Trim(), registerName = RegisterBox.Text.Trim() });
            if (!response.IsSuccessStatusCode) { MessageText.Text = await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<PairResult>();
            if (result is null) { MessageText.Text = "El servidor no devolvio la identidad de la caja."; return; }
            ApiClient.SaveDeviceIdentity(result.DeviceId, result.StoreId, result.RegisterId, result.DeviceToken);
            MessageText.Text = $"Caja emparejada correctamente como {result.RegisterName}.";
            DialogResult = true;
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private sealed record PairResult(Guid DeviceId, Guid StoreId, Guid RegisterId, string DeviceToken, string RegisterName);
}
