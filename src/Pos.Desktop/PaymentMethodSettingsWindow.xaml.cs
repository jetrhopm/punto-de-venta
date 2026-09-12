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
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la configuración");
            OperationFeedback.Show(this, "Formas de pago no disponibles", StatusText.Text, OperationResultKind.Error);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var command = new { cashEnabled = CashBox.IsChecked == true, cardEnabled = CardBox.IsChecked == true, transferEnabled = TransferBox.IsChecked == true, creditEnabled = CreditBox.IsChecked == true };
        if (!command.cashEnabled && !command.cardEnabled && !command.transferEnabled && !command.creditEnabled)
        {
            const string message = "Activa al menos una forma de pago para poder cobrar ventas.";
            StatusText.Text = message;
            OperationFeedback.Show(this, "Selecciona una forma de pago", message, OperationResultKind.Warning);
            return;
        }
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/payment-method-settings", command);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudieron guardar las formas de pago.");
                OperationFeedback.Show(this, "Formas de pago no guardadas", StatusText.Text, OperationResultKind.Error);
                return;
            }
            ConfigurationFeedback.ShowSavedAndClose(this, "Formas de pago", "Las siguientes ventas mostrarán únicamente las formas de pago activas.");
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar la configuración");
            OperationFeedback.Show(this, "Formas de pago no guardadas", StatusText.Text, OperationResultKind.Error);
        }
    }

    private sealed record PaymentSettings(bool CashEnabled, bool CardEnabled, bool TransferEnabled, bool CreditEnabled);
}
