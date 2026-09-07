using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class CutSettingsWindow : Window
{
    public CutSettingsWindow() { InitializeComponent(); Loaded += OnLoaded; }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<CutSettings>("api/cut-settings") ?? new(true, true, false, 0m, string.Empty);
            CountAndAdjustOption.IsChecked = settings.RequireCashCountOnClose; CloseWithoutCountOption.IsChecked = !settings.RequireCashCountOnClose;
            AutoAdjustBox.IsChecked = settings.AutoAdjustCashDifference; CashLimitEnabledBox.IsChecked = settings.CashLimitEnabled;
            CashLimitTextBox.Text = settings.CashLimit.ToString("0.00", CultureInfo.InvariantCulture); CashLimitMessageTextBox.Text = settings.CashLimitMessage;
            RefreshEnabledState(); StatusText.Text = "La configuración se aplicará en los próximos cierres de turno.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la configuración"); }
    }
    private void OnCloseOptionChanged(object sender, RoutedEventArgs e) => RefreshEnabledState();
    private void OnCashLimitChanged(object sender, RoutedEventArgs e) => RefreshEnabledState();
    private void RefreshEnabledState() { AutoAdjustBox.IsEnabled = CountAndAdjustOption?.IsChecked == true; CashLimitTextBox.IsEnabled = CashLimitEnabledBox?.IsChecked == true; CashLimitMessageTextBox.IsEnabled = CashLimitEnabledBox?.IsChecked == true; }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(CashLimitTextBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var limit) && !decimal.TryParse(CashLimitTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out limit)) { StatusText.Text = "Escribe un límite de efectivo válido."; return; }
        var command = new { requireCashCountOnClose = CountAndAdjustOption.IsChecked == true, autoAdjustCashDifference = AutoAdjustBox.IsChecked == true, cashLimitEnabled = CashLimitEnabledBox.IsChecked == true, cashLimit = limit, cashLimitMessage = CashLimitMessageTextBox.Text };
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/cut-settings", command);
            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadErrorMessageAsync(response);
                StatusText.Text = message;
                MessageBox.Show(message, "Revisa la configuración de corte", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var summary = command.requireCashCountOnClose
                ? $"Se solicitará efectivo contado al cerrar turno y el ajuste automático quedó {(command.autoAdjustCashDifference ? "activado" : "desactivado")}."
                : "El turno cerrará sin solicitar efectivo contado ni registrar ajustes.";
            summary += command.cashLimitEnabled ? $" Límite de efectivo: ${limit:0.00}." : " Sin límite de efectivo configurado.";
            StatusText.Text = summary;
            MessageBox.Show(summary, "Configuración de corte guardada", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar la configuración"); }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors))
            {
                foreach (var property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0)
                    {
                        var detail = property.Value[0].GetString();
                        if (!string.IsNullOrWhiteSpace(detail))
                        {
                            return detail == "Indica un límite de efectivo mayor a cero."
                                ? "Indica un límite de efectivo mayor a cero o desactiva el aviso de límite de efectivo."
                                : detail;
                        }
                    }
                }
            }
            if (root.TryGetProperty("detail", out var detailProperty) && !string.IsNullOrWhiteSpace(detailProperty.GetString())) return detailProperty.GetString()!;
        }
        catch (JsonException) { }

        return response.StatusCode == System.Net.HttpStatusCode.BadRequest
            ? "Revisa los valores de corte antes de guardar."
            : "No se pudo guardar la configuración de corte.";
    }
    private sealed record CutSettings(bool RequireCashCountOnClose, bool AutoAdjustCashDifference, bool CashLimitEnabled, decimal CashLimit, string CashLimitMessage);
}
