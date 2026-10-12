using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pos.Desktop;

/// <summary>
/// Recepción de mercancía multiproducto: el cajero escanea lo que entrega el proveedor, ajusta piezas y precio
/// del proveedor en la lista y al confirmar decide precios de venta y si paga con efectivo de caja.
/// Ver docs/plans/3.4.0-compras-multiproducto.md.
/// </summary>
public partial class PurchaseWindow : UserControl
{
    private static HttpClient Client => ApiClient.Client;
    private readonly ObservableCollection<PurchaseReceiptLine> _lines = [];
    private CancellationTokenSource? _searchCancellation;
    private decimal _storeDefaultProfitPercent;
    // false cuando la API en ejecución es anterior a compras multiproducto (no registraría bien el retiro de caja).
    private bool _apiSupportsReception = true;
    // Se conserva hasta que la API confirma, para que un reintento no duplique la compra.
    private Guid _operationId = Guid.NewGuid();
    private MobilePurchaseRequestReview? _mobileRequest;
    private bool _submitting;

    public PurchaseWindow()
    {
        InitializeComponent();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        ReceiptList.ItemsSource = _lines;
        _lines.CollectionChanged += (_, _) => UpdateTotals();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BarcodeScannerService.BarcodeScanned += OnBarcodeScanned;
        FocusSearch();
        await LoadSuppliersAsync();
        await LoadPricingDefaultsAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => BarcodeScannerService.BarcodeScanned -= OnBarcodeScanned;

    private async Task LoadSuppliersAsync(Guid? selectId = null)
    {
        try
        {
            var suppliers = await Client.GetFromJsonAsync<List<PurchaseSupplier>>("/api/suppliers") ?? [];
            var items = new[] { PurchaseSupplier.None }.Concat(suppliers).ToList();
            SupplierComboBox.ItemsSource = items;
            SupplierComboBox.SelectedItem = selectId is Guid id ? items.FirstOrDefault(item => item.Id == id) ?? items[0] : items[0];
        }
        catch (Exception exception) { ShowMessage(ConnectionHelp.FromException(exception, "No se pudieron cargar los proveedores"), MessageKind.Error); }
    }

    private async Task LoadPricingDefaultsAsync()
    {
        try
        {
            using var response = await Client.GetAsync("/api/purchases/pricing-defaults");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _apiSupportsReception = false;
                ShowMessage(OutdatedApiMessage, MessageKind.Error);
                return;
            }
            _apiSupportsReception = true;
            var defaults = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PurchasePricingDefaults>() : null;
            _storeDefaultProfitPercent = defaults?.DefaultProfitPercent ?? 0m;
        }
        catch (Exception) { _storeDefaultProfitPercent = 0m; }
    }

