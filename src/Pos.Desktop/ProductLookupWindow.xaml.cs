using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class ProductLookupWindow : Window
{
    private CancellationTokenSource? _searchCancellation;

    public ProductLookupWindow(string initialQuery = "")
    {
        InitializeComponent();
        SearchBox.Text = initialQuery;
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
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
            StatusText.Text = products.Count == 0 ? "No se encontraron productos." : $"{products.Count} producto(s) encontrados.";
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
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
