using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Win32;
using System.IO;

namespace Pos.Desktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:5000") };
    private CancellationTokenSource? _searchCancellation;
    private Guid? _lastSaleId;
    private readonly ObservableCollection<CartLineView> _cart = [];
    public MainWindow()
    {
        InitializeComponent();
        CartList.ItemsSource = _cart;
        Loaded += (_, _) => ApplyNavigationPermissions();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SessionContext.AccessToken)) Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        try
        {
            using var response = await Client.GetAsync("/api/setup/status");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var configured = document.RootElement.GetProperty("configured").GetBoolean();
            if (configured)
            {
                var storeName = document.RootElement.GetProperty("storeName").GetString();
                StoreNameText.Text = storeName;
                RegisterStatusText.Text = "Caja: configuracion inicial completada";
                StatusText.Text = "API y base de datos locales conectadas.";
            }
            else
            {
                StoreNameText.Text = "Tienda sin configurar";
                StatusText.Text = "Se requiere completar la configuracion inicial.";
            }
        }
        catch (HttpRequestException)
        {
            StoreNameText.Text = "API local no disponible";
            StatusText.Text = "Inicia scripts/dev-up.ps1 para conectar la aplicacion a PostgreSQL.";
        }
    }

    private void OnNavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section })
        {
            if (!HasPermissionFor(section)) { StatusText.Text = "No tienes permiso para abrir este modulo."; return; }
            if (section == "Corte") { OnCloseShiftClick(sender, e); return; }
            if (section == "Inventario") { var window = new InventoryAdjustmentWindow { Owner = this }; window.ShowDialog(); return; }
            if (section == "Clientes") { OpenCustomers(); return; }
            if (section == "Compras") { var window = new PurchaseWindow { Owner = this }; window.ShowDialog(); return; }
            if (section == "Reportes") { var window = new ReportsWindow { Owner = this }; window.ShowDialog(); return; }
            NavigateTo(section);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var section = e.Key switch
        {
            Key.F1 => "Ventas",
            Key.F2 => "Clientes",
            Key.F3 => "Productos",
            Key.F4 => "Inventario",
            _ => null
        };

        if (section is not null)
        {
            if (!HasPermissionFor(section)) { StatusText.Text = "No tienes permiso para abrir este modulo."; e.Handled = true; return; }
            if (section == "Clientes") { OpenCustomers(); e.Handled = true; return; }
            NavigateTo(section);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            OnChargeClick(sender, e);
            e.Handled = true;
        }
    }

    private void OnSalesActionClick(object sender, RoutedEventArgs e) =>
        ShowPendingFeature("Accion de ventas");

    private async void OnOpenShiftClick(object sender, RoutedEventArgs e)
    {
        if (!SessionContext.HasPermission("OpenShift")) { StatusText.Text = "No tienes permiso para abrir turnos."; return; }
        var window = new ShiftWindow { Owner = this };
        if (window.ShowDialog() != true || window.InitialCash is null) return;
        try
        {
            var register = await Client.GetFromJsonAsync<RegisterResponse>("/api/shifts/register");
            if (register is null) { StatusText.Text = "No hay una caja activa configurada."; return; }
            using var response = await Client.PostAsJsonAsync("/api/shifts/open", new { registerId = register.Id, initialCash = window.InitialCash.Value });
            StatusText.Text = response.IsSuccessStatusCode ? "Turno abierto correctamente." : await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo conectar con la API."; }
    }

    private async void OnCashMovementClick(object sender, RoutedEventArgs e)
    {
        if (!SessionContext.HasPermission("RecordCashMovements")) { StatusText.Text = "No tienes permiso para registrar movimientos de efectivo."; return; }
        var window = new CashMovementWindow { Owner = this };
        if (window.ShowDialog() != true || window.Amount is null) return;
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/shifts/cash-movements", new { type = window.Type, amount = window.Amount.Value, reason = window.Reason });
            StatusText.Text = response.IsSuccessStatusCode ? "Movimiento de efectivo registrado." : await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo conectar con la API."; }
    }

    private async void OnCloseShiftClick(object sender, RoutedEventArgs e)
    {
        if (!SessionContext.HasPermission("CloseShift")) { StatusText.Text = "No tienes permiso para cerrar turnos."; return; }
        var window = new CloseShiftWindow { Owner = this };
        if (window.ShowDialog() != true || window.CountedCash is null) return;
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/shifts/close", new { countedCash = window.CountedCash.Value });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<ShiftSummaryResponse>();
            StatusText.Text = result is null ? "Turno cerrado." : $"Turno cerrado. Diferencia: ${result.Difference:0.00}";
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo conectar con la API."; }
    }

    private async void OnProductSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var query = ProductSearchTextBox.Text.Trim();
        if (query.Length == 0) { ProductResultsList.Visibility = Visibility.Collapsed; return; }
        try
        {
            await Task.Delay(180, token);
            var results = await Client.GetFromJsonAsync<List<ProductSearchResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}", token) ?? [];
            ProductResultsList.ItemsSource = results.Select(item => new ProductSearchRow(item)).ToList();
            ProductResultsList.Visibility = results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = results.Count == 0 ? "No se encontraron productos." : $"{results.Count} producto(s) encontrado(s).";
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { StatusText.Text = "No se pudo consultar el catalogo."; }
    }

    private void OnProductSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ProductResultsList.Items.Count == 1)
        {
            ProductResultsList.SelectedIndex = 0;
            StatusText.Text = "Producto seleccionado para la venta.";
            e.Handled = true;
        }
    }

    private void OnProductSelected(object sender, MouseButtonEventArgs e)
    {
        if (ProductResultsList.SelectedItem is not ProductSearchRow row) return;
        var existing = _cart.FirstOrDefault(item => item.ProductId == row.Product.Id);
        if (existing is null) _cart.Add(new CartLineView(row.Product.Id, row.Product.Description, row.Product.Price, 1));
        else { existing.Quantity++; CartList.Items.Refresh(); }
        ProductSearchTextBox.Clear(); ProductResultsList.Visibility = Visibility.Collapsed;
        StatusText.Text = "Producto agregado a la venta.";
    }

    private sealed record ProductSearchResult(Guid Id, string Code, string Description, decimal Price);
    private sealed record ProductSearchRow(ProductSearchResult Product)
    {
        public string DisplayText => $"{Product.Code} | {Product.Description} | ${Product.Price:0.00}";
    }

    private sealed class CartLineView(Guid productId, string description, decimal unitPrice, decimal quantity)
    {
        public Guid ProductId { get; } = productId; public string Description { get; } = description; public decimal UnitPrice { get; } = unitPrice; public decimal Quantity { get; set; } = quantity; public decimal Total => decimal.Round(UnitPrice * Quantity, 2); public string DisplayText => $"{Description} x {Quantity:0.###} = ${Total:0.00}";
    }

    private sealed record SaleResponse(Guid SaleId, decimal Total, decimal Change);
    private sealed record RegisterResponse(Guid Id, string Name);
    private sealed record ShiftSummaryResponse(Guid ShiftId, decimal ExpectedCash, decimal CountedCash, decimal Difference, DateTimeOffset? ClosedAtUtc);

    private async void OnChargeClick(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0) { StatusText.Text = "Agrega al menos un producto antes de cobrar."; return; }
        var cashWindow = new CashWindow(_cart.Sum(item => item.Total)) { Owner = this };
        if (cashWindow.ShowDialog() != true || cashWindow.Received is null) return;
        try
        {
            var command = new { operationId = Guid.NewGuid(), lines = _cart.Select(item => new { productId = item.ProductId, quantity = item.Quantity }).ToArray(), cashReceived = cashWindow.CreditRequested ? 0m : cashWindow.Received.Value, customerId = cashWindow.CustomerId, paymentMethod = cashWindow.CreditRequested ? "Credit" : "Cash" };
            using var response = await Client.PostAsJsonAsync("/api/sales/complete", command);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<SaleResponse>();
            _lastSaleId = result?.SaleId;
            _cart.Clear(); CartList.Items.Refresh(); StatusText.Text = result is null ? "Venta confirmada." : cashWindow.CreditRequested ? "Venta a credito confirmada." : $"Venta confirmada. Cambio: ${result.Change:0.00}";
            if (result is not null) await SaveTicketPdfAsync(result.SaleId);
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo conectar con la API para confirmar la venta."; }
    }

    private async void OnCancelLastSaleClick(object sender, RoutedEventArgs e)
    {
        if (!SessionContext.HasPermission("CancelSales")) { StatusText.Text = "No tienes permiso para cancelar ventas."; return; }
        if (_lastSaleId is null) { StatusText.Text = "No hay una venta reciente para cancelar."; return; }
        var window = new CancelSaleWindow { Owner = this };
        if (window.ShowDialog() != true) return;
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/sales/cancel", new { operationId = Guid.NewGuid(), saleId = _lastSaleId.Value, reason = window.Reason });
            StatusText.Text = response.IsSuccessStatusCode ? "Venta cancelada e inventario revertido." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) _lastSaleId = null;
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo conectar con la API."; }
    }

    private void OnReturnLastSaleClick(object sender, RoutedEventArgs e)
    {
        if (!SessionContext.HasPermission("ProcessReturns")) { StatusText.Text = "No tienes permiso para procesar devoluciones."; return; }
        if (_lastSaleId is null) { StatusText.Text = "No hay una venta reciente para devolver."; return; }
        var window = new ReturnSaleWindow(_lastSaleId.Value) { Owner = this };
        window.ShowDialog();
    }

    private static async Task SaveTicketPdfAsync(Guid saleId)
    {
        using var response = await Client.GetAsync($"/api/sales/{saleId}/ticket.pdf");
        if (!response.IsSuccessStatusCode) return;
        var dialog = new SaveFileDialog { Title = "Guardar ticket PDF", Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"Ticket-{saleId:N}.pdf", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        await File.WriteAllBytesAsync(dialog.FileName, await response.Content.ReadAsByteArrayAsync());
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void NavigateTo(string section)
    {
        CurrentSectionText.Text = section;
        WorkspaceTitleText.Text = section;
        WorkspaceDetailText.Text = $"El modulo {section.ToLowerInvariant()} se implementara en un incremento posterior de la Fase 1.";
        StatusText.Text = $"Navegacion activa: {section}.";
    }

    private void OpenCustomers()
    {
        var window = new CustomerWindow { Owner = this };
        window.ShowDialog();
        CurrentSectionText.Text = "Ventas";
    }

    private void ShowPendingFeature(string feature)
    {
        StatusText.Text = $"{feature} requiere configuracion de tienda, usuario, turno y PostgreSQL. Aun no esta habilitado.";
    }

    private bool HasPermissionFor(string section) => section switch
    {
        "Ventas" => SessionContext.HasPermission("Sell"),
        "Clientes" => SessionContext.HasPermission("ManageCustomersAndCredit"),
        "Productos" => SessionContext.HasPermission("ViewProducts"),
        "Inventario" => SessionContext.HasPermission("ViewInventory"),
        "Corte" => SessionContext.HasPermission("CloseShift"),
        "Configuracion" => SessionContext.HasPermission("ConfigureStore"),
        "Compras" => SessionContext.HasPermission("ManageSuppliersAndPurchases"),
        "Reportes" => SessionContext.HasPermission("ViewReports"),
        _ => false
    };

    private void ApplyNavigationPermissions()
    {
        foreach (var button in FindVisualChildren<Button>(this))
        {
            if (button.Tag is string section) button.IsEnabled = HasPermissionFor(section);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild) yield return typedChild;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