    private async void OnCreateSupplierClick(object sender, RoutedEventArgs e)
    {
        var name = NewSupplierTextBox.Text.Trim();
        if (name.Length == 0) { ShowMessage("Escribe el nombre del proveedor.", MessageKind.Warning); NewSupplierTextBox.Focus(); return; }
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/suppliers", new { name });
            if (!response.IsSuccessStatusCode) { ShowMessage(await PurchasePricing.ReadErrorAsync(response), MessageKind.Error); return; }
            var created = await response.Content.ReadFromJsonAsync<PurchaseSupplier>();
            NewSupplierTextBox.Clear();
            await LoadSuppliersAsync(created?.Id);
            ShowMessage($"Proveedor {name} creado y seleccionado.", MessageKind.Success);
            FocusSearch();
        }
        catch (Exception exception) { ShowMessage(ConnectionHelp.FromException(exception, "No se pudo crear el proveedor"), MessageKind.Error); }
    }

    // ---------- Búsqueda y escaneo ----------

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0) { HideResults(); return; }
        try
        {
            await Task.Delay(180, token);
            var data = await Client.GetFromJsonAsync<List<PurchaseProduct>>($"/api/products/search?q={Uri.EscapeDataString(query)}", token) ?? [];
            if (token.IsCancellationRequested) return;
            ResultsList.ItemsSource = data.Select(item => new ResultRow(item)).ToList();
            ResultsList.SelectedIndex = -1;
            ResultsList.Visibility = data.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ShowMessage(ConnectionHelp.FromException(exception, "No se pudo buscar"), MessageKind.Error); }
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        var resultsVisible = ResultsList.Visibility == Visibility.Visible && ResultsList.Items.Count > 0;
        switch (e.Key)
        {
            case Key.Down or Key.Up:
                if (resultsVisible)
                {
                    var current = ResultsList.SelectedIndex;
                    ResultsList.SelectedIndex = e.Key == Key.Down
                        ? Math.Min(current < 0 ? 0 : current + 1, ResultsList.Items.Count - 1)
                        : Math.Max(current <= 0 ? 0 : current - 1, 0);
                    ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                }
                else MoveLineSelection(e.Key == Key.Down);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = true;
                var query = SearchTextBox.Text.Trim();
                if (query.Length == 0) { EditSelectedQuantity(); break; }
                if (resultsVisible && ResultsList.SelectedItem is ResultRow row) AddProduct(row.Product);
                else await AddByQueryAsync(query);
                break;
            case Key.Delete when SearchTextBox.Text.Length == 0:
                RemoveSelectedLine();
                e.Handled = true;
                break;
            case Key.Escape when resultsVisible:
                HideResults();
                e.Handled = true;
                break;
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ResultRow row) AddProduct(row.Product);
    }

    private void OnBarcodeScanned(object? sender, string code) => Dispatcher.BeginInvoke(async () =>
    {
        if (!IsVisible) return;
        SearchTextBox.Text = code;
        await AddByQueryAsync(code.Trim());
    });

    /// <summary>Busca primero el código exacto (alfanumérico) y, si no, una única coincidencia por descripción.</summary>
    private async Task AddByQueryAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        _searchCancellation?.Cancel();
        try
        {
            var products = await Client.GetFromJsonAsync<List<PurchaseProduct>>($"/api/products/search?q={Uri.EscapeDataString(query)}") ?? [];
            var selected = products.FirstOrDefault(item => string.Equals(item.Code.Trim(), query, StringComparison.OrdinalIgnoreCase))
                ?? (products.Count == 1 ? products[0] : null);
            if (selected is not null) { AddProduct(selected); return; }
            if (products.Count == 0)
            {
                HideResults();
                ShowMessage($"No se encontró ningún producto con \"{query}\". Dalo de alta en Productos (F3) y vuelve a escanearlo.", MessageKind.Error);
                SearchTextBox.SelectAll();
                return;
            }
            ResultsList.ItemsSource = products.Select(item => new ResultRow(item)).ToList();
            ResultsList.Visibility = Visibility.Visible;
            ResultsList.SelectedIndex = 0;
            ShowMessage("Hay varias coincidencias: elige con ↑/↓ y Enter.", MessageKind.Warning);
        }
        catch (Exception exception) { ShowMessage(ConnectionHelp.FromException(exception, "No se pudo buscar el producto"), MessageKind.Error); }
    }

    /// <summary>Agrega el producto a la lista; si ya está, suma una pieza.</summary>
    private void AddProduct(PurchaseProduct product)
    {
        var line = _lines.FirstOrDefault(item => item.Product.Id == product.Id);
        if (line is null)
        {
            line = new PurchaseReceiptLine(product);
            line.Changed += (_, _) => UpdateTotals();
            _lines.Add(line);
        }
        else
        {
            line.AddPieces(1m);
        }
        ReceiptList.SelectedItem = line;
        ReceiptList.ScrollIntoView(line);
        _searchCancellation?.Cancel();
        SearchTextBox.Clear();
        HideResults();
        ShowMessage($"{product.Description}: {line.QuantityText} pieza(s) en la lista.", MessageKind.Success);
        FocusSearch();
    }

    // ---------- Lista ----------

    private void MoveLineSelection(bool moveDown)
    {
        if (_lines.Count == 0) return;
        var current = ReceiptList.SelectedIndex;
        var next = current < 0 ? (moveDown ? 0 : _lines.Count - 1) : Math.Clamp(current + (moveDown ? 1 : -1), 0, _lines.Count - 1);
        ReceiptList.SelectedIndex = next;
        ReceiptList.ScrollIntoView(ReceiptList.SelectedItem);
        if (ReceiptList.SelectedItem is PurchaseReceiptLine line)
            ShowMessage($"Partida {next + 1} de {_lines.Count}: {line.Description}. Supr: quitar · Enter: editar piezas.", MessageKind.Info);
    }

    private void OnReceiptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.Delete) { RemoveSelectedLine(); e.Handled = true; }
        else if (e.Key == Key.Enter) { EditSelectedQuantity(); e.Handled = true; }
    }

    private void OnReceiptDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<TextBox>(source) is not null) return;
        EditSelectedQuantity();
    }

    private void OnCellGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (box.DataContext is PurchaseReceiptLine line) ReceiptList.SelectedItem = line;
        box.Dispatcher.BeginInvoke(box.SelectAll);
    }

    private void OnRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PurchaseReceiptLine line) RemoveLine(line);
    }

    private void RemoveSelectedLine()
    {
        if (ReceiptList.SelectedItem is PurchaseReceiptLine line) RemoveLine(line);
        else ShowMessage("Selecciona con ↑/↓ el producto que quieres quitar.", MessageKind.Warning);
    }

    private void RemoveLine(PurchaseReceiptLine line)
    {
        var index = _lines.IndexOf(line);
        _lines.Remove(line);
        ReceiptList.SelectedIndex = _lines.Count == 0 ? -1 : Math.Min(index, _lines.Count - 1);
        ShowMessage($"{line.Description} se quitó de la lista.", MessageKind.Info);
        FocusSearch();
    }

    private void EditSelectedQuantity()
    {
        if (ReceiptList.SelectedItem is not PurchaseReceiptLine line)
        {
            ShowMessage(_lines.Count == 0 ? "Escanea un producto para agregarlo." : "Selecciona con ↑/↓ el producto que quieres editar.", MessageKind.Info);
            return;
        }
        var window = new SaleQuantityWindow(line.Description, line.UnitOfMeasure, line.Quantity ?? 1m) { Owner = Window.GetWindow(this), Title = "Piezas recibidas" };
        if (window.ShowDialog() == true && window.Quantity is decimal quantity)
        {
            line.QuantityText = quantity.ToString("0.###", CultureInfo.CurrentCulture);
            ShowMessage($"{line.Description}: {line.QuantityText} pieza(s).", MessageKind.Success);
        }
        FocusSearch();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_lines.Count == 0) { FocusSearch(); return; }
        var answer = MessageBox.Show($"¿Vaciar la lista de {_lines.Count} producto(s)? No se registrará nada.", "Vaciar lista", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _lines.Clear();
        _mobileRequest = null;
        _operationId = Guid.NewGuid();
        ShowMessage("Lista vacía. Escanea el primer producto.", MessageKind.Info);
        FocusSearch();
    }

    private void UpdateTotals()
    {
        var total = _lines.Sum(item => item.LineTotal);
        var pieces = _lines.Sum(item => item.Quantity ?? 0m);
        TotalText.Text = total.ToString("C2", CultureInfo.CurrentCulture);
        SummaryText.Text = $"{_lines.Count} producto(s) · {pieces:0.###} pieza(s)";
    }

    // ---------- Confirmación ----------

    internal const string OutdatedApiMessage = "La API de JetVenta que está corriendo es una versión anterior: no tiene el historial de compras ni registra el efectivo retirado. Cierra JetVenta y la API, vuelve a compilar e inicia con scripts\\dev-up.ps1.";

    private async void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_submitting) return;
        if (!_apiSupportsReception)
        {
            await LoadPricingDefaultsAsync();
            if (!_apiSupportsReception)
            {
                new OperationResultWindow("API desactualizada", OutdatedApiMessage, OperationResultKind.Error) { Owner = Window.GetWindow(this) }.ShowDialog();
                return;
            }
        }
        if (_lines.Count == 0) { ShowMessage("La lista está vacía. Escanea los productos que entrega el proveedor.", MessageKind.Warning); FocusSearch(); return; }
        var invalid = _lines.FirstOrDefault(item => !item.IsValid);
        if (invalid is not null)
        {
            ReceiptList.SelectedItem = invalid;
            ReceiptList.ScrollIntoView(invalid);
            ShowMessage($"Revisa {invalid.Description}: las piezas deben ser mayores que cero y el precio del proveedor no puede ser negativo.", MessageKind.Error);
            return;
        }

        var owner = Window.GetWindow(this);
        // 1) Precios de venta: actualizar con %, conservar o capturar.
        var reviewRows = PurchasePriceReviewRow.Build(_lines, _storeDefaultProfitPercent);
        if (reviewRows.Count > 0)
        {
            var review = new PurchasePriceReviewWindow(reviewRows) { Owner = owner };
            if (review.ShowDialog() != true) { ShowMessage("Recepción pendiente: no se registró nada.", MessageKind.Info); return; }
        }
        var decisions = reviewRows.ToDictionary(item => item.Line.Product.Id, item => item.ToCommand());

        // 2) Pago: ¿sale dinero de caja? ¿cuánto se retiró?
        var total = _lines.Sum(item => item.LineTotal);
        var pieces = _lines.Sum(item => item.Quantity ?? 0m);
        List<PurchaseSupplier> suppliers = (SupplierComboBox.ItemsSource as IEnumerable<PurchaseSupplier>)?.ToList() ?? [PurchaseSupplier.None];
        var payment = new PurchasePaymentWindow(suppliers, SupplierComboBox.SelectedItem as PurchaseSupplier ?? PurchaseSupplier.None, total, _lines.Count, pieces,
            _mobileRequest?.ProposedPayFromCashRegister ?? true, _mobileRequest?.ProposedCashWithdrawn) { Owner = owner };
        if (payment.ShowDialog() != true) { ShowMessage("Recepción pendiente: no se registró nada.", MessageKind.Info); return; }
        // El proveedor pudo elegirse en la ventana de pago.
        var supplier = payment.SelectedSupplier;
        SupplierComboBox.SelectedItem = supplier;

        var lines = _lines.Select(item =>
        {
            var decision = decisions.TryGetValue(item.Product.Id, out var value) ? value : (SalePrice: (decimal?)null, ProfitPercentToApply: (decimal?)null);
            return new { productId = item.Product.Id, quantity = item.Quantity!.Value, unitCost = item.UnitCost!.Value, salePrice = decision.SalePrice, profitPercentToApply = decision.ProfitPercentToApply };
        }).ToList();

        _submitting = true;
        ConfirmButton.IsEnabled = false;
        try
        {
            using var response = _mobileRequest is null
                ? await Client.PostAsJsonAsync("/api/purchases/receive", new
                {
                    operationId = _operationId,
                    supplierId = supplier.Id,
                    payFromCashRegister = payment.PayFromCashRegister,
                    cashWithdrawn = payment.CashWithdrawn,
                    notes = payment.Notes,
                    lines
                })
                : await Client.PostAsJsonAsync($"/api/purchases/mobile-requests/{_mobileRequest.Id}/approve", new
                {
                    supplierId = supplier.Id,
                    payFromCashRegister = payment.PayFromCashRegister,
                    cashWithdrawn = payment.CashWithdrawn,
                    notes = payment.Notes,
                    lines
                });
            if (!response.IsSuccessStatusCode)
            {
                var error = await PurchasePricing.ReadErrorAsync(response);
                ShowMessage(error, MessageKind.Error);
                new OperationResultWindow("No se registró la compra", error, OperationResultKind.Error) { Owner = owner }.ShowDialog();
                return;
            }
            var result = _mobileRequest is null
                ? await response.Content.ReadFromJsonAsync<PurchaseReceiveApiResult>()
                : (await response.Content.ReadFromJsonAsync<MobilePurchaseApprovalResult>())?.Purchase;
            var culture = CultureInfo.CurrentCulture;
            var cashMessage = result?.PaidFromCashRegister == true && result.CashWithdrawn is decimal withdrawn
                ? string.Create(culture, $" Salieron {withdrawn:C2} de caja; se restarán del efectivo esperado en el corte.")
                : payment.PayFromCashRegister ? " No se registró salida de efectivo (retiro de $0)." : " No salió dinero de caja.";
            var summary = string.Create(culture, $"Se recibieron {_lines.Count} producto(s), {pieces:0.###} pieza(s), por {(result?.Total ?? total):C2} de {supplier.Name}.{cashMessage}");
            _lines.Clear();
            _mobileRequest = null;
            _operationId = Guid.NewGuid();
            ShowMessage(summary, MessageKind.Success);
            new OperationResultWindow(result?.Existing == true ? "Compra ya registrada" : "Compra registrada", summary, OperationResultKind.Success) { Owner = owner }.ShowDialog();
        }
        catch (Exception exception)
        {
            ShowMessage(exception is HttpRequestException or TaskCanceledException ? ConnectionHelp.ApiUnavailableNotConfirmed : $"No se pudo registrar la compra: {exception.Message}", MessageKind.Error);
        }
        finally
        {
            _submitting = false;
            ConfirmButton.IsEnabled = true;
            FocusSearch();
        }
    }

    private void OnPlanningClick(object sender, RoutedEventArgs e) => new PurchasePlanningWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void OnHistoryClick(object sender, RoutedEventArgs e) => new PurchaseHistoryWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private async void OnMobileRequestsClick(object sender, RoutedEventArgs e)
    {
        var window = new MobilePurchaseRequestsWindow { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.SelectedRequest is not MobilePurchaseRequestReview request) return;
        if (_lines.Count > 0 && MessageBox.Show("La lista actual se reemplazará por la solicitud móvil. Aún no se ha registrado.",
                "Cargar solicitud", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

        _lines.Clear();
        _mobileRequest = request;
        _operationId = request.OperationId;
        await LoadSuppliersAsync(request.SupplierId);
        foreach (var item in request.Lines)
        {
            var product = new PurchaseProduct(item.ProductId, item.Code, item.Description, item.Price, item.Cost, item.Stock, item.UnitOfMeasure, item.ProfitPercent);
            var line = new PurchaseReceiptLine(product)
            {
                QuantityText = item.Quantity.ToString("0.###", CultureInfo.CurrentCulture),
                UnitCostText = item.UnitCost.ToString("0.00", CultureInfo.CurrentCulture)
            };
            line.Changed += (_, _) => UpdateTotals();
            _lines.Add(line);
        }
        ShowMessage($"Solicitud de {request.CreatedBy} en {request.DeviceName} cargada. Revisa las piezas y continúa con precios y pago.", MessageKind.Info);
        UpdateTotals();
    }

    // ---------- Utilidades ----------

    private void HideResults()
    {
        ResultsList.ItemsSource = null;
        ResultsList.Visibility = Visibility.Collapsed;
    }

    private void FocusSearch() => Dispatcher.BeginInvoke(() => { SearchTextBox.Focus(); Keyboard.Focus(SearchTextBox); });

    private enum MessageKind { Info, Success, Warning, Error }

    private void ShowMessage(string message, MessageKind kind)
    {
        MessageText.Text = message;
        MessageText.Foreground = kind switch
        {
            MessageKind.Success => new SolidColorBrush(Color.FromRgb(0x16, 0x83, 0x4A)),
            MessageKind.Warning => new SolidColorBrush(Color.FromRgb(0xA9, 0x63, 0x00)),
            MessageKind.Error => new SolidColorBrush(Color.FromRgb(0xC3, 0x3B, 0x32)),
            _ => new SolidColorBrush(Color.FromRgb(0x31, 0x56, 0x6D))
        };
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    private sealed record ResultRow(PurchaseProduct Product)
    {
        public string DisplayText => $"{Product.Code} | {Product.Description} | Costo ${Product.Cost:0.00} | Venta ${Product.Price:0.00} | Existencia {Product.Stock:0.###} {Product.UnitOfMeasure}";
    }
}
