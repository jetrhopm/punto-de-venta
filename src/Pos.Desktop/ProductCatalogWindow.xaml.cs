using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class ProductCatalogWindow : Window
{
    private CancellationTokenSource? _searchCancellation;
    private ProductRow? _selected;

    public ProductCatalogWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var query = SearchBox.Text.Trim();
        if (query.Length == 0) { ProductsGrid.ItemsSource = null; StatusText.Text = "Escribe un codigo o descripcion para buscar."; return; }
        try
        {
            await Task.Delay(180, token);
            var rows = await ApiClient.Client.GetFromJsonAsync<List<ProductRow>>($"/api/products/search?q={Uri.EscapeDataString(query)}", token) ?? [];
            ProductsGrid.ItemsSource = rows;
            StatusText.Text = rows.Count == 0 ? "No se encontraron productos." : $"{rows.Count} producto(s) encontrado(s). Selecciona uno para editar.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { StatusText.Text = $"No se pudo buscar: {exception.Message}"; }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProductsGrid.SelectedItem is not ProductRow row) return;
        _selected = row;
        FormTitleText.Text = "Editar producto";
        CodeBox.Text = row.Code;
        DescriptionBox.Text = row.Description;
        PriceBox.Text = row.Price.ToString("0.00", CultureInfo.InvariantCulture);
        CostBox.Text = row.Cost.ToString("0.00", CultureInfo.InvariantCulture);
        WholesalePriceBox.Text = row.WholesalePrice.ToString("0.00", CultureInfo.InvariantCulture);
        WholesaleMinimumBox.Text = row.WholesaleMinimumQuantity.ToString("0.###", CultureInfo.InvariantCulture);
        UnitBox.SelectedItem = string.IsNullOrWhiteSpace(row.UnitOfMeasure) ? "Pieza" : row.UnitOfMeasure;
        IsKitBox.IsChecked = row.IsKit;
    }

    private void OnNewClick(object sender, RoutedEventArgs e) => ClearForm();

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var command)) return;
        try
        {
            using var response = _selected is null
                ? await ApiClient.Client.PostAsJsonAsync("/api/products", command)
                : await ApiClient.Client.PutAsJsonAsync($"/api/products/{_selected.Id}", command);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            StatusText.Text = _selected is null ? "Producto creado correctamente." : "Producto actualizado correctamente.";
            var query = CodeBox.Text.Trim();
            ClearForm();
            SearchBox.Text = query;
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar el producto: {exception.Message}"; }
    }

    private async void OnDeactivateClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { StatusText.Text = "Selecciona un producto para desactivarlo."; return; }
        if (MessageBox.Show($"Se desactivara {_selected.Code} - {_selected.Description}. El historial se conservara.", "Desactivar producto", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.DeleteAsync($"/api/products/{_selected.Id}");
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            StatusText.Text = "Producto desactivado. Ya no aparecera en ventas.";
            ClearForm();
            SearchBox.Text = string.Empty;
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo desactivar el producto: {exception.Message}"; }
    }

    private bool TryReadForm(out object command)
    {
        command = new { };
        if (string.IsNullOrWhiteSpace(CodeBox.Text) || string.IsNullOrWhiteSpace(DescriptionBox.Text)) { StatusText.Text = "Codigo y descripcion son obligatorios."; return false; }
        if (!TryDecimal(PriceBox.Text, out var price) || !TryDecimal(CostBox.Text, out var cost) || !TryDecimal(WholesalePriceBox.Text, out var wholesalePrice) || !TryDecimal(WholesaleMinimumBox.Text, out var wholesaleMinimum)) { StatusText.Text = "Costo, precio y mayoreo deben ser numeros validos."; return false; }
        if (price < 0 || cost < 0 || wholesalePrice < 0 || wholesaleMinimum < 0) { StatusText.Text = "Los importes no pueden ser negativos."; return false; }
        var unitOfMeasure = UnitBox.SelectedItem?.ToString() ?? "Pieza";
        command = new { code = CodeBox.Text.Trim(), description = DescriptionBox.Text.Trim(), price, cost, wholesalePrice, wholesaleMinimumQuantity = wholesaleMinimum, isKit = IsKitBox.IsChecked == true, unitOfMeasure };
        return true;
    }

    private static bool TryDecimal(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private void ClearForm()
    {
        _selected = null;
        ProductsGrid.SelectedItem = null;
        FormTitleText.Text = "Nuevo producto";
        CodeBox.Clear(); DescriptionBox.Clear(); CostBox.Text = "0.00"; PriceBox.Text = "0.00"; WholesalePriceBox.Text = "0.00"; WholesaleMinimumBox.Text = "0"; UnitBox.SelectedIndex = 0; IsKitBox.IsChecked = false;
        CodeBox.Focus();
    }

    private sealed record ProductRow(Guid Id, string Code, string Description, decimal Price, decimal Cost, decimal WholesalePrice, decimal WholesaleMinimumQuantity, bool IsKit, string UnitOfMeasure, bool IsActive);
}
