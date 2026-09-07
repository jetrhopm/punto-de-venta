using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class PurchaseWindow : UserControl
{
    private static HttpClient Client => ApiClient.Client;
    private CancellationTokenSource? _searchCancellation;
    private ProductRow? _product;
    public PurchaseWindow()
    {
        InitializeComponent(); Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken); Loaded += OnLoaded; Unloaded += OnUnloaded;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e) { BarcodeScannerService.BarcodeScanned += OnBarcodeScanned; await LoadSuppliersAsync(); SearchTextBox.Focus(); }
    private void OnUnloaded(object sender, RoutedEventArgs e) => BarcodeScannerService.BarcodeScanned -= OnBarcodeScanned;
    private async Task LoadSuppliersAsync()
    {
        try
        {
            var suppliers = await Client.GetFromJsonAsync<List<SupplierResult>>("/api/suppliers") ?? [];
            SupplierComboBox.ItemsSource = new[] { SupplierResult.None }.Concat(suppliers).ToList();
            SupplierComboBox.SelectedIndex = 0;
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailable; }
    }
    private async void OnCreateSupplierClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewSupplierTextBox.Text)) { MessageText.Text = "Escribe el nombre del proveedor."; return; }
        try { using var response = await Client.PostAsJsonAsync("/api/suppliers", new { name = NewSupplierTextBox.Text.Trim() }); if (!response.IsSuccessStatusCode) { MessageText.Text = await response.Content.ReadAsStringAsync(); return; } NewSupplierTextBox.Clear(); await LoadSuppliersAsync(); MessageText.Text = "Proveedor creado."; }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableNotConfirmed; }
    }
    private async void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel(); _searchCancellation = new CancellationTokenSource(); var token = _searchCancellation.Token; var query = SearchTextBox.Text.Trim(); if (query.Length == 0) { ResultsList.Visibility = Visibility.Collapsed; return; }
        try { await Task.Delay(180, token); var data = await Client.GetFromJsonAsync<List<ProductResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}", token) ?? []; ResultsList.ItemsSource = data.Select(item => new ProductRow(item)).ToList(); ResultsList.Visibility = data.Count == 0 ? Visibility.Collapsed : Visibility.Visible; } catch (OperationCanceledException) { } catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }
    private void OnProductSelected(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ProductRow row) SelectProduct(row);
    }
    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (ResultsList.Items.Count > 0)
            {
                ResultsList.SelectedIndex = e.Key == Key.Down
                    ? Math.Min(ResultsList.SelectedIndex < 0 ? 0 : ResultsList.SelectedIndex + 1, ResultsList.Items.Count - 1)
                    : Math.Max(ResultsList.SelectedIndex <= 0 ? 0 : ResultsList.SelectedIndex - 1, 0);
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        if (ResultsList.SelectedItem is ProductRow row) SelectProduct(row);
        else await SelectSearchResultAsync(SearchTextBox.Text.Trim());
        e.Handled = true;
    }
    private async Task SelectSearchResultAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            var products = await Client.GetFromJsonAsync<List<ProductResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}") ?? [];
            var selected = products.FirstOrDefault(item => string.Equals(item.Code, query, StringComparison.OrdinalIgnoreCase))
                ?? (products.Count == 1 ? products[0] : null);
            if (selected is not null) SelectProduct(new ProductRow(selected));
            else { MessageText.Foreground = System.Windows.Media.Brushes.DarkRed; MessageText.Text = products.Count == 0 ? "No se encontró el producto leído." : "Hay varias coincidencias: selecciónala con flechas y Enter."; }
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }
    private void OnBarcodeScanned(object? sender, string code) => Dispatcher.BeginInvoke(async () => { SearchTextBox.Text = code; await SelectSearchResultAsync(code); });
    private void SelectProduct(ProductRow row)
    {
        _product = row;
        SearchTextBox.Text = row.Product.Code;
        SearchTextBox.SelectAll();
        ShowSelectedProduct(row.Product);
        ResultsList.Visibility = Visibility.Collapsed;
        UnitCostTextBox.Text = row.Product.Cost.ToString("0.00", CultureInfo.CurrentCulture);
        SalePriceTextBox.Text = row.Product.Price.ToString("0.00", CultureInfo.CurrentCulture);
        UpdateMargin();
        QuantityTextBox.Focus();
        QuantityTextBox.SelectAll();
    }
    private void OnPricingChanged(object sender, TextChangedEventArgs e) => UpdateMargin();
    private void UpdateMargin()
    {
        if (!TryParse(UnitCostTextBox.Text, out var cost) || !TryParse(SalePriceTextBox.Text, out var price) || cost <= 0m) { ProfitPercentTextBox.Text = string.Empty; return; }
        ProfitPercentTextBox.Text = (((price - cost) / cost) * 100m).ToString("0.##", CultureInfo.CurrentCulture) + "%";
    }
    private async void OnReceiveClick(object sender, RoutedEventArgs e)
    {
        if (_product is null || !TryParse(QuantityTextBox.Text, out var quantity) || !TryParse(UnitCostTextBox.Text, out var cost) || !TryParse(SalePriceTextBox.Text, out var salePrice)) { MessageText.Text = "Selecciona un producto e indica cantidad, costo y precio de venta."; return; }
        var supplierId = (SupplierComboBox.SelectedItem as SupplierResult)?.Id;
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/purchases/receive", new { operationId = Guid.NewGuid(), supplierId, lines = new[] { new { productId = _product.Product.Id, quantity, unitCost = cost, salePrice } } });
            if (!response.IsSuccessStatusCode) { MessageText.Text = await response.Content.ReadAsStringAsync(); return; }
            var updatedStock = _product.Product.Stock + quantity;
            _product = new ProductRow(_product.Product with { Stock = updatedStock, Cost = cost, Price = salePrice });
            ShowSelectedProduct(_product.Product);
            ResultsList.Visibility = Visibility.Collapsed;
            QuantityTextBox.Clear();
            MessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            MessageText.Text = $"Compra recibida. Existencia actualizada a {updatedStock:0.###} unidades.";
            new OperationResultWindow("Compra registrada", $"La compra se registró para {_product.Product.Description}. Existencia actual: {updatedStock:0.###} {_product.Product.UnitOfMeasure}.", OperationResultKind.Success) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableNotConfirmed; }
    }
    private void ShowSelectedProduct(ProductResult product)
    {
        SelectedProductText.Text = $"{product.Code} | {product.Description}";
        SelectedProductInfoText.Text = $"Precio de venta: ${product.Price:0.00}    |    Costo actual: ${product.Cost:0.00}    |    Existencia: {product.Stock:0.###} {product.UnitOfMeasure}";
    }
    private static bool TryParse(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record SupplierResult(Guid? Id, string Name, string? Phone, string? Email)
    {
        public static SupplierResult None { get; } = new(null, "Sin proveedor", null, null);
        public string DisplayText => Name;
    }
    private sealed record ProductResult(Guid Id, string Code, string Description, decimal Price, decimal Cost, decimal Stock, string UnitOfMeasure, decimal ProfitPercent);
    private sealed record ProductRow(ProductResult Product) { public string DisplayText => $"{Product.Code} | {Product.Description} | Venta ${Product.Price:0.00} | Existencia {Product.Stock:0.###} {Product.UnitOfMeasure}"; }
}
