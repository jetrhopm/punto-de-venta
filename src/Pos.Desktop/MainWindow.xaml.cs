using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Win32;
using System.IO;
using System.Media;
using Pos.Printing;

namespace Pos.Desktop;

public partial class MainWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private CancellationTokenSource? _searchCancellation;
    private Guid? _lastSaleId;
    private readonly ObservableCollection<TicketTabView> _tickets = [];
    private readonly ObservableCollection<CartLineView> _emptyCart = [];
    private readonly SemaphoreSlim _draftSaveLock = new(1, 1);
    private TicketTabView? _activeTicket;
    private bool _discardInProgress;
    private bool _exitConfirmed;
    private bool _exitDialogOpen;
    private bool _openingProductLookup;
    private bool _roundSaleAmounts;
    private string _roundingMode = "Tenths";
    private TemporaryPermissionLease? _modulePermissionLease;
    private string? _licenseReminder;
    public MainWindow()
    {
        InitializeComponent();
        CashierNameText.Text = string.IsNullOrWhiteSpace(SessionContext.DisplayName) ? "Usuario" : SessionContext.DisplayName.Trim();
        TicketTabs.ItemsSource = _tickets;
        CartList.ItemsSource = _emptyCart;
        Loaded += (_, _) => ApplyNavigationPermissions();
        Closing += OnClosing;
        PreviewTextInput += OnPreviewTextInput;
        BarcodeScannerService.BarcodeScanned += OnSerialBarcodeScanned;
    }

    public void ShowLicenseReminder(string message)
    {
        _licenseReminder = message;
        LicenseDemoText.Text = message;
        LicenseDemoBanner.Visibility = Visibility.Visible;
        StatusText.Text = message;
    }

    private async void OnOpenLicenseClick(object sender, RoutedEventArgs e)
    {
        var window = new LicenseWindow { Owner = this };
        window.ShowDialog();
        try
        {
            var status = await Client.GetFromJsonAsync<LicenseStatusResponse>("/api/license/status");
            if (status?.IsActive == true && !string.Equals(status.State, "trial", StringComparison.OrdinalIgnoreCase))
            {
                _licenseReminder = null;
                LicenseDemoBanner.Visibility = Visibility.Collapsed;
                StatusText.Text = "Licencia activa para este equipo.";
            }
            else if (status is not null)
            {
                ShowLicenseReminder(status.Message);
            }
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApiClient.ApplySession(SessionContext.AccessToken);
        BarcodeScannerService.StartConfiguredProfile();
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
                StatusText.Text = _licenseReminder ?? "API y base de datos locales conectadas.";
                await LoadSalePricingOptionsAsync();
                await EnsureShiftOpenAfterLoginAsync();
                FocusProductInput();
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
            StatusText.Text = ConnectionHelp.ApiUnavailable;
        }
    }

    private async void OnNavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section }) await OpenSectionAsync(section);
    }

    private async Task OpenSectionAsync(string section)
    {
        if (section == "Ventas") { ShowSalesWorkspace(); return; }

        ReleaseModulePermission();
        var requiredPermission = PermissionFor(section);
        TemporaryPermissionLease? lease = null;
        if (requiredPermission is not null)
        {
            lease = await PermissionAuthorization.RequestAsync(this, requiredPermission, $"{SectionNameFor(section)} requiere autorización para abrirse.");
            if (lease is null)
            {
                StatusText.Text = $"No tienes permiso para {PermissionAuthorization.NameFor(requiredPermission).ToLowerInvariant()}. Pide al administrador que lo asigne en Configuración > Cajeros y permisos, o que autorice esta acción.";
                return;
            }
        }

        try
        {
            if (section == "Corte")
            {
                var module = new CutWindow();
                module.CloseRequested += (_, _) => ShowSalesWorkspace();
                module.CloseShiftRequested += async (_, _) =>
                {
                    if (await CloseShiftFromDialogAsync(openNewShift: true, _modulePermissionLease)) ShowSalesWorkspace();
                };
                ShowEmbeddedModule(module, "Corte", "Consulta cortes sin cerrar los tickets en atención.", lease);
                lease = null;
                return;
            }
            if (section == "Productos") { ShowEmbeddedModule(new ProductCatalogWindow(), "Productos", "Administra el catálogo sin cerrar los tickets en atención.", lease); lease = null; return; }
            if (section == "Inventario") { OpenInventory(lease); lease = null; return; }
            if (section == "Clientes") { OpenCustomers(false, lease); lease = null; return; }
            if (section == "Creditos") { OpenCustomers(true, lease); lease = null; return; }
            if (section == "Compras") { ShowEmbeddedModule(new PurchaseWindow(), "Compras", "Registra compras sin cerrar los tickets en atención.", lease); lease = null; return; }
            if (section == "Reportes") { ShowEmbeddedModule(new ReportsWindow(), "Reportes", "Consulta ventas y análisis sin cerrar los tickets en atención.", lease); lease = null; return; }
            if (section == "Historial") { ShowEmbeddedModule(new SalesHistoryWindow(), "Historial", "Consulta ventas anteriores sin cerrar los tickets en atención.", lease); lease = null; return; }
            if (section == "Facturas") { ShowEmbeddedModule(new InvoicePlaceholderView(), "Facturas", "El módulo CFDI permanece deshabilitado hasta completar su validación fiscal.", lease); lease = null; return; }
            if (section == "Promociones") { var window = new PromotionWindow { Owner = this }; window.ShowDialog(); return; }
            if (section == "Configuracion") { ShowEmbeddedModule(new ConfigurationWindow(), "Configuración", "Administra JetVenta sin cerrar los tickets en atención.", lease); lease = null; return; }
            if (section == "Kits") { var window = new KitWindow { Owner = this }; window.ShowDialog(); return; }
            NavigateTo(section);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
        }
    }

    private async void OnPairingCodeClick(object sender, RoutedEventArgs e)
    {
        await using var authorization = await PermissionAuthorization.RequestAsync(this, "ConfigureStore", "Generar un código para emparejar una caja requiere autorización.");
        if (authorization is null) return;
        new PairingCodeWindow { Owner = this }.ShowDialog();
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var section = key switch
        {
            Key.F1 => "Ventas",
            Key.F2 => "Creditos",
            Key.F3 => "Productos",
            Key.F4 => "Inventario",
            _ => null
        };

        if (section is not null)
        {
            await OpenSectionAsync(section);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.D1 or Key.NumPad1)
        {
            OnDiscardTicketClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F12)
        {
            OnChargeClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            SelectNextTicket();
            e.Handled = true;
        }
        else if (e.Key == Key.F6)
        {
            OnNewTicketClick(sender, e);
            e.Handled = true;
        }
        else if (key == Key.F10)
        {
            await OpenProductLookupAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.F9)
        {
            OpenPriceVerifier();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ShowPendingFeature("Mayoreo manual");
            e.Handled = true;
        }
        else if (e.Key == Key.F7)
        {
            OpenCashMovement("In");
            e.Handled = true;
        }
        else if (e.Key == Key.F8)
        {
            OpenCashMovement("Out");
            e.Handled = true;
        }
        else if (e.Key == Key.Insert)
        {
            await OpenCommonProductAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            await OpenCommonProductAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteSelectedCartLine();
            e.Handled = true;
        }
    }

    private void OnSalesActionClick(object sender, RoutedEventArgs e) =>
        ShowPendingFeature("Accion de ventas");

    private async void OnFindCustomerClick(object sender, RoutedEventArgs e) => await SelectCustomerForActiveTicketAsync();
    private async void OnSelectCustomerClick(object sender, RoutedEventArgs e) => await SelectCustomerForActiveTicketAsync();
    private async void OnTicketHistoryClick(object sender, RoutedEventArgs e)
    {
        await OpenSectionAsync("Historial");
    }

    private async void OnPrintLastTicketClick(object sender, RoutedEventArgs e)
    {
        await using var authorization = await PermissionAuthorization.RequestAsync(this, "ReprintTickets", "Reimprimir el último ticket requiere autorización.");
        if (authorization is null) return;
        try
        {
            var saleId = _lastSaleId;
            if (saleId is null)
            {
                var from = DateTimeOffset.UtcNow.AddDays(-30).ToString("O");
                var to = DateTimeOffset.UtcNow.ToString("O");
                var recent = await Client.GetFromJsonAsync<List<LatestSaleRow>>($"/api/sales/history?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
                saleId = recent?.FirstOrDefault()?.SaleId;
            }

            if (saleId is null) { StatusText.Text = "No hay una compra confirmada para imprimir."; return; }
            StatusText.Text = await OutputTicketAsync(saleId.Value);
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailableRetry; }
        catch (Exception exception) { StatusText.Text = $"No se pudo imprimir el último ticket: {exception.Message}"; }
    }

    private async Task SelectCustomerForActiveTicketAsync()
    {
        if (_activeTicket is null) return;
        await using var authorization = await PermissionAuthorization.RequestAsync(this, "ManageCustomersAndCredit", "Seleccionar un cliente para la venta requiere autorización.");
        if (authorization is null) return;
        var window = new CustomerWindow(true) { Owner = this };
        if (window.ShowDialog() != true || window.SelectedCustomerId is null) return;
        _activeTicket.SetCustomer(window.SelectedCustomerId.Value, window.SelectedCustomerName ?? "Cliente");
        TicketTabs.Items.Refresh();
        CurrentCustomerText.Text = window.SelectedCustomerName ?? "Cliente";
        UpdateSaleSummary();
        await PersistActiveTicketAsync(showError: true);
        StatusText.Text = $"Cliente seleccionado: {window.SelectedCustomerName}.";
        FocusProductInput();
    }

    private void OnChangeTicketClick(object sender, RoutedEventArgs e) => SelectNextTicket();

    private async void OnAddProductFromEntryClick(object sender, RoutedEventArgs e) =>
        await HandleProductEntryAsync(ProductSearchTextBox.Text.Trim());

    private async void OnInsertCommonProductClick(object sender, RoutedEventArgs e) =>
        await OpenCommonProductAsync();

    private async void OnCommonProductClick(object sender, RoutedEventArgs e)
    {
        await OpenCommonProductAsync();
    }

    private async void OnProductLookupClick(object sender, RoutedEventArgs e) =>
        await OpenProductLookupAsync();

    private void OnPriceVerifierClick(object sender, RoutedEventArgs e) =>
        OpenPriceVerifier();

    private void OnWholesaleClick(object sender, RoutedEventArgs e) =>
        ShowPendingFeature("Mayoreo manual");

    private void OnCashInClick(object sender, RoutedEventArgs e) =>
        OpenCashMovement("In");

    private void OnCashOutClick(object sender, RoutedEventArgs e) =>
        OpenCashMovement("Out");

    private void OnDeleteSelectedLineClick(object sender, RoutedEventArgs e) =>
        DeleteSelectedCartLine();

    private async Task OpenProductLookupAsync()
    {
        if (_openingProductLookup) return;
        _openingProductLookup = true;
        try
        {
            await using var authorization = await PermissionAuthorization.RequestAsync(this, "ViewProducts", "Consultar productos requiere autorización.");
            if (authorization is null) return;

            var lookup = new ProductLookupWindow(ProductSearchTextBox.Text.Trim()) { Owner = this };
            if (lookup.ShowDialog() != true || lookup.SelectedProduct is null) return;

            var product = lookup.SelectedProduct;
            await AddProductToCartAsync(new ProductSearchResult(product.Id, product.Code, product.Description, product.Price, product.Stock));
        }
        finally
        {
            _openingProductLookup = false;
        }
    }

    private async void OpenPriceVerifier()
    {
        await using var authorization = await PermissionAuthorization.RequestAsync(this, "ViewProducts", "Verificar precios requiere autorización.");
        if (authorization is null) return;

        new PriceVerifierWindow { Owner = this }.ShowDialog();
    }

    private async Task<bool> OpenShiftFromDialogAsync()
    {
        var window = new ShiftWindow { Owner = this };
        if (window.ShowDialog() != true || window.InitialCash is null) return false;
        try
        {
            var register = await Client.GetFromJsonAsync<RegisterResponse>("/api/shifts/register");
            if (register is null) { StatusText.Text = "No hay una caja activa configurada."; return false; }
            using var response = await Client.PostAsJsonAsync("/api/shifts/open", new { registerId = register.Id, initialCash = window.InitialCash.Value });
            if (response.IsSuccessStatusCode)
            {
                StatusText.Text = "Turno abierto correctamente.";
                return true;
            }

            StatusText.Text = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Este usuario no tiene permiso para abrir caja. Pide a un administrador ajustar sus permisos.",
                System.Net.HttpStatusCode.Conflict => await ShowOpenShiftConflictAsync(response),
                _ => $"No se pudo abrir caja. Codigo {(int)response.StatusCode}."
            };
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; return false; }
        catch (Exception exception) { StatusText.Text = $"No se pudo abrir caja: {exception.Message}"; return false; }
    }

    private static async Task<string> ReadApiMessageAsync(HttpResponseMessage response)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? "No se pudo completar la operacion." : "No se pudo completar la operacion.";
        }
        catch (JsonException)
        {
            return "No se pudo completar la operacion.";
        }
    }

    private async void OnCashMovementClick(object sender, RoutedEventArgs e) => await OpenCashMovementAsync(null);

    private async void OnOpenCashDrawerClick(object sender, RoutedEventArgs e)
    {
        await using var authorization = await PermissionAuthorization.RequestAsync(this, "OpenCashDrawer", "Abrir el cajón de dinero requiere autorización.");
        if (authorization is null) return;

        await TryOpenCashDrawerAsync(explainIfDisabled: true);
    }

    private void OpenCashMovement(string type) => _ = OpenCashMovementAsync(type);

    private async Task OpenCashMovementAsync(string? type)
    {
        await using var authorization = await PermissionAuthorization.RequestAsync(this, "RecordCashMovements", "Registrar una entrada o salida de efectivo requiere autorización.");
        if (authorization is null) return;
        var window = new CashMovementWindow(type) { Owner = this };
        if (window.ShowDialog() != true || window.Amount is null) return;
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/shifts/cash-movements", new { type = window.Type, amount = window.Amount.Value, reason = window.Reason });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            StatusText.Text = "Movimiento de efectivo registrado.";
            await TryOpenCashDrawerAsync();
            if (window.Type == "Out")
            {
                if (string.IsNullOrWhiteSpace(ApiClient.PrinterName)) StatusText.Text += " No hay impresora configurada; no se imprimió comprobante.";
                else
                {
                    try { TicketWindowsPrinter.PrintCashMovement(ApiClient.PrinterName, window.Amount.Value, window.Type, window.Reason, window.ProviderName, TicketWindowsPrinter.CurrentProfile); StatusText.Text += " Comprobante enviado a la impresora."; }
                    catch (Exception exception) { StatusText.Text += $" La salida quedó registrada, pero no se pudo imprimir: {exception.Message}"; }
                }
            }
            await NotifyCashLimitAsync();
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private async void OnCloseShiftClick(object sender, RoutedEventArgs e)
    {
        await CloseShiftFromDialogAsync(openNewShift: true);
    }

    private async Task<bool> CloseShiftFromDialogAsync(bool openNewShift = false, TemporaryPermissionLease? existingAuthorization = null)
    {
        var borrowedAuthorization = existingAuthorization?.Permission == "CloseShift" ? existingAuthorization : null;
        await using var requestedAuthorization = borrowedAuthorization is null
            ? await PermissionAuthorization.RequestCriticalActionAsync(this, "CloseShift", "Cerrar el turno y realizar el corte requiere autorización.")
            : null;
        var authorization = borrowedAuthorization ?? requestedAuthorization;
        if (authorization is null)
        {
            StatusText.Text = "No tienes permiso para cerrar turno. Pide al administrador que lo autorice o que active \"Realizar corte del turno propio y ver efectivo esperado\" en Configuración > Cajeros y permisos.";
            return false;
        }

        HttpResponseMessage summaryResponse;
        try { summaryResponse = await Client.GetAsync("/api/shifts/summary"); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            StatusText.Text = await DescribeCloseConnectionFailureAsync();
            return false;
        }
        if (!summaryResponse.IsSuccessStatusCode)
        {
            StatusText.Text = await ReadApiMessageAsync(summaryResponse);
            summaryResponse.Dispose();
            return false;
        }

        var summary = await summaryResponse.Content.ReadFromJsonAsync<ShiftSummaryResponse>();
        summaryResponse.Dispose();
        if (summary is null) { StatusText.Text = "No se pudo calcular el efectivo esperado."; return false; }
        CutSettingsResponse? cutSettings = null;
        try { cutSettings = await Client.GetFromJsonAsync<CutSettingsResponse>("/api/cut-settings"); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            StatusText.Text = await DescribeCloseConnectionFailureAsync();
            return false;
        }
        decimal? countedCash;
        if (cutSettings?.RequireCashCountOnClose != false)
        {
            var window = new CloseShiftWindow(summary.ExpectedCash) { Owner = this };
            if (window.ShowDialog() != true || window.CountedCash is null) return false;
            countedCash = window.CountedCash.Value;
        }
        else
        {
            if (MessageBox.Show("Se cerrará el turno sin solicitar efectivo contado ni registrar ajuste. ¿Deseas continuar?", "Cerrar turno", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
            countedCash = null;
        }

        try
        {
            using var response = await Client.PostAsJsonAsync("/api/shifts/close", new { countedCash, authorizationGrantId = authorization.GrantId });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "La autorización para cerrar el turno no fue aceptada. Solicita nuevamente la autorización del administrador."
                    : await ReadApiMessageAsync(response);
                return false;
            }
            var result = await response.Content.ReadFromJsonAsync<ShiftSummaryResponse>();
            if (result is null) { StatusText.Text = "Turno cerrado."; return true; }
            StatusText.Text = $"Turno cerrado. Diferencia: ${result.Difference:0.00}";
            new ShiftCloseSummaryWindow(result.ExpectedCash, result.CountedCash, result.Difference) { Owner = this }.ShowDialog();
            if (openNewShift)
            {
                _tickets.Clear();
                _activeTicket = null;
                CartList.ItemsSource = _emptyCart;
                CurrentCustomerText.Text = "Cliente actual: público en general";
                UpdateSaleSummary();
                StatusText.Text = "Turno cerrado. Captura el fondo inicial del nuevo turno para continuar.";
                if (await OpenShiftFromDialogAsync()) await LoadSaleDraftsAsync();
            }
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            var closeState = await ConfirmCloseAfterConnectionFailureAsync(summary.ShiftId);
            if (closeState == true)
            {
                StatusText.Text = "El turno sí quedó cerrado. La respuesta tardó más de lo esperado, pero JetVenta confirmó el cierre.";
                return true;
            }

            StatusText.Text = closeState == false
                ? "La API responde y el turno continúa abierto. El cierre no se aplicó; puedes volver a intentarlo sin duplicar el corte."
                : ConnectionHelp.ApiUnavailableShiftProtected;
            return false;
        }
    }

    private static async Task<string> DescribeCloseConnectionFailureAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var health = await Client.GetAsync("/health", timeout.Token);
            return health.IsSuccessStatusCode
                ? "La API está activa, pero no pudo consultar la caja. Vuelve a intentar; si continúa, abre Configuración > Diagnóstico para revisar la base de datos."
                : ConnectionHelp.ApiUnavailableShiftProtected;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return ConnectionHelp.ApiUnavailableShiftProtected;
        }
    }

    private static async Task<bool?> ConfirmCloseAfterConnectionFailureAsync(Guid shiftId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var response = await Client.GetAsync("/api/shifts/current", timeout.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return true;
                if (response.IsSuccessStatusCode)
                {
                    var current = await response.Content.ReadFromJsonAsync<CurrentShiftResponse>(timeout.Token);
                    return current?.ShiftId != shiftId;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { }

            if (attempt < 2) await Task.Delay(350);
        }

        return null;
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
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailableRetry; }
        catch (Exception exception) { StatusText.Text = $"No se pudo procesar la busqueda: {exception.Message}"; }
    }

    private async void OnProductSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (ProductResultsList.Items.Count > 0)
            {
                var current = ProductResultsList.SelectedIndex;
                ProductResultsList.SelectedIndex = e.Key == Key.Down
                    ? Math.Min(current < 0 ? 0 : current + 1, ProductResultsList.Items.Count - 1)
                    : Math.Max(current <= 0 ? 0 : current - 1, 0);
                ProductResultsList.ScrollIntoView(ProductResultsList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ProductResultsList.SelectedItem is ProductSearchRow) await AddSelectedProductAsync();
            else await HandleProductEntryAsync(ProductSearchTextBox.Text.Trim());
            e.Handled = true;
        }
    }

    private async void OnSearchButtonClick(object sender, RoutedEventArgs e)
    {
        if (ProductResultsList.Items.Count == 1) { ProductResultsList.SelectedIndex = 0; await AddSelectedProductAsync(); }
        else if (ProductResultsList.Items.Count == 0 && !string.IsNullOrWhiteSpace(ProductSearchTextBox.Text)) await HandleProductEntryAsync(ProductSearchTextBox.Text.Trim());
        else StatusText.Text = "Selecciona un producto de la lista.";
        FocusProductInput();
    }

    private async void OnProductSelected(object sender, MouseButtonEventArgs e)
    {
        await AddSelectedProductAsync();
    }

    private async Task AddSelectedProductAsync()
    {
        if (ProductResultsList.SelectedItem is not ProductSearchRow row) return;
        await AddProductToCartAsync(row.Product);
    }

    private async Task HandleProductEntryAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            FocusProductInput();
            return;
        }

        try
        {
            var results = await Client.GetFromJsonAsync<List<ProductSearchResult>>($"/api/products/search?q={Uri.EscapeDataString(query)}") ?? [];
            var exact = results.FirstOrDefault(item => string.Equals(item.Code, query, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                await AddProductToCartAsync(exact);
                return;
            }

            if (results.Count == 1 && !LooksLikeBarcode(query))
            {
                await AddProductToCartAsync(results[0]);
                return;
            }

            if (results.Count > 0)
            {
                ProductResultsList.ItemsSource = results.Select(item => new ProductSearchRow(item)).ToList();
                ProductResultsList.Visibility = Visibility.Visible;
                StatusText.Text = "Selecciona el producto correcto de la lista.";
                FocusProductInput();
                return;
            }

            SystemSounds.Hand.Play();
            StatusText.Text = "Producto no encontrado. Debe registrarse o cobrarse como producto comun.";
            await HandleMissingProductAsync(query);
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
            FocusProductInput();
        }
    }

    private async Task HandleMissingProductAsync(string scannedCode)
    {
        var window = new MissingProductWindow(scannedCode) { Owner = this };
        if (window.ShowDialog() != true || window.Decision == MissingProductDecision.Cancel)
        {
            ProductSearchTextBox.Clear();
            ProductResultsList.Visibility = Visibility.Collapsed;
            FocusProductInput();
            return;
        }

        var isCommonProduct = window.Decision == MissingProductDecision.CommonProduct;
        if (isCommonProduct)
        {
            await OpenCommonProductAsync(window.ProductCode);
            return;
        }
        var requiredPermission = isCommonProduct ? "UseCommonProduct" : "ManageProducts";
        await using var authorization = await PermissionAuthorization.RequestAsync(this, requiredPermission, isCommonProduct ? "Agregar un producto común requiere autorización." : "Registrar un producto desde la venta requiere autorización.");
        if (authorization is null) { FocusProductInput(); return; }

        try
        {
            var command = new { code = window.ProductCode, description = window.ProductDescription, price = window.Price, unitOfMeasure = window.UnitOfMeasure, isCommonProduct };
            using var response = await Client.PostAsJsonAsync("/api/products/quick-sale", command);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await response.Content.ReadAsStringAsync();
                FocusProductInput();
                return;
            }

            var product = await response.Content.ReadFromJsonAsync<ProductSearchResult>();
            if (product is null)
            {
                StatusText.Text = "No se pudo agregar el producto rapido.";
                FocusProductInput();
                return;
            }

            await AddProductToCartAsync(product);
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableNotConfirmed;
            FocusProductInput();
        }
    }

    private async Task OpenCommonProductAsync(string? suggestedCode = null)
    {
        var window = new CommonProductWindow(suggestedCode) { Owner = this };
        if (window.ShowDialog() != true) { FocusProductInput(); return; }

        await using var authorization = await PermissionAuthorization.RequestAsync(this, "UseCommonProduct", "Agregar un producto común requiere autorización.");
        if (authorization is null) { FocusProductInput(); return; }

        try
        {
            var command = new { code = window.ProductCode, description = window.ProductDescription, price = window.Price, unitOfMeasure = window.UnitOfMeasure, isCommonProduct = true };
            using var response = await Client.PostAsJsonAsync("/api/products/quick-sale", command);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ReadApiMessageAsync(response);
                FocusProductInput();
                return;
            }

            var product = await response.Content.ReadFromJsonAsync<ProductSearchResult>();
            if (product is null) throw new InvalidOperationException("El servidor no devolvió el artículo temporal.");
            await AddProductToCartAsync(product, window.Quantity);
            StatusText.Text = "Producto común agregado sólo a este ticket. No modifica el inventario.";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableNotConfirmed;
            FocusProductInput();
        }
    }

    private async Task<string> ShowOpenShiftConflictAsync(HttpResponseMessage response)
    {
        try
        {
            var conflict = await response.Content.ReadFromJsonAsync<OpenShiftConflictResponse>();
            if (conflict is not null &&
                string.Equals(conflict.Code, "register_shift_open", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(conflict.OpenedBy))
            {
                var openedBy = conflict.OpenedBy;
                var window = new RegisterShiftOpenWindow(openedBy, conflict.OpenedAtUtc) { Owner = this };
                window.ShowDialog();
                if (window.ChangeUserRequested)
                {
                    await EndSessionAsync();
                    CompleteSignOut();
                    return "Cambiando de usuario.";
                }
                return $"La caja sigue abierta por {openedBy}. Cierra esta sesión e inicia con ese usuario para realizar el corte.";
            }
        }
        catch (Exception) { }

        return await ReadApiMessageAsync(response);
    }

    private async Task AddProductToCartAsync(ProductSearchResult product, decimal? requestedQuantity = null)
    {
        if (_activeTicket is null)
        {
            StatusText.Text = "Crea o recupera un ticket antes de agregar productos.";
            return;
        }

        var quantity = requestedQuantity ?? 1m;
        if (requestedQuantity is null && IsBulkUnit(product.UnitOfMeasure))
        {
            var window = new SaleQuantityWindow(product.Description, product.UnitOfMeasure, 1m) { Owner = this };
            if (window.ShowDialog() != true || window.Quantity is null) { FocusProductInput(); return; }
            quantity = window.Quantity.Value;
        }
        if (quantity <= 0m) return;

        var cart = _activeTicket.Lines;
        var existing = cart.FirstOrDefault(item => item.ProductId == product.Id);
        if (existing is null) cart.Add(new CartLineView(product.Id, product.Code, product.Description, product.Price, product.Stock, quantity, product.UnitOfMeasure));
        else { existing.Quantity += quantity; }
        var line = existing ?? cart[^1];
        await ApplyPromotionQuoteAsync(line);
        CartList.Items.Refresh();
        ProductSearchTextBox.Clear();
        ProductResultsList.Visibility = Visibility.Collapsed;
        UpdateSaleSummary();
        QueueActiveTicketSave();
        SystemSounds.Asterisk.Play();
        StatusText.Text = line.DiscountTotal > 0m
            ? $"Producto agregado. Promoción aplicada: {line.DiscountTotal:C2} de descuento."
            : "Producto agregado a la venta.";
        FocusProductInput();
    }

    private async Task ApplyPromotionQuoteAsync(CartLineView line)
    {
        try
        {
            var url = $"/api/promotions/quote?productId={line.ProductId}&price={line.BaseUnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)}&quantity={line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            using var response = await Client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                line.UnitPrice = line.BaseUnitPrice;
                line.DiscountTotal = 0m;
                line.PromotionalTotal = null;
                return;
            }
            var quote = await response.Content.ReadFromJsonAsync<PromotionPriceQuote>();
            if (quote is not null)
            {
                line.UnitPrice = quote.UnitPrice;
                line.DiscountTotal = quote.DiscountTotal;
                line.PromotionalTotal = quote.Total;
            }
        }
        catch (HttpRequestException)
        {
            // La venta sigue mostrando el precio normal; el servidor vuelve a validar la promoción al cobrar.
            line.UnitPrice = line.BaseUnitPrice;
            line.DiscountTotal = 0m;
            line.PromotionalTotal = null;
        }
    }

    private static bool LooksLikeBarcode(string value) => value.Length >= 6 && value.All(char.IsLetterOrDigit);

    private void FocusProductInput()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ProductSearchTextBox.Focus();
            Keyboard.Focus(ProductSearchTextBox);
        });
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!string.Equals(WorkspaceTitleText.Text, "Venta en curso", StringComparison.OrdinalIgnoreCase)) return;
        if (Keyboard.FocusedElement is TextBox) return;
        ProductSearchTextBox.Focus();
        ProductSearchTextBox.Text += e.Text;
        ProductSearchTextBox.CaretIndex = ProductSearchTextBox.Text.Length;
        e.Handled = true;
    }

    private void OnSerialBarcodeScanned(object? sender, string code)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (!string.Equals(WorkspaceTitleText.Text, "Venta en curso", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "Se recibió un código del lector, pero Ventas no está abierto.";
                return;
            }

            ProductSearchTextBox.Text = code;
            await HandleProductEntryAsync(code);
        });
    }

    private void OnCartKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        DeleteSelectedCartLine();
        e.Handled = true;
    }

    private async void OnCartDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CartList.SelectedItem is not CartLineView line || _activeTicket is null) return;
        var window = new SaleQuantityWindow(line.Description, line.UnitOfMeasure, line.Quantity) { Owner = this };
        if (window.ShowDialog() != true || window.Quantity is null) return;
        line.Quantity = window.Quantity.Value;
        await ApplyPromotionQuoteAsync(line);
        CartList.Items.Refresh();
        UpdateSaleSummary();
        QueueActiveTicketSave();
        StatusText.Text = "Cantidad actualizada en el ticket.";
        FocusProductInput();
    }

    private void DeleteSelectedCartLine()
    {
        if (CartList.SelectedItem is not CartLineView line)
        {
            StatusText.Text = "Selecciona una partida para borrarla.";
            FocusProductInput();
            return;
        }

        if (_activeTicket is null) return;
        _activeTicket.Lines.Remove(line);
        CartList.Items.Refresh();
        UpdateSaleSummary();
        QueueActiveTicketSave();
        StatusText.Text = "Partida eliminada de la venta.";
        FocusProductInput();
    }

    private void UpdateSaleSummary()
    {
        var cart = _activeTicket?.Lines ?? _emptyCart;
        var subtotal = decimal.Round(cart.Sum(item => item.Total), 2, MidpointRounding.AwayFromZero);
        var total = CalculateSaleTotal(subtotal);
        SaleTotalText.Text = $"${total:0.00}";
        var rounding = total - subtotal;
        SaleItemsText.Text = rounding == 0m
            ? $"Artículos: {cart.Sum(item => item.Quantity):0.###}"
            : $"Artículos: {cart.Sum(item => item.Quantity):0.###} · Subtotal ${subtotal:0.00} · Redondeo +${rounding:0.00}";
        CurrentSectionText.Text = _activeTicket?.Title ?? "Nueva venta";
    }

    private async Task NotifyCashLimitAsync()
    {
        try
        {
            var settings = await Client.GetFromJsonAsync<CutSettingsResponse>("/api/cut-settings");
            if (settings is not { CashLimitEnabled: true } || settings.CashLimit <= 0m) return;
            var summary = await Client.GetFromJsonAsync<ShiftSummaryResponse>("/api/shifts/summary");
            if (summary is null || summary.ExpectedCash < settings.CashLimit) return;
            MessageBox.Show(settings.CashLimitMessage, "Límite de efectivo en caja", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailableRetry; }
    }

    private static bool IsBulkUnit(string? unit) => unit is not null && unit.Contains("granel", StringComparison.OrdinalIgnoreCase) || unit is "Kilogramo" or "Gramo" or "Litro" or "Mililitro" or "Metro";

    private decimal CalculateSaleTotal(decimal subtotal)
    {
        if (!_roundSaleAmounts) return subtotal;
        return string.Equals(_roundingMode, "Whole", StringComparison.OrdinalIgnoreCase)
            ? decimal.Ceiling(subtotal)
            : decimal.Ceiling(subtotal * 10m) / 10m;
    }

    private async Task LoadSalePricingOptionsAsync()
    {
        try
        {
            var settings = await Client.GetFromJsonAsync<StoreOptionsResponse>("/api/store-options");
            if (settings is null) return;
            _roundSaleAmounts = settings.RoundSaleAmounts;
            _roundingMode = settings.RoundingMode;
            UpdateSaleSummary();
        }
        catch (HttpRequestException)
        {
            _roundSaleAmounts = false;
        }
    }

    private sealed record ProductSearchResult(Guid Id, string Code, string Description, decimal Price, decimal Stock = 0m, string UnitOfMeasure = "Pieza");
    private sealed record ProductSearchRow(ProductSearchResult Product)
    {
        public string DisplayText => $"{Product.Code} | {Product.Description} | ${Product.Price:0.00}";
    }

    private sealed class CartLineView(Guid productId, string code, string description, decimal unitPrice, decimal stock, decimal quantity, string unitOfMeasure = "Pieza")
    {
        public Guid ProductId { get; } = productId; public string Code { get; } = code; public string Description { get; } = description; public string UnitOfMeasure { get; } = unitOfMeasure; public decimal BaseUnitPrice { get; } = unitPrice; public decimal UnitPrice { get; set; } = unitPrice; public decimal Stock { get; } = stock; public decimal Quantity { get; set; } = quantity; public decimal DiscountTotal { get; set; } public decimal? PromotionalTotal { get; set; } public decimal Total => PromotionalTotal ?? decimal.Round(UnitPrice * Quantity, 2); public string DisplayText => $"{Code} | {Description} x {Quantity:0.###} = ${Total:0.00}";
    }

    private sealed class TicketTabView(Guid id, Guid operationId, int ticketNumber, IEnumerable<CartLineView>? lines = null)
    {
        public Guid Id { get; } = id;
        public Guid OperationId { get; } = operationId;
        public int TicketNumber { get; } = ticketNumber;
        public Guid? CustomerId { get; private set; }
        public string? CustomerName { get; private set; }
        public string Title => string.IsNullOrWhiteSpace(CustomerName) ? $"Ticket {TicketNumber}" : CustomerName!;
        public ObservableCollection<CartLineView> Lines { get; } = new(lines ?? []);
        public void SetCustomer(Guid customerId, string customerName) { CustomerId = customerId; CustomerName = customerName.Trim(); }
    }

    private sealed record SaleDraftResponse(Guid Id, Guid OperationId, int TicketNumber, DateTimeOffset UpdatedAtUtc, IReadOnlyList<SaleDraftLineResponse> Lines);
    private sealed record SaleDraftLineResponse(Guid ProductId, string Code, string Description, decimal UnitPrice, decimal Stock, decimal Quantity);
    private sealed record PromotionPriceQuote(Guid ProductId, decimal BaseUnitPrice, decimal UnitPrice, decimal Quantity, decimal Total, decimal DiscountTotal, bool PromotionApplied);
    private sealed record SaleResponse(Guid SaleId, decimal Total, decimal Change, bool Existing);
    private sealed record LicenseStatusResponse(bool IsActive, string State, string Message);
    private sealed record StoreOptionsResponse(bool InventoryEnabled, string InventoryCostMethod, bool CreditSalesEnabled, bool CommonProductsEnabled, bool AutoPriceWithProfit, decimal DefaultProfitPercent, bool RoundSaleAmounts, string RoundingMode, string OccasionalNotice, int OccasionalNoticeEverySales);
    private sealed record RegisterResponse(Guid Id, string Name);
    private sealed record ShiftSummaryResponse(Guid ShiftId, decimal ExpectedCash, decimal CountedCash, decimal Difference, DateTimeOffset? ClosedAtUtc);
    private sealed record CutSettingsResponse(bool RequireCashCountOnClose, bool AutoAdjustCashDifference, bool CashLimitEnabled, decimal CashLimit, string CashLimitMessage);
    private sealed record MercadoPagoStatus(bool Enabled);
    private sealed record CurrentShiftResponse(Guid ShiftId, Guid RegisterId, Guid UserId, decimal InitialCash, DateTimeOffset OpenedAtUtc);
    private sealed record OpenShiftConflictResponse(string? Code, string? Message, string? OpenedBy, DateTimeOffset OpenedAtUtc);
    private sealed record LatestSaleRow(Guid SaleId, DateTimeOffset CreatedAtUtc, decimal Total, string Status);

    private async void OnNewTicketClick(object sender, RoutedEventArgs e) => await CreateNewTicketAsync();

    private async Task LoadSaleDraftsAsync()
    {
        try
        {
            var drafts = await Client.GetFromJsonAsync<List<SaleDraftResponse>>("/api/sale-drafts") ?? [];
            _tickets.Clear();
            foreach (var draft in drafts)
            {
                _tickets.Add(ToTicket(draft));
            }

            if (_tickets.Count == 0)
            {
                await CreateNewTicketAsync();
                return;
            }

            TicketTabs.SelectedIndex = 0;
            ActivateTicket(_tickets[0]);
            StatusText.Text = _tickets.Count == 1
                ? "Ticket en atención recuperado."
                : $"{_tickets.Count} tickets en atención recuperados.";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
    }

    private async Task CreateNewTicketAsync(bool saveCurrentTicket = true)
    {
        if (saveCurrentTicket
            && _activeTicket is { Lines.Count: > 0 } activeTicket
            && _tickets.Contains(activeTicket)
            && !await PersistActiveTicketAsync())
        {
            StatusText.Text = "No se pudo guardar el ticket actual. Revisa la conexión antes de abrir otro.";
            return;
        }

        try
        {
            using var response = await Client.PostAsync("/api/sale-drafts", null);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ReadApiMessageAsync(response);
                return;
            }

            var draft = await response.Content.ReadFromJsonAsync<SaleDraftResponse>();
            if (draft is null)
            {
                StatusText.Text = "El servidor no devolvió el nuevo ticket.";
                return;
            }

            var ticket = ToTicket(draft);
            _tickets.Add(ticket);
            TicketTabs.SelectedItem = ticket;
            ActivateTicket(ticket);
            StatusText.Text = $"{ticket.Title} listo para atender.";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
    }

    private static TicketTabView ToTicket(SaleDraftResponse draft) => new(
        draft.Id,
        draft.OperationId,
        draft.TicketNumber,
        draft.Lines.Select(line => new CartLineView(line.ProductId, line.Code, line.Description, line.UnitPrice, line.Stock, line.Quantity)));

    private void OnTicketSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != TicketTabs || TicketTabs.SelectedItem is not TicketTabView ticket) return;
        ActivateTicket(ticket);
    }

    private void ActivateTicket(TicketTabView ticket)
    {
        _activeTicket = ticket;
        CartList.ItemsSource = ticket.Lines;
        CartList.Items.Refresh();
        UpdateSaleSummary();
        CurrentCustomerText.Text = ticket.CustomerName ?? "Seleccionar cliente";
        FocusProductInput();
    }

    private void SelectNextTicket()
    {
        if (_tickets.Count == 0) return;
        var current = Math.Max(0, TicketTabs.SelectedIndex);
        TicketTabs.SelectedIndex = (current + 1) % _tickets.Count;
    }

    private void QueueActiveTicketSave() => _ = PersistActiveTicketAsync(showError: false);

    private async Task<bool> PersistActiveTicketAsync(bool showError = true)
    {
        var ticket = _activeTicket;
        if (ticket is null) return false;
        var lines = ticket.Lines.Select(line => new { productId = line.ProductId, quantity = line.Quantity }).ToArray();

        await _draftSaveLock.WaitAsync();
        try
        {
            using var response = await Client.PutAsJsonAsync($"/api/sale-drafts/{ticket.Id}", new { lines });
            if (response.IsSuccessStatusCode) return true;
            if (showError) StatusText.Text = await ReadApiMessageAsync(response);
            return false;
        }
        catch (HttpRequestException)
        {
            if (showError) StatusText.Text = ConnectionHelp.ApiUnavailableNotConfirmed;
            return false;
        }
        finally
        {
            _draftSaveLock.Release();
        }
    }

    private async void OnDiscardTicketClick(object sender, RoutedEventArgs e)
    {
        if (_discardInProgress) return;

        // La pestaña seleccionada es la fuente de verdad. La referencia activa
        // puede quedar desfasada mientras WPF procesa el cambio de pestaña.
        var ticket = TicketTabs.SelectedItem as TicketTabView ?? _activeTicket;
        if (ticket is null || !_tickets.Contains(ticket)) return;
        if (ticket.Lines.Count > 0 && MessageBox.Show($"¿Descartar {ticket.Title}? Sus productos no se venderán ni afectarán inventario.", "Descartar ticket", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _discardInProgress = true;
        var lockHeld = false;
        try
        {
            await _draftSaveLock.WaitAsync();
            lockHeld = true;
            using var response = await Client.DeleteAsync($"/api/sale-drafts/{ticket.Id}");
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ReadApiMessageAsync(response);
                return;
            }

            var removedIndex = _tickets.IndexOf(ticket);
            if (removedIndex < 0) return;
            _tickets.RemoveAt(removedIndex);
            if (_tickets.Count == 0) await CreateNewTicketAsync();
            else
            {
                var nextIndex = Math.Min(removedIndex, _tickets.Count - 1);
                TicketTabs.SelectedIndex = nextIndex;
                ActivateTicket(_tickets[nextIndex]);
            }
            StatusText.Text = "Ticket descartado. No se registró ninguna venta.";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableNotConfirmed;
        }
        finally
        {
            if (lockHeld) _draftSaveLock.Release();
            _discardInProgress = false;
        }
    }

    private async void OnChargeClick(object sender, RoutedEventArgs e)
    {
        var ticket = _activeTicket;
        if (ticket is null || ticket.Lines.Count == 0) { StatusText.Text = "Agrega al menos un producto antes de cobrar."; return; }
        await using var saleAuthorization = await PermissionAuthorization.RequestAsync(this, "Sell", "Cobrar una venta requiere autorización.");
        if (saleAuthorization is null) return;
        if (!await PersistActiveTicketAsync()) return;
        var ticketSubtotal = decimal.Round(ticket.Lines.Sum(item => item.Total), 2, MidpointRounding.AwayFromZero);
        var ticketTotal = CalculateSaleTotal(ticketSubtotal);
        var cashWindow = new CashWindow(ticketTotal, ticket.Lines.Sum(item => item.Quantity), ticket.CustomerId, ticket.CustomerName) { Owner = this };
        if (cashWindow.ShowDialog() != true || cashWindow.Received is null) return;
        await using var creditAuthorization = cashWindow.CreditRequested
            ? await PermissionAuthorization.RequestAsync(this, "SellOnCredit", "Cobrar una venta a crédito requiere autorización.")
            : TemporaryPermissionLease.NotRequired;
        if (cashWindow.CreditRequested && creditAuthorization is null) return;
        try
        {
            var pointAmount = cashWindow.PaymentMethod == "Card" ? ticketTotal : cashWindow.PaymentMethod == "Mixed" ? cashWindow.CardAmount : 0m;
            if (pointAmount > 0m && await IsMercadoPagoEnabledAsync())
            {
                var point = new MercadoPagoPaymentWindow(ticket.OperationId, pointAmount) { Owner = this };
                if (point.ShowDialog() != true || !point.Approved) { StatusText.Text = "La venta sigue abierta porque el cobro con Mercado Pago no fue aprobado."; return; }
            }
            var cashReceived = cashWindow.CreditRequested || cashWindow.PaymentMethod is not ("Cash" or "Mixed") ? 0m : cashWindow.Received.Value;
            var command = new { operationId = ticket.OperationId, draftId = ticket.Id, lines = ticket.Lines.Select(item => new { productId = item.ProductId, quantity = item.Quantity }).ToArray(), cashReceived, cardAmount = cashWindow.CreditRequested ? 0m : cashWindow.CardAmount, transferAmount = cashWindow.CreditRequested ? 0m : cashWindow.TransferAmount, customerId = cashWindow.CustomerId ?? ticket.CustomerId, paymentMethod = cashWindow.PaymentMethod, printRequested = cashWindow.PrintRequested };
            using var response = await Client.PostAsJsonAsync("/api/sales/complete", command);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await ReadApiMessageAsync(response); MessageBox.Show(StatusText.Text, "No se pudo confirmar la venta", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var result = await response.Content.ReadFromJsonAsync<SaleResponse>();
            _lastSaleId = result?.SaleId;
            _tickets.Remove(ticket);
            if (_tickets.Count == 0) await CreateNewTicketAsync();
            else { TicketTabs.SelectedIndex = 0; ActivateTicket(_tickets[0]); }
            StatusText.Text = result is null ? "Venta confirmada." : result.Existing ? "La venta ya estaba confirmada; no se registró un cobro duplicado." : cashWindow.CreditRequested ? "Venta a crédito confirmada." : $"Venta confirmada. Cambio: ${result.Change:0.00}";
            if (result is not null && !result.Existing) await NotifyCashLimitAsync();
            if (result is not null && !result.Existing && !cashWindow.CreditRequested && cashWindow.PaymentMethod is "Cash" or "Mixed")
                await TryOpenCashDrawerAsync();
            if (result is not null && !result.Existing && cashWindow.PrintRequested)
            {
                try { StatusText.Text += " " + await OutputTicketAsync(result.SaleId); }
                catch (Exception exception) { StatusText.Text += $" La venta quedó guardada, pero no se imprimió el ticket: {exception.Message}"; }
            }
            else if (result is not null && !result.Existing && !cashWindow.PrintRequested)
            {
                StatusText.Text += " Venta confirmada sin imprimir ticket.";
            }
            FocusProductInput();
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailableNotConfirmed; }
    }

    private static async Task<bool> IsMercadoPagoEnabledAsync()
    {
        try { return (await Client.GetFromJsonAsync<MercadoPagoStatus>("/api/integrations/mercado-pago/settings"))?.Enabled == true; }
        catch { return false; }
    }

    private async void OnCancelLastSaleClick(object sender, RoutedEventArgs e)
    {
        await using var authorization = await PermissionAuthorization.RequestAsync(this, "CancelSales", "Cancelar una venta confirmada requiere autorización.");
        if (authorization is null) return;
        if (_lastSaleId is null) { StatusText.Text = "No hay una venta reciente para cancelar."; return; }
        var window = new CancelSaleWindow { Owner = this };
        if (window.ShowDialog() != true) return;
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/sales/cancel", new { operationId = Guid.NewGuid(), saleId = _lastSaleId.Value, reason = window.Reason });
            StatusText.Text = response.IsSuccessStatusCode ? "Venta cancelada e inventario revertido." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) _lastSaleId = null;
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private async void OnReturnLastSaleClick(object sender, RoutedEventArgs e)
    {
        var authorization = await PermissionAuthorization.RequestAsync(this, "ProcessReturns", "Procesar una devolución requiere autorización.");
        if (authorization is null) return;
        if (_lastSaleId is null)
        {
            StatusText.Text = "Selecciona la venta que deseas devolver desde el historial.";
            ShowEmbeddedModule(new SalesHistoryWindow(), "Historial", "Selecciona una venta para procesar una devolución.", authorization);
            return;
        }
        await using (authorization)
        {
            var window = new ReturnSaleWindow(_lastSaleId.Value) { Owner = this };
            window.ShowDialog();
        }
    }

    private static async Task SaveTicketPdfAsync(Guid saleId)
    {
        using var response = await Client.GetAsync($"/api/sales/{saleId}/ticket.pdf");
        if (!response.IsSuccessStatusCode) return;
        var dialog = new SaveFileDialog { Title = "Guardar ticket PDF", Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"Ticket-{saleId:N}.pdf", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        await File.WriteAllBytesAsync(dialog.FileName, await response.Content.ReadAsByteArrayAsync());
    }

    private static async Task<string> OutputTicketAsync(Guid saleId)
    {
        if (string.IsNullOrWhiteSpace(ApiClient.PrinterName))
        {
            await SaveTicketPdfAsync(saleId);
            return "No hay impresora configurada; se ofreció guardar el ticket en PDF.";
        }

        var ticket = await Client.GetFromJsonAsync<TicketPdfData>($"/api/sales/{saleId}/ticket-data")
            ?? throw new InvalidOperationException("El servidor no devolvió los datos del ticket.");
        var profile = TicketWindowsPrinter.CurrentProfile;
        TicketWindowsPrinter.Print(ApiClient.PrinterName, ticket, profile, $"Ticket {saleId:N}");
        using var markResponse = await Client.PostAsync($"/api/sales/{saleId}/ticket/printed", null);
        return markResponse.IsSuccessStatusCode
            ? $"Ticket enviado a {ApiClient.PrinterName}."
            : $"Ticket enviado a {ApiClient.PrinterName}; no se pudo actualizar el estado de impresión.";
    }

    private async Task<bool> TryOpenCashDrawerAsync(bool explainIfDisabled = false)
    {
        try
        {
            var settings = await Client.GetFromJsonAsync<CashDrawerSettingsResult>("/api/cash-drawer-settings");
            if (settings is not { Enabled: true })
            {
                if (explainIfDisabled) StatusText.Text = "El cajón no está configurado o está desactivado.";
                return false;
            }
            TicketWindowsPrinter.OpenCashDrawer(settings.PrinterName, settings.Model);
            StatusText.Text += " Cajón abierto.";
            return true;
        }
        catch (Exception exception)
        {
            StatusText.Text += exception is HttpRequestException
                ? $" {ConnectionHelp.ApiUnavailableRetry}"
                : explainIfDisabled
                    ? $" No se pudo abrir el cajón: {exception.Message}"
                    : $" La operación quedó registrada, pero no se pudo abrir el cajón: {exception.Message}";
            return false;
        }
    }

    private sealed record CashDrawerSettingsResult(bool Enabled, string PrinterName, string Model, string Port);

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private async void OnExitClick(object sender, RoutedEventArgs e) => await RequestExitAsync();

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitConfirmed) return;
        e.Cancel = true;
        await RequestExitAsync();
    }

    private async Task EnsureShiftOpenAfterLoginAsync()
    {
        var currentShift = await GetCurrentShiftAsync();
        if (currentShift is not null)
        {
            var window = new ShiftWindow { Owner = this };
            window.ShowAlreadyOpen(currentShift.InitialCash, currentShift.OpenedAtUtc);
            window.ShowDialog();
            StatusText.Text = "Caja abierta. Puedes continuar vendiendo.";
            await LoadSaleDraftsAsync();
            return;
        }

        StatusText.Text = "No hay turno abierto. Captura el fondo inicial para empezar.";
        if (await OpenShiftFromDialogAsync()) await LoadSaleDraftsAsync();
    }

    private async Task<bool?> HasOpenShiftAsync()
    {
        try
        {
            using var response = await Client.GetAsync("/api/shifts/current");
            if (response.IsSuccessStatusCode) return true;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            StatusText.Text = await ReadApiMessageAsync(response);
            return null;
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
            return null;
        }
    }

    private async Task<CurrentShiftResponse?> GetCurrentShiftAsync()
    {
        try
        {
            using var response = await Client.GetAsync("/api/shifts/current");
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CurrentShiftResponse>() : null;
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
            return null;
        }
    }

    private async Task RequestExitAsync()
    {
        if (_exitDialogOpen) return;
        _exitDialogOpen = true;
        try
        {
            if (_activeTicket?.Lines.Count > 0 && !await PersistActiveTicketAsync())
            {
                MessageBox.Show("El ticket actual no pudo guardarse. JetVenta permanece abierto para no perder la venta en atención.", "No se puede salir", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var hasOpenShift = await HasOpenShiftAsync();
            if (hasOpenShift is null)
            {
            MessageBox.Show($"No se pudo comprobar el estado de la caja.{Environment.NewLine}{Environment.NewLine}{ConnectionHelp.ApiUnavailableShiftProtected}", "No se puede salir", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (hasOpenShift.Value)
            {
                var decisionWindow = new ExitShiftWindow { Owner = this };
                if (decisionWindow.ShowDialog() != true || decisionWindow.Decision == ExitShiftDecision.Cancel) return;
                if (decisionWindow.Decision is ExitShiftDecision.CloseShiftAndExit or ExitShiftDecision.CloseShiftAndSignOut)
                {
                    var closed = await CloseShiftFromDialogAsync(existingAuthorization: _modulePermissionLease);
                    if (!closed)
                    {
                        MessageBox.Show(StatusText.Text, "No se pudo cerrar el turno", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var stillOpen = await HasOpenShiftAsync();
                    if (stillOpen is not false)
                    {
                        MessageBox.Show("El servidor no confirmó el cierre del turno. JetVenta permanece abierto para proteger el corte.", "Cierre sin confirmar", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (decisionWindow.Decision == ExitShiftDecision.CloseShiftAndSignOut)
                    {
                        await EndSessionAsync();
                        CompleteSignOut();
                        return;
                    }
                }
            }
            await EndSessionAsync();
            CompleteExit();
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cerrar JetVenta");
            MessageBox.Show(StatusText.Text, "No se puede salir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _exitDialogOpen = false; }
    }

    private async Task EndSessionAsync()
    {
        var moduleAuthorization = _modulePermissionLease;
        _modulePermissionLease = null;
        if (moduleAuthorization is not null) await moduleAuthorization.DisposeAsync();

        if (string.IsNullOrWhiteSpace(SessionContext.AccessToken)) return;
        try { await Client.DeleteAsync("/api/auth/session"); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or ObjectDisposedException) { }
    }

    private void CompleteExit()
    {
        _exitConfirmed = true;
        Closing -= OnClosing;
        Close();
        System.Windows.Application.Current.Shutdown(0);
    }

    private void CompleteSignOut()
    {
        _exitConfirmed = true;
        Closing -= OnClosing;
        SessionContext.Clear();
        ApiClient.ApplySession(null);

        var login = new LoginWindow();
        System.Windows.Application.Current.MainWindow = login;
        login.Show();
        Close();
    }

    private void NavigateTo(string section)
    {
        CurrentSectionText.Text = section;
        WorkspaceTitleText.Text = section;
        WorkspaceDetailText.Text = $"El modulo {section.ToLowerInvariant()} se implementara en un incremento posterior de la Fase 1.";
        StatusText.Text = $"Navegacion activa: {section}.";
    }

    private void ShowSalesWorkspace()
    {
        ReleaseModulePermission();
        ModuleWorkspace.Content = null;
        ModuleWorkspace.Visibility = Visibility.Collapsed;
        SalesWorkspace.Visibility = Visibility.Visible;
        CurrentSectionText.Text = "Nueva venta";
        WorkspaceTitleText.Text = "Venta en curso";
        WorkspaceDetailText.Text = "Escanea un código o busca un producto.";
        StatusText.Text = "Venta en curso. Los tickets en atención se conservaron.";
        FocusProductInput();
    }

    private void ShowEmbeddedModule(UserControl module, string section, string detail, TemporaryPermissionLease? authorization = null)
    {
        ReleaseModulePermission();
        _modulePermissionLease = authorization;
        SalesWorkspace.Visibility = Visibility.Collapsed;
        ModuleWorkspace.Content = module;
        ModuleWorkspace.Visibility = Visibility.Visible;
        CurrentSectionText.Text = section;
        StatusText.Text = $"{section} abierto. Los tickets en atención siguen guardados.";
    }

    private void OpenCustomers(bool creditMode = false, TemporaryPermissionLease? authorization = null)
    {
        var section = creditMode ? "Créditos" : "Clientes";
        var detail = creditMode
            ? "Consulta créditos y registra abonos sin cerrar los tickets en atención."
            : "Administra clientes sin cerrar los tickets en atención.";
        ShowEmbeddedModule(new CustomerModule(creditMode), section, detail, authorization);
    }

    private void OpenInventory(TemporaryPermissionLease? authorization = null)
    {
        var module = new InventoryWindow();
        module.CloseRequested += (_, _) => ShowSalesWorkspace();
        ShowEmbeddedModule(module, "Inventario", "Consulta y ajusta inventario sin cerrar los tickets en atención.", authorization);
    }

    private void ShowPendingFeature(string feature)
    {
        StatusText.Text = $"{feature} pendiente.";
        MessageBox.Show($"{feature} todavía no está terminado. El botón queda visible para conservar el flujo y el atajo, y se conectará en un incremento posterior.", "Función pendiente", MessageBoxButton.OK, MessageBoxImage.Information);
        FocusProductInput();
    }

    private void ReleaseModulePermission()
    {
        var authorization = _modulePermissionLease;
        _modulePermissionLease = null;
        if (authorization is not null) _ = authorization.DisposeAsync();
    }

    private static string? PermissionFor(string section) => section switch
    {
        "Ventas" => "Sell",
        "Creditos" or "Clientes" => "ManageCustomersAndCredit",
        "Productos" => "ViewProducts",
        "Inventario" => "ViewInventory",
        "Corte" => "CloseShift",
        "Configuracion" => "ConfigureStore",
        "Compras" => "ManageSuppliersAndPurchases",
        "Reportes" => "ViewReports",
        "Historial" => "ViewSalesHistory",
        "Promociones" or "Kits" => "ManageProducts",
        _ => null
    };

    private static string SectionNameFor(string section) => section switch
    {
        "Creditos" => "Créditos",
        "Configuracion" => "Configuración",
        _ => section
    };

    private void ApplyNavigationPermissions()
    {
        OpenCashDrawerButton.IsEnabled = true;
        foreach (var button in FindVisualChildren<Button>(this))
        {
            if (button.Tag is string) button.IsEnabled = true;
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
