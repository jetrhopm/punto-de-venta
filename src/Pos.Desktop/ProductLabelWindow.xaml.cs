using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.Printing;

namespace Pos.Desktop;

public partial class ProductLabelWindow : Window
{
    private CancellationTokenSource? _searchCancellation;
    private ProductResult? _selectedProduct;
    private string _currencySymbol = "$";

    public ProductLabelWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => { _searchCancellation?.Cancel(); BarcodeScannerService.BarcodeScanned -= OnBarcodeScanned; };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BarcodeScannerService.BarcodeScanned += OnBarcodeScanned;
        PrintButton.IsEnabled = ApiClient.IsTicketPrintingAvailable;
        StatusText.Text = ApiClient.IsTicketPrintingAvailable
            ? $"Imprimirá con {ApiClient.PrinterName} ({ApiClient.PrinterTicketWidthMm} mm)."
            : "No hay impresora configurada para esta caja. Configúrala en Configuración > Impresora.";
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<CurrencySettings>("/api/currency-settings");
            _currencySymbol = string.IsNullOrWhiteSpace(settings?.CurrencySymbol) ? "$" : settings.CurrencySymbol.Trim();
        }
        catch (HttpRequestException) { }
        SearchBox.Focus();
    }

    private async void OnSearchChanged(object sender, TextChangedEventArgs e) => await SearchAsync(SearchBox.Text.Trim(), false);
    private async void OnSearchClick(object sender, RoutedEventArgs e) => await SearchAsync(SearchBox.Text.Trim(), true);

    private async Task SearchAsync(string query, bool immediate)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        if (string.IsNullOrWhiteSpace(query)) { ResultsGrid.ItemsSource = null; return; }
        try
        {
            if (!immediate) await Task.Delay(180, token);
            var products = await ApiClient.Client.GetFromJsonAsync<List<ProductResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}", token) ?? [];
            if (token.IsCancellationRequested || !string.Equals(query, SearchBox.Text.Trim(), StringComparison.Ordinal)) return;
            ResultsGrid.ItemsSource = products.Select(product => new ProductRow(product, _currencySymbol)).ToList();
            ResultsGrid.SelectedIndex = products.Count > 0 ? 0 : -1;
            StatusText.Text = products.Count == 0 ? "No se encontraron productos." : $"{products.Count} producto(s) encontrados. Usa flechas y Enter o doble clic para elegirlo.";
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (ResultsGrid.Items.Count > 0)
            {
                ResultsGrid.SelectedIndex = e.Key == Key.Down ? Math.Min(Math.Max(ResultsGrid.SelectedIndex, 0) + 1, ResultsGrid.Items.Count - 1) : Math.Max(ResultsGrid.SelectedIndex - 1, 0);
                ResultsGrid.ScrollIntoView(ResultsGrid.SelectedItem);
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter) { await SelectExactOrSelectedAsync(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private async void OnResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SelectCurrentProduct(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        await Task.CompletedTask;
    }

    private void OnProductDoubleClick(object sender, MouseButtonEventArgs e) => SelectCurrentProduct();

    private async Task SelectExactOrSelectedAsync()
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            var matches = await ApiClient.Client.GetFromJsonAsync<List<ProductResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}") ?? [];
            var product = matches.FirstOrDefault(item => string.Equals(item.Code, query, StringComparison.OrdinalIgnoreCase))
                ?? (ResultsGrid.SelectedItem as ProductRow)?.Product
                ?? (matches.Count == 1 ? matches[0] : null);
            if (product is null) { StatusText.Text = "Hay varias coincidencias: selecciónala con flechas o doble clic."; return; }
            SelectProduct(product);
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }

    private void SelectCurrentProduct()
    {
        if (ResultsGrid.SelectedItem is ProductRow row) SelectProduct(row.Product);
    }

    private void SelectProduct(ProductResult product)
    {
        _selectedProduct = product;
        SearchBox.Text = product.Code;
        SelectedProductText.Text = $"{product.Code}\n{product.Description}";
        SelectedPriceText.Text = $"{_currencySymbol}{product.Price:0.00}";
        StatusText.Text = "Etiqueta lista. Indica la cantidad y pulsa Imprimir.";
        try { PreviewHost.Content = TicketWindowsPrinter.CreateProductLabelsVisual(new ProductLabelData(product.Code, product.Description, product.Price, _currencySymbol), 1, TicketWindowsPrinter.CurrentProfile); }
        catch (InvalidOperationException exception) { PreviewHost.Content = null; StatusText.Text = exception.Message; }
        QuantityBox.Focus();
        QuantityBox.SelectAll();
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct is null) { OperationFeedback.Show(this, "Etiquetas", "Selecciona un producto antes de imprimir.", OperationResultKind.Warning); return; }
        if (!ApiClient.IsTicketPrintingAvailable || string.IsNullOrWhiteSpace(ApiClient.PrinterName)) { OperationFeedback.Show(this, "Impresora no configurada", "Para imprimir etiquetas, ve a Configuración > Impresora y habilita una impresora para esta caja.", OperationResultKind.Warning); return; }
        if (!int.TryParse(QuantityBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity) || quantity is < 1 or > 200) { OperationFeedback.Show(this, "Cantidad inválida", "Indica entre 1 y 200 etiquetas.", OperationResultKind.Warning); return; }
        try
        {
            var product = _selectedProduct;
            TicketWindowsPrinter.PrintProductLabels(ApiClient.PrinterName, new ProductLabelData(product.Code, product.Description, product.Price, _currencySymbol), quantity, TicketWindowsPrinter.CurrentProfile);
            StatusText.Text = $"Se enviaron {quantity} etiqueta(s) a {ApiClient.PrinterName}.";
            OperationFeedback.Show(this, "Etiquetas enviadas", StatusText.Text, OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"No se pudieron imprimir las etiquetas: {exception.Message}";
            OperationFeedback.Show(this, "No se pudieron imprimir", StatusText.Text, OperationResultKind.Error);
        }
        await Task.CompletedTask;
    }

    private void OnBarcodeScanned(object? sender, string code) => Dispatcher.BeginInvoke(async () => { SearchBox.Text = code; await SelectExactOrSelectedAsync(); });
    private void OnQuantityPreviewMouseDown(object sender, MouseButtonEventArgs e) { if (sender is TextBox box && !box.IsKeyboardFocusWithin) { e.Handled = true; box.Focus(); box.SelectAll(); } }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed record CurrencySettings(string CurrencySymbol);
    private sealed record ProductResult(Guid Id, string Code, string Description, decimal Price, decimal Stock, string UnitOfMeasure);
    private sealed record ProductRow(ProductResult Product, string CurrencySymbol)
    {
        public string Code => Product.Code;
        public string Description => Product.Description;
        public string UnitOfMeasure => Product.UnitOfMeasure;
        public string PriceText => $"{CurrencySymbol}{Product.Price:0.00}";
    }
}
