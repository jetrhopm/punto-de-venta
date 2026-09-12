using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;

namespace Pos.Desktop;

public partial class PairingCodeWindow : Window
{
    private readonly string? _existingCode;
    private readonly string? _description;
    public PairingCodeWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await GenerateAsync();
    }
    public PairingCodeWindow(string code, string description)
    {
        _existingCode = code;
        _description = description;
        InitializeComponent();
        Loaded += (_, _) => { CodeText.Text = _existingCode; DescriptionText.Text = _description; GenerateButton.Visibility = Visibility.Collapsed; };
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
