using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class PriceVerifierWindow : Window
{
    public PriceVerifierWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CodeBox.Focus();
    }

    private async void OnCodeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await VerifyAsync();
    }

    private async Task VerifyAsync()
    {
        var code = CodeBox.Text.Trim();
        if (code.Length == 0)
        {
            StatusText.Text = "Escanea o escribe un código.";
            return;
        }

        try
        {
            var products = await ApiClient.Client.GetFromJsonAsync<List<ProductLookupResult>>($"/api/products/search?q={Uri.EscapeDataString(code)}") ?? [];
            var product = products.FirstOrDefault(item => string.Equals(item.Code.Trim(), code, StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                ProductNameText.Text = "Código no encontrado";
                ProductCodeText.Text = $"Código de barras: {code}";
                ProductPriceText.Text = "$0.00";
                ProductStockText.Text = "0";
                ProductAvailabilityText.Text = "No encontrado";
                ProductAvailabilityText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 35, 24));
                ProductAvailabilityBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(253, 235, 234));
                ProductDetailText.Text = "";
                StatusText.Text = "No se encontró un producto con ese código.";
                CodeBox.SelectAll();
                return;
            }

            ProductNameText.Text = GetDisplayName(product);
            ProductCodeText.Text = $"Código de barras: {product.Code}";
            ProductPriceText.Text = $"${product.Price:0.00}";
            ProductStockText.Text = $"{product.Stock:0.###} {product.UnitOfMeasure}";
            var available = product.Stock > 0m;
            ProductAvailabilityText.Text = available ? "Disponible" : "Agotado";
            ProductAvailabilityText.Foreground = new System.Windows.Media.SolidColorBrush(available
                ? System.Windows.Media.Color.FromRgb(22, 131, 74)
                : System.Windows.Media.Color.FromRgb(180, 35, 24));
            ProductAvailabilityBadge.Background = new System.Windows.Media.SolidColorBrush(available
                ? System.Windows.Media.Color.FromRgb(231, 245, 236)
                : System.Windows.Media.Color.FromRgb(253, 235, 234));
            ProductDetailText.Text = $"Unidad de venta: {product.UnitOfMeasure}";
            StatusText.Text = "";
            CodeBox.SelectAll();
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
    }

    private static string GetDisplayName(ProductLookupResult product)
    {
        var description = product.Description?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(description) ||
            string.Equals(description, product.Code, StringComparison.OrdinalIgnoreCase) ||
            description.All(char.IsDigit)
            ? "Producto sin descripción registrada"
            : description;
    }

    private sealed record ProductLookupResult(Guid Id, string Code, string Description, decimal Price, decimal Stock, string UnitOfMeasure);
}
