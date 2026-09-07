using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class InventoryAdjustmentWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private CancellationTokenSource? _searchCancellation;
    private ProductRow? _selected;

    public InventoryAdjustmentWindow()
    {
        InitializeComponent();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        Closed += (_, _) => _searchCancellation?.Cancel();
    }

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _selected = null;
        SelectedProductText.Text = "Selecciona un producto de la lista.";
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0) { ResultsList.Visibility = Visibility.Collapsed; return; }
        try
        {
            await Task.Delay(180, cancellationToken);
            var results = await Client.GetFromJsonAsync<List<ProductResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}", cancellationToken) ?? [];
            ResultsList.ItemsSource = results.Select(item => new ProductRow(item)).ToList();
            ResultsList.Visibility = results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(SearchTextBox.Text)) return;
        _ = SelectExactBarcodeAsync(e);
    }

    private async Task SelectExactBarcodeAsync(KeyEventArgs e)
    {
        try
        {
            var query = SearchTextBox.Text.Trim();
            var results = await Client.GetFromJsonAsync<List<ProductResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}") ?? [];
            var exact = results.FirstOrDefault(item => string.Equals(item.Code.Trim(), query, StringComparison.OrdinalIgnoreCase));
            if (exact is null)
            {
                MessageText.Text = "No se encontró un código exacto. Revisa la lectura o usa las flechas para elegir una coincidencia.";
                return;
            }

            SelectProduct(new ProductRow(exact));
            e.Handled = true;
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableRetry; }
        catch (OperationCanceledException) { }
    }

    private void OnProductSelected(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ProductRow row) SelectProduct(row);
    }

    private void SelectProduct(ProductRow row)
    {
        _selected = row;
        SelectedProductText.Text = $"{row.Product.Code} | {row.Product.Description} | Existencia actual: {row.Product.Stock:0.###}";
        ResultsList.Visibility = Visibility.Collapsed;
        QuantityTextBox.Focus();
        QuantityTextBox.SelectAll();
    }

    private async void OnAdjustClick(object sender, RoutedEventArgs e)
    {
        var reason = ReasonTextBox.Text.Trim();
        if (_selected is null || !TryParseDecimal(QuantityTextBox.Text, out var quantity) || quantity == 0m || reason.Length == 0)
        {
            MessageText.Text = "Selecciona producto, indica una cantidad distinta de cero y escribe un motivo.";
            return;
        }
        if (reason.Length > 80)
        {
            MessageText.Text = $"El motivo no puede superar 80 caracteres. Actualmente tiene {reason.Length}.";
            return;
        }
        try
        {
            var command = new { operationId = Guid.NewGuid(), productId = _selected.Product.Id, quantity, reason };
            using var response = await Client.PostAsJsonAsync("/api/inventory/adjust", command);
            if (!response.IsSuccessStatusCode) { MessageText.Text = await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<AdjustmentResult>();
            MessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            MessageText.Text = result is null ? "Ajuste registrado." : $"Ajuste registrado. Existencia: {result.StockBefore:0.###} -> {result.StockAfter:0.###}";
            QuantityTextBox.Clear(); ReasonTextBox.Clear();
        }
        catch (OperationCanceledException) { MessageText.Text = "La conexión tardó demasiado. No se confirmó el ajuste."; }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableNotConfirmed; }
    }

    private static bool TryParseDecimal(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record ProductResult(Guid Id, string Code, string Description, decimal Price, decimal Stock);
    private sealed record ProductRow(ProductResult Product) { public string DisplayText => $"{Product.Code} | {Product.Description} | Venta ${Product.Price:0.00} | Existencia {Product.Stock:0.###}"; }
    private sealed record AdjustmentResult(Guid MovementId, Guid ProductId, decimal Quantity, decimal StockBefore, decimal StockAfter, string Reason);
}
