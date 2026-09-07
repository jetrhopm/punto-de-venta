using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class ProductLookupWindow : Window
{
    private CancellationTokenSource? _searchCancellation;
    public ProductLookupSelection? SelectedProduct { get; private set; }

    public ProductLookupWindow(string initialQuery = "")
    {
        InitializeComponent();
        SearchBox.Text = initialQuery;
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
        Closed += (_, _) => _searchCancellation?.Cancel();
    }

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            ProductsGrid.ItemsSource = null;
            StatusText.Text = "Escribe un código, descripción o palabra parcial.";
            return;
        }

        try
        {
            await Task.Delay(180, token);
            var products = await ApiClient.Client.GetFromJsonAsync<List<ProductLookupResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}", token) ?? [];
            ProductsGrid.ItemsSource = products.Select(product => new ProductLookupRow(product)).ToList();
            ProductsGrid.SelectedIndex = products.Count > 0 ? 0 : -1;
            StatusText.Text = products.Count == 0 ? "No se encontraron productos." : $"{products.Count} producto(s) encontrados.";
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up)
        {
            if (ProductsGrid.Items.Count > 0)
            {
                ProductsGrid.SelectedIndex = e.Key == Key.Down
                    ? Math.Min(ProductsGrid.SelectedIndex < 0 ? 0 : ProductsGrid.SelectedIndex + 1, ProductsGrid.Items.Count - 1)
                    : Math.Max(ProductsGrid.SelectedIndex <= 0 ? 0 : ProductsGrid.SelectedIndex - 1, 0);
                ProductsGrid.Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            await SelectProductAsync();
            e.Handled = true;
        }
    }

    private async void OnProductsGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SelectProductAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private async void OnProductDoubleClick(object sender, MouseButtonEventArgs e) => await SelectProductAsync();

    private async Task SelectProductAsync()
    {
        if (ProductsGrid.SelectedItem is not ProductLookupRow row)
        {
            var query = SearchBox.Text.Trim();
            if (query.Length == 0)
            {
                StatusText.Text = "Escribe un código o selecciona un producto para agregarlo a la venta.";
                return;
            }

            try
            {
                var products = await ApiClient.Client.GetFromJsonAsync<List<ProductLookupResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}") ?? [];
                var product = products.FirstOrDefault(item => string.Equals(item.Code, query, StringComparison.OrdinalIgnoreCase))
                    ?? (products.Count == 1 ? products[0] : null);
                if (product is null)
                {
                    StatusText.Text = products.Count > 1 ? "Hay varios productos. Selecciona uno de la lista." : "No se encontró el producto.";
                    return;
                }

                SelectedProduct = new ProductLookupSelection(product.Id, product.Code, product.Description, product.Price, product.WholesalePrice, product.WholesaleMinimumQuantity, product.Stock, product.UnitOfMeasure);
                DialogResult = true;
                return;
            }
            catch (HttpRequestException)
            {
                StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
                return;
            }
        }

        SelectedProduct = new ProductLookupSelection(row.Product.Id, row.Product.Code, row.Product.Description, row.Product.Price, row.Product.WholesalePrice, row.Product.WholesaleMinimumQuantity, row.Product.Stock, row.Product.UnitOfMeasure);
        DialogResult = true;
    }

    private sealed record ProductLookupResult(Guid Id, string Code, string Description, string? Category, decimal Price, decimal WholesalePrice, decimal WholesaleMinimumQuantity, decimal Stock, string UnitOfMeasure);

    private sealed record ProductLookupRow(ProductLookupResult Product)
    {
        public string Code => Product.Code;
        public string Description => Product.Description;
        public string Category => string.IsNullOrWhiteSpace(Product.Category) ? "-" : Product.Category;
        public string UnitOfMeasure => Product.UnitOfMeasure;
        public string PriceText => $"${Product.Price:0.00}";
        public string WholesaleText => Product.WholesalePrice > 0m ? $"${Product.WholesalePrice:0.00} desde {Product.WholesaleMinimumQuantity:0.###}" : "-";
        public string StockText => $"{Product.Stock:0.###}";
    }
}

public sealed record ProductLookupSelection(Guid Id, string Code, string Description, decimal Price, decimal WholesalePrice, decimal WholesaleMinimumQuantity, decimal Stock, string UnitOfMeasure = "Pieza");
