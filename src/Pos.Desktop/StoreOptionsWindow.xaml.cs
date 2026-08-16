using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class StoreOptionsWindow : Window
{
    public StoreOptionsWindow() { InitializeComponent(); Loaded += OnLoaded; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<StoreOptions>("/api/store-options");
            if (settings is null) return;
            InventoryEnabledBox.IsChecked = settings.InventoryEnabled;
            CostMethodBox.SelectedItem = CostMethodBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), settings.InventoryCostMethod, StringComparison.OrdinalIgnoreCase)) ?? CostMethodBox.Items[0];
            CreditSalesEnabledBox.IsChecked = settings.CreditSalesEnabled;
            CommonProductsEnabledBox.IsChecked = settings.CommonProductsEnabled;
            AutoPriceBox.IsChecked = settings.AutoPriceWithProfit;
            DefaultProfitBox.Text = settings.DefaultProfitPercent.ToString("0.##", CultureInfo.InvariantCulture);
            RoundAmountsBox.IsChecked = settings.RoundSaleAmounts;
            RoundingModeBox.SelectedItem = RoundingModeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), settings.RoundingMode, StringComparison.OrdinalIgnoreCase)) ?? RoundingModeBox.Items[0];
            NoticeBox.Text = settings.OccasionalNotice;
            NoticeEveryBox.Text = settings.OccasionalNoticeEverySales.ToString(CultureInfo.InvariantCulture);
            RefreshEnabledState();
        }
        catch (Exception exception) { StatusText.Text = $"No se pudieron cargar las opciones: {exception.Message}"; }
    }

    private void OnRoundChanged(object sender, RoutedEventArgs e) => RefreshEnabledState();
    private void RefreshEnabledState() { RoundingModeBox.IsEnabled = RoundAmountsBox.IsChecked == true; }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(DefaultProfitBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var profit) && !decimal.TryParse(DefaultProfitBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out profit)) { StatusText.Text = "Escribe un margen predeterminado válido."; return; }
        if (!int.TryParse(NoticeEveryBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var every)) { StatusText.Text = "Escribe una frecuencia válida para el aviso."; return; }
        var command = new
        {
            inventoryEnabled = InventoryEnabledBox.IsChecked == true,
            inventoryCostMethod = (CostMethodBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "WeightedAverage",
            creditSalesEnabled = CreditSalesEnabledBox.IsChecked == true,
            commonProductsEnabled = CommonProductsEnabledBox.IsChecked == true,
            autoPriceWithProfit = AutoPriceBox.IsChecked == true,
            defaultProfitPercent = profit,
            roundSaleAmounts = RoundAmountsBox.IsChecked == true,
            roundingMode = (RoundingModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Tenths",
            occasionalNotice = NoticeBox.Text,
            occasionalNoticeEverySales = every
        };
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("/api/store-options", command);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            StatusText.Text = "Opciones guardadas correctamente.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudieron guardar las opciones: {exception.Message}"; }
    }

    private sealed record StoreOptions(bool InventoryEnabled, string InventoryCostMethod, bool CreditSalesEnabled, bool CommonProductsEnabled, bool AutoPriceWithProfit, decimal DefaultProfitPercent, bool RoundSaleAmounts, string RoundingMode, string OccasionalNotice, int OccasionalNoticeEverySales);
}
