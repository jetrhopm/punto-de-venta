using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class PurchasePlanningWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private readonly List<SuggestionRow> _suggestions = [];
    private readonly List<OrderLineRow> _orderLines = [];
    private bool _loadingFilters;

    public PurchasePlanningWindow()
    {
        InitializeComponent();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadFiltersAsync();
        await LoadSuggestionsAsync();
        await LoadOrdersAsync();
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
            var line = _orderLines.SingleOrDefault(item => item.ProductId == suggestion.ProductId);
            if (line is null) _orderLines.Add(new OrderLineRow(suggestion));
            else line.Quantity += suggestion.Quantity;
        }
        RefreshOrderLines(); MessageText.Text = $"Se agregaron {selected.Count} partida(s) a la orden. La orden aún no altera inventario.";
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
    private sealed record SupplierOption(Guid? Id, string Name) { public static SupplierOption All { get; } = new(null, "Todos los proveedores"); public static SupplierOption None { get; } = new(null, "Sin proveedor asignado"); public string DisplayText => Name; }
    private sealed record DepartmentOption(Guid? Id, string Name) { public static DepartmentOption All { get; } = new(null, "Todos los departamentos"); public string DisplayText => Name; }
    private sealed class SuggestionRow(SuggestionDto source)
    {
        public bool IsSelected { get; set; }
        public Guid ProductId => source.ProductId; public string Code => source.Code; public string Description => source.Description; public string Department => string.IsNullOrWhiteSpace(source.Department) ? "Sin departamento" : source.Department; public string Supplier => string.IsNullOrWhiteSpace(source.Supplier) ? "Sin proveedor" : source.Supplier; public decimal Quantity => source.SuggestedQuantity; public decimal UnitCost => source.UnitCost; public string StockText => $"{source.Stock:0.###} {source.UnitOfMeasure}"; public string MinimumText => source.MinimumStock.ToString("0.###", CultureInfo.CurrentCulture); public string QuantityText => source.SuggestedQuantity.ToString("0.###", CultureInfo.CurrentCulture);
    }
    private sealed class OrderLineRow(SuggestionRow source)
    {
        public Guid ProductId { get; } = source.ProductId; public decimal Quantity { get; set; } = source.Quantity; public decimal UnitCost { get; } = source.UnitCost; public string DisplayText => $"{source.Code} | {source.Description} | {Quantity:0.###} x ${UnitCost:0.00} = ${Quantity * UnitCost:0.00}";
    }
    private sealed class OrderRow(OrderDto source)
    {
        public Guid Id => source.Id; public string Supplier => string.IsNullOrWhiteSpace(source.Supplier) ? "Sin proveedor" : source.Supplier; public string Status => source.Status; public string StatusText => source.Status == "Open" ? "Pendiente" : "Cerrada"; public string? Notes => source.Notes; public int LineCount => source.LineCount; public string TotalText => $"${source.Total:0.00}"; public string CreatedText => source.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }
}
