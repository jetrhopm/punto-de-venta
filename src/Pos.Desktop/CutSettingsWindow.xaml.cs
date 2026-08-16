using System.Globalization;
using System.Net.Http.Json;
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
        catch (Exception exception) { StatusText.Text = $"No se pudo cargar la configuración: {exception.Message}"; }
    }
    private void OnCloseOptionChanged(object sender, RoutedEventArgs e) => RefreshEnabledState();
    private void OnCashLimitChanged(object sender, RoutedEventArgs e) => RefreshEnabledState();
    private void RefreshEnabledState() { AutoAdjustBox.IsEnabled = CountAndAdjustOption?.IsChecked == true; CashLimitTextBox.IsEnabled = CashLimitEnabledBox?.IsChecked == true; CashLimitMessageTextBox.IsEnabled = CashLimitEnabledBox?.IsChecked == true; }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(CashLimitTextBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var limit) && !decimal.TryParse(CashLimitTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out limit)) { StatusText.Text = "Escribe un límite de efectivo válido."; return; }
        var command = new { requireCashCountOnClose = CountAndAdjustOption.IsChecked == true, autoAdjustCashDifference = AutoAdjustBox.IsChecked == true, cashLimitEnabled = CashLimitEnabledBox.IsChecked == true, cashLimit = limit, cashLimitMessage = CashLimitMessageTextBox.Text };
        try { using var response = await ApiClient.Client.PutAsJsonAsync("api/cut-settings", command); StatusText.Text = response.IsSuccessStatusCode ? "Configuración de corte guardada correctamente." : await response.Content.ReadAsStringAsync(); }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar la configuración: {exception.Message}"; }
    }
    private sealed record CutSettings(bool RequireCashCountOnClose, bool AutoAdjustCashDifference, bool CashLimitEnabled, decimal CashLimit, string CashLimitMessage);
}
