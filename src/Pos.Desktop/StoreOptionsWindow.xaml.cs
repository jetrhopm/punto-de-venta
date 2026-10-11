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
        LoadShiftClosePrinting();
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<StoreOptions>("/api/store-options");
            if (settings is null) return;
            InventoryEnabledBox.IsChecked = settings.InventoryEnabled;
            BlockSalesWithoutStockBox.IsChecked = settings.BlockSalesWithoutStock;
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
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar las opciones"); }
    }

    private void LoadShiftClosePrinting()
    {
        // Sin impresora no hay nada que imprimir: la opción se muestra desactivada y sin marcar.
        var printerAvailable = ApiClient.IsTicketPrintingAvailable;
        PrintShiftCloseBox.IsEnabled = printerAvailable;
        PrintShiftCloseBox.IsChecked = printerAvailable && ApiClient.PrintShiftCloseTicket;
        PrintShiftCloseHint.Text = printerAvailable
            ? $"Se aplica sólo a esta caja y usa la impresora {ApiClient.PrinterName}. Desmárcala para cerrar turno sin imprimir el ticket del corte; el corte se registra igual."
            : "No hay impresora configurada para esta caja, por lo que el corte no se imprime. Configúrala en Configuración > Impresora para habilitar esta opción.";
    }

    private void OnRoundChanged(object sender, RoutedEventArgs e) => RefreshEnabledState();
    private void OnInventoryChanged(object sender, RoutedEventArgs e) => RefreshEnabledState();
    private void RefreshEnabledState()
    {
        RoundingModeBox.IsEnabled = RoundAmountsBox.IsChecked == true;
        BlockSalesWithoutStockBox.IsEnabled = InventoryEnabledBox.IsChecked == true;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(DefaultProfitBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var profit) && !decimal.TryParse(DefaultProfitBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out profit)) { StatusText.Text = "Escribe un margen predeterminado válido."; return; }
        if (!int.TryParse(NoticeEveryBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var every)) { StatusText.Text = "Escribe una frecuencia válida para el aviso."; return; }
        var command = new
        {
            inventoryEnabled = InventoryEnabledBox.IsChecked == true,
            blockSalesWithoutStock = BlockSalesWithoutStockBox.IsChecked == true,
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
            if (!response.IsSuccessStatusCode) { StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudieron guardar las opciones de la tienda."); return; }
            // Sólo se cambia la preferencia cuando la caja tiene impresora; sin ella la opción no es editable.
            if (ApiClient.IsTicketPrintingAvailable) ApiClient.SetPrintShiftCloseTicket(PrintShiftCloseBox.IsChecked == true);
            ConfigurationFeedback.ShowSavedAndClose(this, "Opciones habilitadas", "Las opciones seleccionadas se aplicarán a las siguientes ventas y productos.");
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron guardar las opciones"); }
    }

    private sealed record StoreOptions(bool InventoryEnabled, bool BlockSalesWithoutStock, string InventoryCostMethod, bool CreditSalesEnabled, bool CommonProductsEnabled, bool AutoPriceWithProfit, decimal DefaultProfitPercent, bool RoundSaleAmounts, string RoundingMode, string OccasionalNotice, int OccasionalNoticeEverySales);
}
