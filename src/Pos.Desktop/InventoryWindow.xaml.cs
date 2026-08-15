using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Pos.Desktop;

public partial class InventoryWindow : Window
{
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private CancellationTokenSource? _loadCancellation;
    private int _page = 1;
    private string _sort = "description";
    private bool _descending;
    private bool _saving;
    private int _totalPages = 1;

    public InventoryWindow()
    {
        InitializeComponent();
        _filterTimer.Tick += async (_, _) => { _filterTimer.Stop(); _page = 1; await LoadAsync(); };
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _loadCancellation?.Cancel();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) { if (IsLoaded) { _filterTimer.Stop(); _filterTimer.Start(); } }
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void OnPreviousPageClick(object sender, RoutedEventArgs e) { if (_page <= 1) return; _page--; await LoadAsync(); }
    private async void OnNextPageClick(object sender, RoutedEventArgs e) { if (_page >= _totalPages) return; _page++; await LoadAsync(); }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var sort = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(sort)) return;
        _descending = _sort == sort && !_descending;
        _sort = sort;
        _page = 1;
        _ = LoadAsync();
    }

    private async void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not InventoryRow row) return;
        var column = e.Column.SortMemberPath;
        if (column is not ("stock" or "minimum" or "maximum")) return;

        // Let the grid commit its binding before reading the entered value.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (!IsLoaded) return;
        if (column == "stock") await SaveStockAsync(row);
        else await SaveLimitsAsync(row);
    }

    private async Task LoadAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _loadCancellation = cancellation;
        try
        {
            var query = Uri.EscapeDataString(SearchBox.Text.Trim());
            var status = (StatusBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            StatusText.Text = "Actualizando inventario...";
            using var response = await ApiClient.Client.GetAsync($"api/inventory/catalog?q={query}&status={Uri.EscapeDataString(status)}&sort={_sort}&descending={_descending.ToString().ToLowerInvariant()}&page={_page}", cancellation.Token);
            if (!ReferenceEquals(_loadCancellation, cancellation)) return;
            if (!response.IsSuccessStatusCode) { StatusText.Text = "No se pudo consultar el inventario."; return; }
            var result = await response.Content.ReadFromJsonAsync<InventoryPage>(cancellation.Token);
            if (!ReferenceEquals(_loadCancellation, cancellation)) return;
            if (result is null) return;
            InventoryGrid.ItemsSource = result.Items;
            _totalPages = result.TotalPages;
            ProductsText.Text = result.TotalCount.ToString("N0"); UnitsText.Text = result.TotalUnits.ToString("N3"); CostValueText.Text = result.TotalCostValue.ToString("C2"); SaleValueText.Text = result.TotalSaleValue.ToString("C2"); ProfitValueText.Text = result.TotalPotentialProfit.ToString("C2"); AlertsText.Text = (result.LowStockCount + result.OutOfStockCount).ToString("N0"); AlertsDetailText.Text = $"{result.OutOfStockCount:N0} agotados · {result.LowStockCount:N0} bajo mínimo";
            PageInfoText.Text = result.TotalCount == 0 ? "No hay productos que coincidan." : $"Página {result.Page} de {result.TotalPages}. Mostrando {((result.Page - 1) * result.PageSize) + 1}-{Math.Min(result.Page * result.PageSize, result.TotalCount)} de {result.TotalCount:N0} productos.";
            StatusText.Text = "Inventario actualizado.";
        }
        catch (OperationCanceledException) when (!ReferenceEquals(_loadCancellation, cancellation)) { }
        catch (OperationCanceledException) { StatusText.Text = "La conexión tardó demasiado. Puedes seguir usando JetVenta e intentar actualizar después."; }
        catch (HttpRequestException) { StatusText.Text = "No hay conexión con los servicios de JetVenta. Puedes cerrar esta ventana o intentar actualizar."; }
        catch (Exception exception) { StatusText.Text = $"No se pudo consultar el inventario: {exception.Message}"; }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation)) _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task SaveLimitsAsync(InventoryRow row)
    {
        if (_saving) return;
        if (!decimal.TryParse(row.MinimumStockText, out var minimum) || !decimal.TryParse(row.MaximumStockText, out var maximum) || minimum < 0m || maximum < 0m || maximum > 0m && maximum < minimum)
        {
            MessageBox.Show("Los mínimos y máximos deben ser números no negativos. El máximo puede quedar en 0 para no utilizarlo.", "Inventario", MessageBoxButton.OK, MessageBoxImage.Warning);
            await LoadAsync();
            return;
        }
        _saving = true;
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/inventory/limits", new { operationId = Guid.NewGuid(), productId = row.ProductId, minimumStock = minimum, maximumStock = maximum });
            if (!response.IsSuccessStatusCode) { StatusText.Text = "No se pudieron guardar los límites. Se restaurarán los valores confirmados."; await LoadAsync(); return; }
            row.MinimumStock = minimum; row.MaximumStock = maximum;
            StatusText.Text = $"Límites actualizados para {row.Description}.";
        }
        catch (OperationCanceledException) { StatusText.Text = "La conexión tardó demasiado al guardar los límites. Se recargará el inventario para confirmar el resultado."; await LoadAsync(); }
        catch (HttpRequestException) { StatusText.Text = "No hay conexión con los servicios de JetVenta. No se modificó el inventario."; await LoadAsync(); }
        catch (Exception exception) { StatusText.Text = $"No se pudieron guardar los límites: {exception.Message}"; await LoadAsync(); }
        finally { _saving = false; }
    }

    private async Task SaveStockAsync(InventoryRow row)
    {
        if (_saving) return;
        if (!decimal.TryParse(row.StockText, out var requestedStock) || requestedStock < 0m)
        {
            MessageBox.Show("La existencia debe ser un número igual o mayor que cero.", "Inventario", MessageBoxButton.OK, MessageBoxImage.Warning);
            await LoadAsync();
            return;
        }

        requestedStock = decimal.Round(requestedStock, 3, MidpointRounding.AwayFromZero);
        var quantity = requestedStock - row.Stock;
        if (quantity == 0m) return;
        var action = quantity > 0m ? "incrementará" : "reducirá";
        var confirmation = MessageBox.Show(
            $"La existencia de {row.Description} cambiará de {row.Stock:0.###} a {requestedStock:0.###}.\n\nEste ajuste quedará registrado en movimientos de inventario.",
            $"Confirmar {action} existencia",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) { await LoadAsync(); return; }

        _saving = true;
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/inventory/adjust", new
            {
                operationId = Guid.NewGuid(),
                productId = row.ProductId,
                quantity,
                reason = "Corrección de existencia desde el catálogo de inventario"
            });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = "No se pudo guardar el ajuste. Se recargará el inventario para confirmar el resultado.";
                await LoadAsync();
                return;
            }

            row.Stock = requestedStock;
            StatusText.Text = $"Existencia actualizada para {row.Description}. El movimiento quedó registrado.";
            await LoadAsync();
        }
        catch (OperationCanceledException) { StatusText.Text = "La conexión tardó demasiado. Se recargará el inventario para confirmar si el ajuste se registró."; await LoadAsync(); }
        catch (HttpRequestException) { StatusText.Text = "No hay conexión con los servicios de JetVenta. No se confirmó el ajuste; el inventario se recargará al reconectar."; await LoadAsync(); }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar el ajuste: {exception.Message}"; await LoadAsync(); }
        finally { _saving = false; }
    }

    private void OnAdjustmentClick(object sender, RoutedEventArgs e) { new InventoryAdjustmentWindow { Owner = this }.ShowDialog(); _ = LoadAsync(); }
    private void OnImportClick(object sender, RoutedEventArgs e) { new ProductImportWindow { Owner = this }.ShowDialog(); _ = LoadAsync(); }
    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Exportar inventario", Filter = "CSV (*.csv)|*.csv", FileName = $"inventario-{DateTime.Now:yyyyMMdd-HHmmss}.csv", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        using var response = await ApiClient.Client.GetAsync("api/inventory/export");
        if (!response.IsSuccessStatusCode) { MessageBox.Show("No se pudo exportar el inventario.", "Inventario", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        await using var source = await response.Content.ReadAsStreamAsync(); await using var target = File.Create(dialog.FileName); await source.CopyToAsync(target);
        StatusText.Text = $"Inventario exportado: {dialog.FileName}";
    }
    private void OnMovementsClick(object sender, RoutedEventArgs e) => new InventoryMovementsWindow { Owner = this }.ShowDialog();

    private sealed class InventoryPage
    {
        public List<InventoryRow> Items { get; set; } = [];
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public decimal TotalUnits { get; set; }
        public decimal TotalCostValue { get; set; }
        public decimal TotalSaleValue { get; set; }
        public decimal TotalPotentialProfit { get; set; }
        public int LowStockCount { get; set; }
        public int OutOfStockCount { get; set; }
    }

    private sealed class InventoryRow
    {
        private decimal _stock;
        private decimal _minimumStock;
        private decimal _maximumStock;
        public Guid ProductId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string UnitOfMeasure { get; set; } = string.Empty;
        public decimal Cost { get; set; }
        public decimal Price { get; set; }
        public decimal Stock
        {
            get => _stock;
            set { _stock = value; StockText = value.ToString("0.###"); }
        }
        public string StockText { get; set; } = "0";
        public decimal MinimumStock
        {
            get => _minimumStock;
            set { _minimumStock = value; MinimumStockText = value.ToString("0.###"); }
        }
        public decimal MaximumStock
        {
            get => _maximumStock;
            set { _maximumStock = value; MaximumStockText = value.ToString("0.###"); }
        }
        public string MinimumStockText { get; set; } = "0";
        public string MaximumStockText { get; set; } = "0";
        public decimal CostValue { get; set; }
        public decimal SaleValue { get; set; }
        public decimal PotentialProfit { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
