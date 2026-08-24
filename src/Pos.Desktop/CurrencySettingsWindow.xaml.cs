using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class CurrencySettingsWindow : Window
{
    public CurrencySettingsWindow() { InitializeComponent(); Loaded += OnLoaded; SymbolBox.TextChanged += OnSymbolChanged; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<CurrencySettings>("api/currency-settings");
            SymbolBox.Text = string.IsNullOrWhiteSpace(settings?.CurrencySymbol) ? "$" : settings.CurrencySymbol;
            StatusText.Text = "El símbolo se aplicará a los nuevos tickets y comprobantes.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la configuración"); }
    }

    private void OnSymbolChanged(object sender, TextChangedEventArgs e) => PreviewText.Text = $"{(string.IsNullOrWhiteSpace(SymbolBox.Text) ? "$" : SymbolBox.Text.Trim())}123.45";

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var symbol = SymbolBox.Text.Trim();
        if (symbol.Length is < 1 or > 5) { StatusText.Text = "Escribe un símbolo de uno a cinco caracteres."; return; }
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/currency-settings", new { currencySymbol = symbol });
            StatusText.Text = response.IsSuccessStatusCode ? "Símbolo de moneda guardado correctamente." : await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar el símbolo"); }
    }

    private sealed record CurrencySettings(string CurrencySymbol);
}
