using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.Printing;

namespace Pos.Desktop;

public partial class PurchasePlanningWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private readonly List<SuggestionRow> _suggestions = [];
    private readonly List<OrderLineRow> _orderLines = [];
    private bool _loadingFilters;
    private CancellationTokenSource? _productSearchCancellation;

    public PurchasePlanningWindow()
    {
        InitializeComponent();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BarcodeScannerService.BarcodeScanned += OnBarcodeScanned;
        PrintOrderButton.IsEnabled = ApiClient.IsTicketPrintingAvailable;
        PrintHintText.Text = ApiClient.IsTicketPrintingAvailable
            ? $"Imprime con el perfil configurado de {ApiClient.PrinterTicketWidthMm} mm."
            : "Configura y habilita una impresora en Configuración > Impresora para imprimir esta lista.";
        await LoadFiltersAsync();
        await LoadSuggestionsAsync();
        await LoadOrdersAsync();
        ProductSearchTextBox.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        BarcodeScannerService.BarcodeScanned -= OnBarcodeScanned;
        _productSearchCancellation?.Cancel();
    }

    private async Task LoadFiltersAsync()
    {
        _loadingFilters = true;
        try
        {
            var suppliers = await GetWithRetryAsync<List<SupplierDto>>("/api/suppliers") ?? [];
            var departments = await GetWithRetryAsync<List<DepartmentDto>>("/api/departments") ?? [];
            SupplierFilterComboBox.ItemsSource = new[] { SupplierOption.All }.Concat(suppliers.Select(item => new SupplierOption(item.Id, item.Name))).ToList();
            OrderSupplierComboBox.ItemsSource = new[] { SupplierOption.None }.Concat(suppliers.Select(item => new SupplierOption(item.Id, item.Name))).ToList();
            DepartmentFilterComboBox.ItemsSource = new[] { DepartmentOption.All }.Concat(departments.Select(item => new DepartmentOption(item.Id, item.Name))).ToList();
            SupplierFilterComboBox.SelectedIndex = 0;
            OrderSupplierComboBox.SelectedIndex = 0;
            DepartmentFilterComboBox.SelectedIndex = 0;
        }
        catch (HttpRequestException) { MessageText.Text = "JetVenta aún está preparando la conexión. Pulsa Actualizar en unos segundos."; }
        finally { _loadingFilters = false; }
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingFilters) await LoadSuggestionsAsync();
    }

    private async void OnRefreshSuggestionsClick(object sender, RoutedEventArgs e) => await LoadSuggestionsAsync();

    private async Task LoadSuggestionsAsync()
    {
        try
        {
            var supplier = SupplierFilterComboBox.SelectedItem as SupplierOption;
            var department = DepartmentFilterComboBox.SelectedItem as DepartmentOption;
            var filters = new List<string>();
            if (supplier?.Id is Guid supplierId) filters.Add($"supplierId={supplierId}");
            if (department?.Id is Guid departmentId) filters.Add($"departmentId={departmentId}");
            var path = "/api/purchase-planning/suggestions" + (filters.Count == 0 ? string.Empty : "?" + string.Join("&", filters));
            var suggestions = await GetWithRetryAsync<List<SuggestionDto>>(path) ?? [];
            _suggestions.Clear(); _suggestions.AddRange(suggestions.Select(item => new SuggestionRow(item)));
            SuggestionsGrid.ItemsSource = null; SuggestionsGrid.ItemsSource = _suggestions;
            SuggestionSummaryText.Text = _suggestions.Count == 0 ? "No hay productos con existencia igual o menor al mínimo para este filtro." : $"{_suggestions.Count} producto(s) requieren revisión. Los artículos sin proveedor permanecen disponibles.";
        }
        catch (HttpRequestException) { MessageText.Text = "No se pudieron consultar las sugerencias. La API no respondió después de varios intentos; revisa Configuración > Diagnóstico."; }
    }

    private void OnAddSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = _suggestions.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0) { MessageText.Text = "Selecciona al menos un producto de la lista de sugerencias."; return; }
        foreach (var suggestion in selected)
        {
            AddSuggestionToOrder(suggestion);
        }
        RefreshOrderLines(); MessageText.Text = $"Se agregaron {selected.Count} partida(s) a la orden. La orden aún no altera inventario.";
    }

    private void OnSuggestionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsGrid.SelectedItem is not SuggestionRow suggestion) return;
        AddSuggestionToOrder(suggestion);
        RefreshOrderLines();
        MessageText.Text = $"{suggestion.Description} se agregó a la orden. La orden aún no altera inventario.";
    }

    private void AddSuggestionToOrder(SuggestionRow suggestion)
    {
        var line = _orderLines.SingleOrDefault(item => item.ProductId == suggestion.ProductId);
        if (line is null) _orderLines.Add(new OrderLineRow(suggestion));
        else line.Quantity += suggestion.Quantity;
    }

    private async void OnProductSearchChanged(object sender, TextChangedEventArgs e)
    {
        _productSearchCancellation?.Cancel();
        _productSearchCancellation = new CancellationTokenSource();
        var cancellationToken = _productSearchCancellation.Token;
        var query = ProductSearchTextBox.Text.Trim();
        if (query.Length == 0)
        {
            ProductResultsList.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            await Task.Delay(180, cancellationToken);
            var products = await Client.GetFromJsonAsync<List<ProductSearchDto>>($"/api/products/search?q={Uri.EscapeDataString(query)}", cancellationToken) ?? [];
            ProductResultsList.ItemsSource = products.Select(item => new ProductSearchRow(item)).ToList();
            ProductResultsList.Visibility = products.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }

    private async void OnProductSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (ProductResultsList.Items.Count > 0)
            {
                ProductResultsList.SelectedIndex = e.Key == Key.Down
                    ? Math.Min(ProductResultsList.SelectedIndex < 0 ? 0 : ProductResultsList.SelectedIndex + 1, ProductResultsList.Items.Count - 1)
                    : Math.Max(ProductResultsList.SelectedIndex <= 0 ? 0 : ProductResultsList.SelectedIndex - 1, 0);
                ProductResultsList.ScrollIntoView(ProductResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;
        if (ProductResultsList.SelectedItem is ProductSearchRow selected) AddProductToOrder(selected, 1m);
        else await AddExactProductAsync(ProductSearchTextBox.Text.Trim());
        e.Handled = true;
    }

    private void OnProductSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProductResultsList.SelectedItem is ProductSearchRow selected) AddProductToOrder(selected, 1m);
    }

    private void OnBarcodeScanned(object? sender, string code) => Dispatcher.BeginInvoke(async () =>
    {
        ProductSearchTextBox.Text = code;
        await AddExactProductAsync(code);
    });

    private async Task AddExactProductAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            var products = await Client.GetFromJsonAsync<List<ProductSearchDto>>($"/api/products/search?q={Uri.EscapeDataString(query)}") ?? [];
            var selected = products.FirstOrDefault(item => string.Equals(item.Code, query, StringComparison.OrdinalIgnoreCase))
                ?? (products.Count == 1 ? products[0] : null);
            if (selected is null)
            {
                MessageText.Text = products.Count == 0
                    ? "No se encontró el producto leído."
                    : "Hay varias coincidencias. Elige con flechas y presiona Enter.";
                return;
            }
            AddProductToOrder(new ProductSearchRow(selected), 1m);
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }

    private void AddProductToOrder(ProductSearchRow product, decimal quantity)
    {
        var suggested = _suggestions.SingleOrDefault(item => item.ProductId == product.Product.Id);
        var quantityToAdd = suggested?.Quantity ?? quantity;
        var existing = _orderLines.SingleOrDefault(item => item.ProductId == product.Product.Id);
        if (existing is null) _orderLines.Add(new OrderLineRow(product, quantityToAdd));
        else existing.Quantity += quantityToAdd;
        ProductSearchTextBox.Clear();
        ProductResultsList.Visibility = Visibility.Collapsed;
        RefreshOrderLines();
        MessageText.Text = $"{product.Product.Description} se agregó a la lista. No se modificó inventario.";
    }

    private void OnRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if (OrderLinesList.SelectedItem is not OrderLineRow line) return;
        _orderLines.Remove(line); RefreshOrderLines();
    }

    private async void OnCreateOrderClick(object sender, RoutedEventArgs e)
    {
        if (_orderLines.Count == 0) { MessageText.Text = "Agrega al menos una partida antes de guardar la orden."; return; }
        var supplier = OrderSupplierComboBox.SelectedItem as SupplierOption;
        var payload = new { operationId = Guid.NewGuid(), supplierId = supplier?.Id, notes = NotesTextBox.Text.Trim(), lines = _orderLines.Select(item => new { productId = item.ProductId, quantity = item.Quantity, unitCost = item.UnitCost }).ToArray() };
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/purchase-orders", payload);
            if (!response.IsSuccessStatusCode) { MessageText.Text = "No se pudo guardar la orden. Revisa los datos e inténtalo nuevamente."; return; }
            _orderLines.Clear(); NotesTextBox.Clear(); OrderSupplierComboBox.SelectedIndex = 0; RefreshOrderLines();
            MessageText.Text = "Orden guardada. No se modificaron existencias, costos ni precios.";
            await LoadOrdersAsync();
            new OperationResultWindow("Orden de compra guardada", "La orden quedó como pendiente. Registra la mercancía con “Recibir compra” cuando llegue a la tienda.", OperationResultKind.Success) { Owner = this }.ShowDialog();
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableNotConfirmed; }
    }

    private async void OnPrintOrderClick(object sender, RoutedEventArgs e)
    {
        if (_orderLines.Count == 0) { MessageText.Text = "Agrega al menos un producto antes de imprimir la lista."; return; }
        if (!ApiClient.IsTicketPrintingAvailable || string.IsNullOrWhiteSpace(ApiClient.PrinterName))
        {
            MessageText.Text = "Configura y habilita una impresora en Configuración > Impresora antes de imprimir la lista.";
            return;
        }

        try
        {
            var settings = await GetWithRetryAsync<TicketSettingsDto>("/api/ticket-settings");
            var supplier = OrderSupplierComboBox.SelectedItem as SupplierOption;
            var profile = TicketWindowsPrinter.CurrentProfile;
            var total = _orderLines.Sum(item => item.Quantity * item.UnitCost);
            var ticket = new TicketPdfData(
                settings?.Name ?? "JETVENTA",
                settings?.LegalName ?? string.Empty,
                settings?.TaxId ?? string.Empty,
                settings?.Address ?? string.Empty,
                settings?.Phone ?? string.Empty,
                "LISTA DE COMPRA",
                "Lista de reposición. No modifica inventario.",
                profile.WidthMm,
                Guid.NewGuid(),
                Guid.Empty,
                supplier?.Name ?? "Sin proveedor asignado",
                SessionContext.DisplayName ?? "Usuario",
                DateTimeOffset.Now,
                _orderLines.Select(item => new TicketPdfLine($"{item.Code} | {item.Description}", item.Quantity, item.UnitCost, item.Quantity * item.UnitCost)).ToList(),
                [],
                total);
            TicketWindowsPrinter.Print(ApiClient.PrinterName, ticket, profile, $"Lista de compra {DateTime.Now:yyyyMMddHHmmss}");
            MessageText.Text = $"Lista enviada a {ApiClient.PrinterName} con formato de {profile.WidthMm} mm.";
        }
        catch (Exception exception) { MessageText.Text = $"No se pudo imprimir la lista: {exception.Message}"; }
    }

    private async void OnRefreshOrdersClick(object sender, RoutedEventArgs e) => await LoadOrdersAsync();

    private async Task LoadOrdersAsync()
    {
        try
        {
            var orders = await GetWithRetryAsync<List<OrderDto>>("/api/purchase-orders") ?? [];
            OrdersGrid.ItemsSource = orders.Select(item => new OrderRow(item)).ToList();
        }
        catch (HttpRequestException) { MessageText.Text = "No se pudieron consultar las órdenes. La API no respondió después de varios intentos; revisa Configuración > Diagnóstico."; }
    }

    private async void OnCloseOrderClick(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not OrderRow order) { MessageText.Text = "Selecciona una orden guardada para cerrarla."; return; }
        if (order.Status == "Closed") { MessageText.Text = "La orden seleccionada ya está cerrada."; return; }
        try
        {
            using var response = await Client.PostAsync($"/api/purchase-orders/{order.Id}/close", null);
            if (!response.IsSuccessStatusCode) { MessageText.Text = "No se pudo cerrar la orden."; return; }
            MessageText.Text = "Orden cerrada. Esta acción no modifica existencias."; await LoadOrdersAsync();
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }

    private void RefreshOrderLines()
    {
        OrderLinesList.ItemsSource = null; OrderLinesList.ItemsSource = _orderLines;
        var total = _orderLines.Sum(item => item.Quantity * item.UnitCost);
        OrderTotalText.Text = _orderLines.Count == 0 ? "Sin productos seleccionados" : $"{_orderLines.Count} partida(s) | Total estimado: ${total:0.00}";
    }

    private static async Task<T?> GetWithRetryAsync<T>(string path)
    {
        HttpRequestException? lastException = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try { return await Client.GetFromJsonAsync<T>(path); }
            catch (HttpRequestException exception) when (attempt < 4)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(450 * attempt));
            }
        }
        throw lastException ?? new HttpRequestException("No se pudo conectar con la API.");
    }

    private sealed record SupplierDto(Guid Id, string Name);
    private sealed record DepartmentDto(Guid Id, string Name);
    private sealed record SuggestionDto(Guid ProductId, string Code, string Description, Guid? DepartmentId, string? Department, Guid? SupplierId, string? Supplier, decimal Stock, decimal MinimumStock, decimal SuggestedQuantity, decimal UnitCost, decimal EstimatedTotal, string UnitOfMeasure);
    private sealed record OrderDto(Guid Id, string Status, string? Supplier, string? Notes, decimal Total, int LineCount, DateTimeOffset CreatedAtUtc);
    private sealed record ProductSearchDto(Guid Id, string Code, string Description, decimal Price, decimal Cost, decimal Stock, string UnitOfMeasure);
    private sealed record TicketSettingsDto(string Name, string LegalName, string TaxId, string Address, string Phone, string TicketHeader, string TicketFooter, int TicketWidthMm);
    private sealed record SupplierOption(Guid? Id, string Name) { public static SupplierOption All { get; } = new(null, "Todos los proveedores"); public static SupplierOption None { get; } = new(null, "Sin proveedor asignado"); public string DisplayText => Name; }
    private sealed record DepartmentOption(Guid? Id, string Name) { public static DepartmentOption All { get; } = new(null, "Todos los departamentos"); public string DisplayText => Name; }
    private sealed class SuggestionRow(SuggestionDto source)
    {
        public bool IsSelected { get; set; }
        public Guid ProductId => source.ProductId; public string Code => source.Code; public string Description => source.Description; public string Department => string.IsNullOrWhiteSpace(source.Department) ? "Sin departamento" : source.Department; public string Supplier => string.IsNullOrWhiteSpace(source.Supplier) ? "Sin proveedor" : source.Supplier; public decimal Quantity => source.SuggestedQuantity; public decimal UnitCost => source.UnitCost; public string StockText => $"{source.Stock:0.###} {source.UnitOfMeasure}"; public string MinimumText => source.MinimumStock.ToString("0.###", CultureInfo.CurrentCulture); public string QuantityText => source.SuggestedQuantity.ToString("0.###", CultureInfo.CurrentCulture);
    }
    private sealed class OrderLineRow
    {
        public OrderLineRow(SuggestionRow source)
            : this(source.ProductId, source.Code, source.Description, source.Quantity, source.UnitCost) { }

        public OrderLineRow(ProductSearchRow source, decimal quantity)
            : this(source.Product.Id, source.Product.Code, source.Product.Description, quantity, source.Product.Cost) { }

        private OrderLineRow(Guid productId, string code, string description, decimal quantity, decimal unitCost)
        {
            ProductId = productId;
            Code = code;
            Description = description;
            Quantity = quantity;
            UnitCost = unitCost;
        }

        public Guid ProductId { get; }
        public string Code { get; }
        public string Description { get; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; }
        public string DisplayText => $"{Code} | {Description} | {Quantity:0.###} x ${UnitCost:0.00} = ${Quantity * UnitCost:0.00}";
    }
    private sealed record ProductSearchRow(ProductSearchDto Product) { public string DisplayText => $"{Product.Code} | {Product.Description} | Costo ${Product.Cost:0.00} | Existencia {Product.Stock:0.###} {Product.UnitOfMeasure}"; }
    private sealed class OrderRow(OrderDto source)
    {
        public Guid Id => source.Id; public string Supplier => string.IsNullOrWhiteSpace(source.Supplier) ? "Sin proveedor" : source.Supplier; public string Status => source.Status; public string StatusText => source.Status == "Open" ? "Pendiente" : "Cerrada"; public string? Notes => source.Notes; public int LineCount => source.LineCount; public string TotalText => $"${source.Total:0.00}"; public string CreatedText => source.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }
}
