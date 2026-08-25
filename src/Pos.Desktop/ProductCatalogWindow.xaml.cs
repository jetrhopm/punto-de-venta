using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class ProductCatalogWindow : UserControl
{
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _filterCancellation;
    private CatalogProductRow? _selected;
    private List<DepartmentRow> _departments = [];
    private int _page = 1;
    private bool _loadingForm;
    private string _configuredWeightUnit = "Kilogramo";
    private bool _autoPriceWithProfit = true;
    private decimal _defaultProfitPercent = 20m;
    private bool _catalogReady;
    private bool _updatingFilterControls;

    public ProductCatalogWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await LoadStoreOptionsAsync();
            ClearForm();
            await LoadMeasureSettingsAsync();
            await LoadDepartmentsAsync();
            _catalogReady = true;
            await LoadCatalogAsync();
            SearchBox.Focus();
        };
        Unloaded += (_, _) =>
        {
            _catalogReady = false;
            _filterCancellation?.Cancel();
            _loadCancellation?.Cancel();
        };
        SearchBox.TextChanged += OnFilterTextChanged;
        MinimumPriceBox.TextChanged += OnFilterTextChanged;
        MaximumPriceBox.TextChanged += OnFilterTextChanged;
        MinimumProfitBox.TextChanged += OnFilterTextChanged;
        DepartmentFilterBox.SelectionChanged += OnFilterSelectionChanged;
        SortBox.SelectionChanged += OnFilterSelectionChanged;
        DescendingBox.Checked += OnFilterCheckedChanged;
        DescendingBox.Unchecked += OnFilterCheckedChanged;
    }

    private async Task LoadStoreOptionsAsync()
    {
        try
        {
            var options = await ApiClient.Client.GetFromJsonAsync<StoreOptions>("/api/store-options");
            if (options is not null)
            {
                _autoPriceWithProfit = options.AutoPriceWithProfit;
                _defaultProfitPercent = options.DefaultProfitPercent;
            }
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar las opciones de precios"); }
    }

    private async Task LoadMeasureSettingsAsync()
    {
        try
        {
            var settings = await ApiClient.Client.GetFromJsonAsync<MeasureSettings>("/api/measure-settings");
            if (!string.IsNullOrWhiteSpace(settings?.DefaultWeightUnit)) _configuredWeightUnit = settings.DefaultWeightUnit;
            WeightUnitHintText.Text = $"Granel usará {_configuredWeightUnit.ToLowerInvariant()} según Configuración.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la unidad de peso"); }
    }

    private async Task LoadDepartmentsAsync()
    {
        try
        {
            _departments = await ApiClient.Client.GetFromJsonAsync<List<DepartmentRow>>("/api/departments") ?? [];
            var filter = new List<DepartmentRow> { new(Guid.Empty, "Todos los departamentos", true) };
            filter.AddRange(_departments);
            DepartmentFilterBox.ItemsSource = filter;
            DepartmentFilterBox.SelectedIndex = 0;
            DepartmentBox.ItemsSource = _departments;
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar los departamentos"); }
    }

    public async Task ReloadDepartmentsAsync() => await LoadDepartmentsAsync();

    private async Task LoadCatalogAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        try
        {
            var query = new List<string> { $"page={_page}", $"sort={Uri.EscapeDataString(CurrentSort())}", $"descending={DescendingBox.IsChecked == true}" };
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) query.Add($"q={Uri.EscapeDataString(SearchBox.Text.Trim())}");
            if (DepartmentFilterBox.SelectedValue is Guid department && department != Guid.Empty) query.Add($"departmentId={department}");
            if (TryDecimal(MinimumPriceBox.Text, out var minimumPrice)) query.Add($"minimumPrice={minimumPrice.ToString(CultureInfo.InvariantCulture)}");
            if (TryDecimal(MaximumPriceBox.Text, out var maximumPrice)) query.Add($"maximumPrice={maximumPrice.ToString(CultureInfo.InvariantCulture)}");
            if (TryDecimal(MinimumProfitBox.Text, out var minimumProfit)) query.Add($"minimumProfit={minimumProfit.ToString(CultureInfo.InvariantCulture)}");
            var result = await ApiClient.Client.GetFromJsonAsync<CatalogPage>("/api/products/catalog?" + string.Join('&', query), token);
            token.ThrowIfCancellationRequested();
            ProductsGrid.ItemsSource = result?.Items ?? [];
            var total = result?.TotalCount ?? 0;
            PageText.Text = result is null ? string.Empty : $"Página {result.Page} de {result.TotalPages} · {total:N0} productos";
            PreviousPageButton.IsEnabled = result is not null && result.Page > 1;
            NextPageButton.IsEnabled = result is not null && result.Page < result.TotalPages;
            StatusText.Text = total == 0 ? "No hay productos que coincidan con los filtros." : "Selecciona un producto para editarlo. La página muestra hasta 500 productos.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar el catálogo"); }
    }

    private string CurrentSort() => (SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "description";
    private void OnFilterTextChanged(object sender, TextChangedEventArgs e) => QueueCatalogSynchronization();
    private void OnFilterSelectionChanged(object sender, SelectionChangedEventArgs e) => QueueCatalogSynchronization();
    private void OnFilterCheckedChanged(object sender, RoutedEventArgs e) => QueueCatalogSynchronization();

    private void QueueCatalogSynchronization()
    {
        if (!_catalogReady || _updatingFilterControls) return;
        _filterCancellation?.Cancel();
        _filterCancellation = new CancellationTokenSource();
        _ = SynchronizeCatalogAsync(_filterCancellation.Token);
    }

    private async Task SynchronizeCatalogAsync(CancellationToken token)
    {
        try
        {
            StatusText.Text = "Actualizando catálogo...";
            await Task.Delay(220, token);
            _page = 1;
            await LoadCatalogAsync();
        }
        catch (OperationCanceledException) { }
    }

    private void CancelCatalogSynchronization() => _filterCancellation?.Cancel();

    private async void OnApplyFiltersClick(object sender, RoutedEventArgs e) { CancelCatalogSynchronization(); _page = 1; await LoadCatalogAsync(); }
    private async void OnPreviousPageClick(object sender, RoutedEventArgs e) { if (_page > 1) { CancelCatalogSynchronization(); _page--; await LoadCatalogAsync(); } }
    private async void OnNextPageClick(object sender, RoutedEventArgs e) { if (NextPageButton.IsEnabled) { CancelCatalogSynchronization(); _page++; await LoadCatalogAsync(); } }

    private void OnGridSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (e.Column.SortMemberPath is null) return;
        CancelCatalogSynchronization();
        _updatingFilterControls = true;
        SortBox.SelectedItem = SortBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), e.Column.SortMemberPath, StringComparison.OrdinalIgnoreCase)) ?? SortBox.SelectedItem;
        DescendingBox.IsChecked = !(DescendingBox.IsChecked == true);
        _updatingFilterControls = false;
        _page = 1;
        QueueCatalogSynchronization();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProductsGrid.SelectedItem is not CatalogProductRow row) return;
        _selected = row;
        _loadingForm = true;
        FormTitleText.Text = "Editar producto";
        CodeBox.Text = row.Code;
        DescriptionBox.Text = row.Description;
        DepartmentBox.SelectedValue = row.DepartmentId;
        UnitBox.SelectedItem = row.UnitOfMeasure;
        CostBox.Text = Money(row.Cost);
        ProfitPercentBox.Text = row.ProfitPercent > 0m ? Percent(row.ProfitPercent) : string.Empty;
        PriceBox.Text = Money(row.Price);
        ProfitAmountBox.Text = Money(row.ProfitAmount);
        WholesalePriceBox.Text = Money(row.WholesalePrice);
        WholesaleProfitPercentBox.Text = row.WholesaleProfitPercent > 0m ? Percent(row.WholesaleProfitPercent) : string.Empty;
        WholesaleProfitAmountBox.Text = Money(row.WholesaleProfitAmount);
        WholesaleMinimumBox.Text = Quantity(row.WholesaleMinimumQuantity);
        IsKitBox.IsChecked = row.IsKit;
        _loadingForm = false;
    }

    private void OnNewClick(object sender, RoutedEventArgs e) => ClearForm();
    private void OnPricingChanged(object sender, RoutedEventArgs e) { if (!_loadingForm) CalculateSalePrice(); }
    private void OnWholesalePricingChanged(object sender, RoutedEventArgs e) { if (!_loadingForm) CalculateWholesalePrice(); }
    private void OnPriceChanged(object sender, TextChangedEventArgs e) { UpdateProfitAmount(); }
    private void OnWholesalePriceChanged(object sender, TextChangedEventArgs e) { UpdateWholesaleProfitAmount(); }
    private void CalculateSalePrice() { if (TryDecimal(CostBox.Text, out var cost) && TryDecimal(ProfitPercentBox.Text, out var profit)) PriceBox.Text = Money(cost * (1m + profit / 100m)); UpdateProfitAmount(); }
    private void CalculateWholesalePrice() { if (TryDecimal(CostBox.Text, out var cost) && TryDecimal(WholesaleProfitPercentBox.Text, out var profit)) WholesalePriceBox.Text = Money(cost * (1m + profit / 100m)); UpdateWholesaleProfitAmount(); }
    private void UpdateProfitAmount()
    {
        if (PriceBox is null || CostBox is null || ProfitAmountBox is null) return;
        if (TryDecimal(PriceBox.Text, out var price) && TryDecimal(CostBox.Text, out var cost)) ProfitAmountBox.Text = Money(price - cost);
        else ProfitAmountBox.Text = "0.00";
    }
    private void UpdateWholesaleProfitAmount()
    {
        if (WholesalePriceBox is null || CostBox is null || WholesaleProfitAmountBox is null) return;
        if (TryDecimal(WholesalePriceBox.Text, out var price) && TryDecimal(CostBox.Text, out var cost) && price > 0m) WholesaleProfitAmountBox.Text = Money(price - cost); else WholesaleProfitAmountBox.Text = "0.00";
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var command)) return;
        try
        {
            using var response = _selected is null ? await ApiClient.Client.PostAsJsonAsync("/api/products", command) : await ApiClient.Client.PutAsJsonAsync($"/api/products/{_selected.Id}", command);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            var code = CodeBox.Text.Trim(); ClearForm(); SearchBox.Text = code; _page = 1; await LoadCatalogAsync(); StatusText.Text = "Producto guardado correctamente.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar el producto"); }
    }

    private async void OnDeactivateClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { StatusText.Text = "Selecciona un producto para desactivarlo."; return; }
        if (MessageBox.Show($"Se desactivará {_selected.Code} - {_selected.Description}. El historial se conservará.", "Desactivar producto", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.DeleteAsync($"/api/products/{_selected.Id}");
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            ClearForm(); await LoadCatalogAsync(); StatusText.Text = "Producto desactivado. El historial se conserva.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo desactivar el producto"); }
    }

    private bool TryReadForm(out object command)
    {
        command = new { };
        if (string.IsNullOrWhiteSpace(CodeBox.Text) || string.IsNullOrWhiteSpace(DescriptionBox.Text)) { StatusText.Text = "Código y descripción son obligatorios."; return false; }
        if (!TryDecimal(CostBox.Text, out var cost) || !TryDecimal(PriceBox.Text, out var price)) { StatusText.Text = "Costo y precio de venta deben ser números válidos."; return false; }
        var profit = TryDecimal(ProfitPercentBox.Text, out var parsedProfit) ? parsedProfit : 0m;
        var wholesalePrice = TryDecimal(WholesalePriceBox.Text, out var parsedWholesalePrice) ? parsedWholesalePrice : 0m;
        var wholesaleProfit = TryDecimal(WholesaleProfitPercentBox.Text, out var parsedWholesaleProfit) ? parsedWholesaleProfit : 0m;
        var wholesaleMinimum = TryDecimal(WholesaleMinimumBox.Text, out var parsedWholesaleMinimum) ? parsedWholesaleMinimum : 0m;
        if (cost < 0 || profit < 0 || price < 0 || wholesalePrice < 0 || wholesaleProfit < 0 || wholesaleMinimum < 0) { StatusText.Text = "Los importes y porcentajes no pueden ser negativos."; return false; }
        if (price <= 0m && profit <= 0m) { StatusText.Text = "Indica un precio de venta manual mayor a cero o captura un porcentaje de ganancia."; return false; }
        var unit = UnitBox.SelectedItem?.ToString() ?? "Pieza";
        if (string.Equals(unit, "Granel (unidad configurada)", StringComparison.OrdinalIgnoreCase)) unit = _configuredWeightUnit;
        command = new { code = CodeBox.Text.Trim(), description = DescriptionBox.Text.Trim(), price, cost, profitPercent = profit, wholesalePrice, wholesaleProfitPercent = wholesaleProfit, wholesaleMinimumQuantity = wholesaleMinimum, isKit = IsKitBox.IsChecked == true, unitOfMeasure = unit, departmentId = DepartmentBox.SelectedValue is Guid department && department != Guid.Empty ? department : (Guid?)null };
        return true;
    }

    private void OnDepartmentsClick(object sender, RoutedEventArgs e) { var window = new DepartmentManagerWindow { Owner = Window.GetWindow(this) }; window.Closed += async (_, _) => await LoadDepartmentsAsync(); window.ShowDialog(); }
    private void OnPromotionsClick(object sender, RoutedEventArgs e) { new PromotionWindow { Owner = Window.GetWindow(this) }.ShowDialog(); }
    private void ClearForm() { _selected = null; _loadingForm = true; ProductsGrid.SelectedItem = null; FormTitleText.Text = "Nuevo producto"; CodeBox.Clear(); DescriptionBox.Clear(); DepartmentBox.SelectedIndex = -1; UnitBox.SelectedIndex = 0; CostBox.Text = "0.00"; ProfitPercentBox.Text = _autoPriceWithProfit ? Percent(_defaultProfitPercent) : string.Empty; PriceBox.Text = "0.00"; ProfitAmountBox.Text = "0.00"; WholesalePriceBox.Text = "0.00"; WholesaleProfitPercentBox.Text = string.Empty; WholesaleProfitAmountBox.Text = "0.00"; WholesaleMinimumBox.Text = "0"; IsKitBox.IsChecked = false; _loadingForm = false; CodeBox.Focus(); }
    private static bool TryDecimal(string? value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Percent(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private sealed record DepartmentRow(Guid Id, string Name, bool IsActive);
    private sealed record CatalogProductRow(Guid Id, string Code, string Description, string Department, Guid? DepartmentId, decimal Cost, decimal Price, decimal ProfitPercent, decimal ProfitAmount, decimal WholesalePrice, decimal WholesaleProfitPercent, decimal WholesaleProfitAmount, decimal WholesaleMinimumQuantity, decimal Stock, decimal MinimumStock, decimal MaximumStock, string UnitOfMeasure, bool IsKit, bool IsActive);
    private sealed record CatalogPage(List<CatalogProductRow> Items, int Page, int PageSize, int TotalCount, int TotalPages);
    private sealed record MeasureSettings(string DefaultWeightUnit);
    private sealed record StoreOptions(bool InventoryEnabled, string InventoryCostMethod, bool CreditSalesEnabled, bool CommonProductsEnabled, bool AutoPriceWithProfit, decimal DefaultProfitPercent, bool RoundSaleAmounts, string RoundingMode, string OccasionalNotice, int OccasionalNoticeEverySales);
}
