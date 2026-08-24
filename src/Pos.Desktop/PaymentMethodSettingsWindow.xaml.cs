using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class PaymentMethodSettingsWindow : Window
{
    public PaymentMethodSettingsWindow() { InitializeComponent(); Loaded += OnLoaded; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<PaymentSettings>("api/payment-method-settings");
            CashBox.IsChecked = settings?.CashEnabled ?? true; CardBox.IsChecked = settings?.CardEnabled ?? true;
            TransferBox.IsChecked = settings?.TransferEnabled ?? true; CreditBox.IsChecked = settings?.CreditEnabled ?? true;
            StatusText.Text = "Los cambios se aplican a las siguientes ventas.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la configuración"); }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var command = new { cashEnabled = CashBox.IsChecked == true, cardEnabled = CardBox.IsChecked == true, transferEnabled = TransferBox.IsChecked == true, creditEnabled = CreditBox.IsChecked == true };
        if (!command.cashEnabled && !command.cardEnabled && !command.transferEnabled && !command.creditEnabled) { StatusText.Text = "Activa al menos una forma de pago."; return; }
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/payment-method-settings", command);
            StatusText.Text = response.IsSuccessStatusCode ? "Formas de pago guardadas correctamente." : await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar la configuración"); }
    }

    private sealed record PaymentSettings(bool CashEnabled, bool CardEnabled, bool TransferEnabled, bool CreditEnabled);
}
