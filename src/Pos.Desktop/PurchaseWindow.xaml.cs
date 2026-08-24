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
        InitializeComponent(); Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken); Loaded += async (_, _) => await LoadSuppliersAsync();
    }
    private async Task LoadSuppliersAsync()
    {
        try { SupplierComboBox.ItemsSource = await Client.GetFromJsonAsync<List<SupplierResult>>("/api/suppliers") ?? []; SupplierComboBox.SelectedIndex = 0; }
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
        if (ResultsList.SelectedItem is not ProductRow row) return;
        _product = row;
        SearchTextBox.Text = row.Product.Code;
        SearchTextBox.SelectAll();
        ShowSelectedProduct(row.Product);
        ResultsList.Visibility = Visibility.Collapsed;
        UnitCostTextBox.Text = row.Product.Cost.ToString("0.00", CultureInfo.CurrentCulture);
        QuantityTextBox.Focus();
        QuantityTextBox.SelectAll();
    }
    private async void OnReceiveClick(object sender, RoutedEventArgs e)
    {
        if (SupplierComboBox.SelectedItem is not SupplierResult supplier || _product is null || !TryParse(QuantityTextBox.Text, out var quantity) || !TryParse(UnitCostTextBox.Text, out var cost)) { MessageText.Text = "Selecciona proveedor y producto, e indica cantidad y costo."; return; }
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/purchases/receive", new { operationId = Guid.NewGuid(), supplierId = supplier.Id, lines = new[] { new { productId = _product.Product.Id, quantity, unitCost = cost } } });
            if (!response.IsSuccessStatusCode) { MessageText.Text = await response.Content.ReadAsStringAsync(); return; }
            var updatedStock = _product.Product.Stock + quantity;
            _product = new ProductRow(_product.Product with { Stock = updatedStock, Cost = cost });
            ShowSelectedProduct(_product.Product);
            ResultsList.Visibility = Visibility.Collapsed;
            QuantityTextBox.Clear();
            MessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            MessageText.Text = $"Compra recibida. Existencia actualizada a {updatedStock:0.###} unidades.";
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableNotConfirmed; }
    }
    private void ShowSelectedProduct(ProductResult product)
    {
        SelectedProductText.Text = $"{product.Code} | {product.Description}";
        SelectedProductInfoText.Text = $"Precio de venta: ${product.Price:0.00}    |    Costo actual: ${product.Cost:0.00}    |    Existencia: {product.Stock:0.###} {product.UnitOfMeasure}";
    }
    private static bool TryParse(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record SupplierResult(Guid Id, string Name, string? Phone, string? Email) { public string DisplayText => Name; }
    private sealed record ProductResult(Guid Id, string Code, string Description, decimal Price, decimal Cost, decimal Stock, string UnitOfMeasure);
    private sealed record ProductRow(ProductResult Product) { public string DisplayText => $"{Product.Code} | {Product.Description} | Venta ${Product.Price:0.00} | Existencia {Product.Stock:0.###} {Product.UnitOfMeasure}"; }
}
