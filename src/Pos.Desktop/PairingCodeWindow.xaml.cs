using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;

namespace Pos.Desktop;

public partial class PairingCodeWindow : Window
{
    public PairingCodeWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await GenerateAsync();
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e) => await GenerateAsync();

    private async Task GenerateAsync()
    {
        ApiClient.ApplySession(SessionContext.AccessToken);
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/lan/pairing-codes", new { });
            if (!response.IsSuccessStatusCode) { CodeText.Text = "Sin permiso"; return; }
            var result = await response.Content.ReadFromJsonAsync<PairingCodeResult>();
            CodeText.Text = result?.Code ?? "------";
        }
        catch (HttpRequestException) { CodeText.Text = "Sin conexion"; }
    }

    private sealed record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);
}
