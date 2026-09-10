using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class KitWindow : Window
{
    private readonly IReadOnlyList<ProductEditorWindow.DepartmentOption> _departments;
    private readonly string _weightUnit;
    private readonly bool _autoPrice;
    private readonly decimal _defaultProfit;
    private readonly ObservableCollection<KitComponentRow> _components = [];
    private KitRow? _selected;
    private ProductSearchRow? _pendingComponent;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _componentSearchCancellation;
    private bool _showInactive;

    public KitWindow() : this([], "Kilogramo", true, 20m) { }

    public KitWindow(IReadOnlyList<ProductEditorWindow.DepartmentOption> departments, string weightUnit, bool autoPrice, decimal defaultProfit)
    {
        InitializeComponent();
        _departments = departments; _weightUnit = weightUnit; _autoPrice = autoPrice; _defaultProfit = defaultProfit;
        ComponentsGrid.ItemsSource = _components;
        Loaded += async (_, _) => await LoadKitsAsync();
        Closed += (_, _) => { _searchCancellation?.Cancel(); _componentSearchCancellation?.Cancel(); };
    }

    private async Task LoadKitsAsync()
    {
        _searchCancellation?.Cancel(); _searchCancellation = new CancellationTokenSource(); var token = _searchCancellation.Token;
        try
        {
            var rows = await ApiClient.Client.GetFromJsonAsync<List<KitRow>>($"/api/kits?includeInactive={_showInactive}&q={Uri.EscapeDataString(SearchBox.Text.Trim())}", token) ?? [];
            token.ThrowIfCancellationRequested(); KitsGrid.ItemsSource = rows;
            if (_selected is not null) KitsGrid.SelectedItem = rows.SingleOrDefault(item => item.Id == _selected.Id);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar los kits."); }
    }
    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e) { try { await Task.Delay(180); await LoadKitsAsync(); } catch (TaskCanceledException) { } }
    private async void OnKitSelectionChanged(object sender, SelectionChangedEventArgs e) { if (KitsGrid.SelectedItem is KitRow row) { _selected = row; await LoadComponentsAsync(); } }
    private void OnKitDoubleClick(object sender, MouseButtonEventArgs e) => OnEditDetailsClick(sender, e);

    private async Task LoadComponentsAsync()
    {
        if (_selected is null) return;
        try
        {
            var rows = await ApiClient.Client.GetFromJsonAsync<List<KitComponentRow>>($"/api/kits/{_selected.Id}") ?? [];
            _components.Clear(); foreach (var row in rows) _components.Add(row); UpdateSelectedSummary();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la composición del kit."); }
    }

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        var editor = new ProductEditorWindow(null, _departments, _weightUnit, _autoPrice, _defaultProfit, true) { Owner = this };
        if (editor.ShowDialog() != true || editor.SavedProduct is null || editor.SavedProduct.Id == Guid.Empty) return;
        await LoadKitsAsync();
        _selected = ToRow(editor.SavedProduct);
        KitsGrid.SelectedItem = ((IEnumerable<KitRow>)KitsGrid.ItemsSource).SingleOrDefault(item => item.Id == _selected.Id);
        _components.Clear(); UpdateSelectedSummary(); StatusText.Text = "Agrega los artículos y guarda la composición para dejar el kit listo para vender."; ComponentSearchBox.Focus();
    }
    private async void OnEditDetailsClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { ShowResult("Selecciona un kit", "Selecciona un kit de la lista para editar sus datos."); return; }
        var item = _selected;
        var editor = new ProductEditorWindow(new ProductEditorWindow.ProductEditModel(item.Id, item.Code, item.Description, item.DepartmentId, item.UnitOfMeasure, item.Cost, item.Price, item.ProfitPercent, item.WholesalePrice, item.WholesaleProfitPercent, item.WholesaleMinimumQuantity, 0m, item.MinimumStock, item.MaximumStock, true), _departments, _weightUnit, _autoPrice, _defaultProfit, true) { Owner = this };
        if (editor.ShowDialog() == true) await LoadKitsAsync();
    }

    private async void OnComponentSearchChanged(object sender, TextChangedEventArgs e)
    {
        _pendingComponent = null; _componentSearchCancellation?.Cancel(); _componentSearchCancellation = new CancellationTokenSource(); var token = _componentSearchCancellation.Token;
        var query = ComponentSearchBox.Text.Trim(); if (query.Length < 2) { ComponentResults.Visibility = Visibility.Collapsed; return; }
        try
        {
            await Task.Delay(180, token);
            var rows = await ApiClient.Client.GetFromJsonAsync<List<ProductSearchRow>>($"/api/products/search?q={Uri.EscapeDataString(query)}", token) ?? [];
            rows = rows.Where(item => !item.IsKit && (_selected is null || item.Id != _selected.Id)).ToList();
            ComponentResults.ItemsSource = rows; ComponentResults.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { }
    }
    private void OnComponentSelected(object sender, MouseButtonEventArgs e)
    {
        if (ComponentResults.SelectedItem is not ProductSearchRow row) return;
        _pendingComponent = row; ComponentSearchBox.Text = row.Display; ComponentResults.Visibility = Visibility.Collapsed;
    }
    private void OnAddComponentClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { ShowResult("Guarda primero el kit", "Crea o selecciona un kit antes de agregar artículos."); return; }
        if (_pendingComponent is null || !TryDecimal(QuantityBox.Text, out var quantity) || quantity <= 0m) { ShowResult("Revisa el componente", "Selecciona un artículo y captura una cantidad mayor que cero."); return; }
        if (_components.Any(item => item.ComponentProductId == _pendingComponent.Id)) { ShowResult("Artículo ya agregado", "El artículo ya forma parte del kit. Quita la partida y vuelve a agregarla con la cantidad correcta."); return; }
        _components.Add(new KitComponentRow(_pendingComponent.Id, _pendingComponent.Code, _pendingComponent.Description, _pendingComponent.UnitOfMeasure, quantity, _pendingComponent.Cost, _pendingComponent.Price, decimal.Round(_pendingComponent.Cost * quantity, 2), decimal.Round(_pendingComponent.Price * quantity, 2)));
        _pendingComponent = null; ComponentSearchBox.Clear(); QuantityBox.Text = "1"; UpdateSelectedSummary();
    }
    private void OnRemoveComponentClick(object sender, RoutedEventArgs e)
    {
        if (ComponentsGrid.SelectedItem is not KitComponentRow row) { ShowResult("Selecciona un artículo", "Selecciona un artículo de la composición para retirarlo."); return; }
        _components.Remove(row); UpdateSelectedSummary();
    }
    private async void OnSaveCompositionClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _components.Count == 0) { ShowResult("Falta la composición", "El kit debe incluir al menos un artículo antes de guardarlo."); return; }
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("/api/kits", new { kitProductId = _selected.Id, components = _components.Select(item => new { componentProductId = item.ComponentProductId, quantity = item.Quantity }).ToArray() });
            if (!response.IsSuccessStatusCode) { ShowResult("Kit no guardado", await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo guardar la composición."), OperationResultKind.Error); return; }
            StatusText.Text = "Composición guardada. El inventario de los artículos se descontará al vender este kit.";
            await LoadKitsAsync(); await LoadComponentsAsync();
        }
        catch (Exception exception) { ShowResult("Kit no guardado", ConnectionHelp.FromException(exception, "No se pudo guardar la composición."), OperationResultKind.Error); }
    }
    private async void OnDeactivateClick(object sender, RoutedEventArgs e) => await SetStatusAsync(false);
    private async void OnReactivateClick(object sender, RoutedEventArgs e) => await SetStatusAsync(true);
    private async Task SetStatusAsync(bool active)
    {
        if (_selected is null) { ShowResult("Selecciona un kit", "Selecciona un kit de la lista."); return; }
        var action = active ? "reactivar" : "dar de baja";
        var confirmation = new OperationConfirmationWindow(char.ToUpper(action[0]) + action[1..] + " kit", active ? "El kit volverá a estar disponible para vender." : "El kit dejará de venderse, pero conservará su historial y composición.", OperationResultKind.Warning, MessageBoxButton.YesNo) { Owner = this };
        if (confirmation.ShowDialog() != true || confirmation.Result != MessageBoxResult.Yes) return;
        using var response = await ApiClient.Client.PutAsJsonAsync("/api/products/status", new { productIds = new[] { _selected.Id }, isActive = active });
        if (!response.IsSuccessStatusCode) { ShowResult("No se pudo actualizar", await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo actualizar el estado del kit."), OperationResultKind.Error); return; }
        StatusText.Text = active ? "Kit reactivado correctamente." : "Kit dado de baja. Su historial y composición se conservaron."; await LoadKitsAsync();
    }
    private async void OnToggleInactiveClick(object sender, RoutedEventArgs e) { _showInactive = !_showInactive; ShowInactiveText.Text = _showInactive ? "Ocultar bajas" : "Mostrar bajas"; await LoadKitsAsync(); }
    private void UpdateSelectedSummary()
    {
        if (_selected is null) { SelectedTitleText.Text = "Selecciona o crea un kit"; SelectedDetailText.Text = "El precio de venta se edita en los datos del kit."; TotalsText.Text = string.Empty; return; }
        SelectedTitleText.Text = $"{_selected.Code} · {_selected.Description}";
        SelectedDetailText.Text = $"Precio final de venta: {_selected.Price.ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}. El costo estimado se calcula con los artículos incluidos.";
        TotalsText.Text = $"Costo estimado: {_components.Sum(item => item.TotalCost).ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}  |  Precio de referencia de artículos: {_components.Sum(item => item.TotalPrice).ToString("C2", CultureInfo.GetCultureInfo("es-MX"))}";
        DeactivateButton.Visibility = _selected.IsActive ? Visibility.Visible : Visibility.Collapsed; ReactivateButton.Visibility = _selected.IsActive ? Visibility.Collapsed : Visibility.Visible;
    }
    private static KitRow ToRow(ProductEditorWindow.ProductEditModel item) => new(item.Id, item.Code, item.Description, string.Empty, item.DepartmentId, item.Cost, item.Price, item.ProfitPercent, item.WholesalePrice, item.WholesaleProfitPercent, item.WholesaleMinimumQuantity, item.MinimumStock, item.MaximumStock, item.UnitOfMeasure, true, 0);
    private void ShowResult(string title, string message, OperationResultKind kind = OperationResultKind.Warning) => new OperationResultWindow(title, message, kind) { Owner = this }.ShowDialog();
    private static bool TryDecimal(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private sealed record KitRow(Guid Id, string Code, string Description, string Department, Guid? DepartmentId, decimal Cost, decimal Price, decimal ProfitPercent, decimal WholesalePrice, decimal WholesaleProfitPercent, decimal WholesaleMinimumQuantity, decimal MinimumStock, decimal MaximumStock, string UnitOfMeasure, bool IsActive, int ComponentCount) { public string Status => IsActive ? "Activo" : "Baja lógica"; }
    private sealed record ProductSearchRow(Guid Id, string Code, string Description, decimal Price, decimal Cost, bool IsKit, string UnitOfMeasure) { public string Display => $"{Code} | {Description}"; }
    private sealed record KitComponentRow(Guid ComponentProductId, string Code, string Description, string UnitOfMeasure, decimal Quantity, decimal UnitCost, decimal UnitPrice, decimal TotalCost, decimal TotalPrice) { public string Display => $"{Code} | {Description}"; }
}
