using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace JetVenta.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApiClient _api;
    private readonly MobileSettings _settings;
    private readonly MobileShoppingListStore _shoppingListStore;
    private readonly CameraBarcodeReaderView _cameraView;
    private readonly ObservableCollection<MobileCountResult> _sessionCounts = [];
    private readonly ObservableCollection<MobileShoppingListItem> _shoppingItems = [];
    private readonly ObservableCollection<MobilePurchaseReceiptLine> _purchaseLines = [];
    private bool _readingBarcode;
    private bool _cameraAvailable;
    private bool _cameraPermissionGranted;
    private bool _cameraInitializing;
    private bool _cameraPermissionRequestInProgress;
    private bool _hasFrontCamera;
    private bool _hasRearCamera;
    private CameraLocation _cameraLocation = CameraLocation.Rear;
    private CancellationTokenSource? _cameraStartCancellation;
    private bool _canViewInventory;
    private bool _canAdjustInventory;
    private bool _loggedIn;
    private bool _historyDifferencesOnly;
    private string _inventoryStatus = "all";
    private Guid? _countSessionId;
    private MobileProduct? _selectedCountProduct;
    private MobileProduct? _selectedShoppingProduct;
    private MobilePurchaseProduct? _selectedPurchaseProduct;
    private Guid _purchaseOperationId = Guid.NewGuid();
    private Guid? _lastPurchaseRequestId;
    private bool _submittingPurchase;
    private bool _shoppingListLoaded;
    private Panel _activePanel = Panel.Home;

    public MainPage(MobileApiClient api, MobileSettings settings, MobileShoppingListStore shoppingListStore)
    {
        _api = api;
        _settings = settings;
        _shoppingListStore = shoppingListStore;
        InitializeComponent();
        NavigationPage.SetHasNavigationBar(this, false);
        _cameraView = new CameraBarcodeReaderView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _cameraView.BarcodesDetected += OnBarcodesDetected;
        ResultsView.ItemsSource = null;
        CountResultsLog.ItemsSource = _sessionCounts;
        ShoppingListView.ItemsSource = _shoppingItems;
        PurchaseLinesView.ItemsSource = _purchaseLines;
        _purchaseLines.CollectionChanged += (_, _) => UpdatePurchaseTotals();
        SetBarcodeOptions(_cameraView);
        Loaded += async (_, _) => await InitializeSafelyAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loggedIn) await EnableCameraAsync();
    }

    protected override void OnDisappearing()
    {
        StopCameraViews();
        base.OnDisappearing();
    }

    internal void OnHostPaused()
    {
        StopCameraViews();
        _cameraAvailable = false;
        UpdateCameraControls();
    }

    internal async void OnHostResumed()
    {
        if (!_loggedIn || _cameraPermissionRequestInProgress) return;
        await Task.Delay(400);
        if (!_loggedIn) return;
        await EnableCameraAsync(requestPermission: false);
    }

    private static void SetBarcodeOptions(CameraBarcodeReaderView view) => view.Options = new BarcodeReaderOptions
    {
        Formats = BarcodeFormats.OneDimensional,
        AutoRotate = true,
        TryHarder = true,
        TryInverted = true,
        Multiple = false
    };

    private async Task InitializeSafelyAsync()
    {
        try
        {
            ServerEntry.Text = await _settings.GetServerAsync() ?? string.Empty;
            UserEntry.Text = await _settings.GetUserNameAsync() ?? string.Empty;
            var paired = !string.IsNullOrWhiteSpace(await _settings.GetDeviceTokenAsync());
            var loggedIn = paired && !string.IsNullOrWhiteSpace(await _settings.GetAccessTokenAsync());
            if (loggedIn)
            {
                var capabilities = await _api.GetCapabilitiesAsync();
                ApplyCapabilities(capabilities);
            }
            ShowState(paired, loggedIn);
            if (loggedIn) await EnableCameraAsync();
        }
        catch (MobileSessionExpiredException)
        {
            ShowState(true, false);
        }
        catch (Exception exception)
        {
            ShowState(false, false);
            await DisplayAlertAsync("No se pudo cargar la configuración", $"JetVenta Móvil sigue abierta. Revisa la configuración del teléfono e inténtalo de nuevo.\n\nDetalle: {exception.Message}", "Aceptar");
        }
    }

    private void ApplyCapabilities(MobileCapabilities capabilities)
    {
        _canViewInventory = capabilities.CanViewInventory;
        _canAdjustInventory = capabilities.CanAdjustInventory;
        VerifierMenu.IsVisible = _canViewInventory;
        CountMenu.IsVisible = _canAdjustInventory;
        InventoryMenu.IsVisible = _canViewInventory || _canAdjustInventory;
        ShoppingListMenu.IsVisible = _canViewInventory || _canAdjustInventory;
        PermissionHint.IsVisible = !_canViewInventory && !_canAdjustInventory;
    }

    private void ShowState(bool paired, bool loggedIn)
    {
        _loggedIn = loggedIn;
        PairingPanel.IsVisible = !paired;
        LoginPanel.IsVisible = paired && !loggedIn;
        LogoutButton.IsVisible = loggedIn;
        PurchaseMenu.IsVisible = loggedIn;
        if (!paired || !loggedIn) SetPanel(Panel.Home);
        else SetPanel(_activePanel);
        ConnectionLabel.Text = loggedIn ? "Conectado a JetVenta" : paired ? "Teléfono conectado" : "Configura este teléfono";
    }

    private void SetPanel(Panel panel)
    {
        _activePanel = panel;
        HomePanel.IsVisible = _loggedIn && panel == Panel.Home;
        CheckerPanel.IsVisible = _loggedIn && panel == Panel.Verifier && _canViewInventory;
        CountPanel.IsVisible = _loggedIn && panel == Panel.Count && _canAdjustInventory;
        HistoryPanel.IsVisible = _loggedIn && panel == Panel.History && _canAdjustInventory;
        InventoryPanel.IsVisible = _loggedIn && panel == Panel.Inventory && (_canViewInventory || _canAdjustInventory);
        ShoppingListPanel.IsVisible = _loggedIn && panel == Panel.ShoppingList && (_canViewInventory || _canAdjustInventory);
        PurchasePanel.IsVisible = _loggedIn && panel == Panel.Purchases;
        UpdateCameraDetection();
    }

    private void UpdateCameraDetection()
    {
        StopCameraViews();
        UpdateCameraControls();

        var activeHost = CheckerPanel.IsVisible ? VerifierCameraHost
            : CountPanel.IsVisible ? CountCameraHost
            : InventoryPanel.IsVisible ? InventoryCameraHost
            : PurchasePanel.IsVisible ? PurchaseCameraHost
            : null;
        MoveCameraToHost(activeHost);
        if (activeHost is null || !_cameraPermissionGranted) return;

        _cameraStartCancellation = new CancellationTokenSource();
        _ = StartCameraAfterLayoutAsync(_cameraView, _cameraStartCancellation.Token);
    }

    private void MoveCameraToHost(Grid? targetHost)
    {
        if (_cameraView.Parent is Grid currentHost && currentHost != targetHost)
            currentHost.Children.Remove(_cameraView);

        if (targetHost is not null && _cameraView.Parent != targetHost)
            targetHost.Children.Insert(0, _cameraView);
    }

    private async Task StartCameraAfterLayoutAsync(CameraBarcodeReaderView view, CancellationToken cancellationToken)
    {
        try
        {
            _cameraInitializing = true;
            UpdateCameraControls();
            await Task.Delay(500, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !view.IsVisible) return;

            IReadOnlyList<CameraInfo> cameras = [];
            for (var attempt = 0; attempt < 3 && cameras.Count == 0; attempt++)
            {
                cameras = await view.GetAvailableCameras();
                if (cameras.Count == 0 && attempt < 2)
                    await Task.Delay(500, cancellationToken);
            }
            _hasFrontCamera = cameras.Any(camera => camera.Location == CameraLocation.Front);
            _hasRearCamera = cameras.Any(camera => camera.Location == CameraLocation.Rear);
            _cameraAvailable = cameras.Count > 0;
            if (!_cameraAvailable)
            {
                _cameraLocation = CameraLocation.Rear;
                UpdateCameraControls();
                return;
            }

            if (_cameraLocation == CameraLocation.Front && !_hasFrontCamera)
                _cameraLocation = CameraLocation.Rear;
            else if (_cameraLocation == CameraLocation.Rear && !_hasRearCamera)
                _cameraLocation = CameraLocation.Front;

            view.SelectedCamera = null;
            view.CameraLocation = _cameraLocation;

            UpdateCameraControls();
            await Task.Delay(150, cancellationToken);
            if (!cancellationToken.IsCancellationRequested && view.IsVisible)
                view.IsDetecting = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            _cameraAvailable = false;
            UpdateCameraControls();
        }
        finally
        {
            _cameraInitializing = false;
            UpdateCameraControls();
        }
    }

    private void StopCameraViews()
    {
        _cameraStartCancellation?.Cancel();
        _cameraStartCancellation?.Dispose();
        _cameraStartCancellation = null;
        _cameraView.IsDetecting = false;
    }

    private void UpdateCameraControls()
    {
        var cameraName = _cameraLocation == CameraLocation.Front ? "Cámara frontal" : "Cámara trasera";
        var switchText = _cameraLocation == CameraLocation.Front ? "Usar trasera" : "Usar frontal";
        VerifierCameraButton.Text = switchText;
        CountCameraButton.Text = switchText;
        InventoryCameraButton.Text = switchText;
        PurchaseCameraButton.Text = switchText;
        VerifierCameraName.Text = cameraName;
        CountCameraName.Text = cameraName;
        InventoryCameraName.Text = cameraName;
        PurchaseCameraName.Text = cameraName;

        var canSwitch = _cameraAvailable && _hasFrontCamera && _hasRearCamera;
        VerifierCameraButton.IsEnabled = canSwitch;
        CountCameraButton.IsEnabled = canSwitch;
        InventoryCameraButton.IsEnabled = canSwitch;
        PurchaseCameraButton.IsEnabled = canSwitch;
        var statusText = _cameraInitializing
            ? "Iniciando cámara…"
            : !_cameraPermissionGranted
                ? PurchasePanel.IsVisible ? "Activa el permiso de cámara en Ajustes para escanear productos." : "Activa el permiso de cámara en Ajustes para escanear. También puedes buscar el artículo."
                : PurchasePanel.IsVisible ? "No se pudo iniciar la cámara. Reintenta para continuar con el escaneo." : "No se pudo iniciar la cámara. Reintenta o busca el artículo manualmente.";
        VerifierCameraStatus.Text = statusText;
        CountCameraStatus.Text = statusText;
        InventoryCameraStatus.Text = statusText;
        PurchaseCameraStatus.Text = statusText;
        var showStatus = _cameraInitializing || !_cameraAvailable;
        VerifierCameraStatus.IsVisible = showStatus && CheckerPanel.IsVisible;
        CountCameraStatus.IsVisible = showStatus && CountPanel.IsVisible;
        InventoryCameraStatus.IsVisible = showStatus && InventoryPanel.IsVisible;
        PurchaseCameraStatus.IsVisible = showStatus && PurchasePanel.IsVisible;
        VerifierCameraRetryButton.IsVisible = showStatus && CheckerPanel.IsVisible && _cameraPermissionGranted && !_cameraInitializing;
        CountCameraRetryButton.IsVisible = showStatus && CountPanel.IsVisible && _cameraPermissionGranted && !_cameraInitializing;
        InventoryCameraRetryButton.IsVisible = showStatus && InventoryPanel.IsVisible && _cameraPermissionGranted && !_cameraInitializing;
        PurchaseCameraRetryButton.IsVisible = showStatus && PurchasePanel.IsVisible && _cameraPermissionGranted && !_cameraInitializing;
        VerifierCameraSettingsButton.IsVisible = showStatus && CheckerPanel.IsVisible && !_cameraPermissionGranted;
        CountCameraSettingsButton.IsVisible = showStatus && CountPanel.IsVisible && !_cameraPermissionGranted;
        InventoryCameraSettingsButton.IsVisible = showStatus && InventoryPanel.IsVisible && !_cameraPermissionGranted;
        PurchaseCameraSettingsButton.IsVisible = showStatus && PurchasePanel.IsVisible && !_cameraPermissionGranted;
    }

    private async void OnRetryCameraClicked(object? sender, EventArgs e) => await EnableCameraAsync(requestPermission: true);

    private void OnOpenCameraSettingsClicked(object? sender, EventArgs e) => AppInfo.ShowSettingsUI();

    private async void OnSwitchCameraClicked(object? sender, EventArgs e)
    {
        if (!_cameraAvailable || !_hasFrontCamera || !_hasRearCamera) return;

        StopCameraViews();
        _cameraLocation = _cameraLocation == CameraLocation.Front ? CameraLocation.Rear : CameraLocation.Front;
        _cameraView.SelectedCamera = null;
        _cameraView.CameraLocation = _cameraLocation;
        UpdateCameraControls();
        await Task.Delay(200);
        UpdateCameraDetection();
    }

    private async void OnPairClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            await _api.PairAsync(ServerEntry.Text ?? string.Empty, PairingCodeEntry.Text ?? string.Empty, DeviceEntry.Text ?? string.Empty);
            PairingCodeEntry.Text = string.Empty;
            ShowState(true, false);
            await DisplayAlertAsync("Teléfono conectado", "Ahora inicia sesión para consultar productos.", "Continuar");
        });
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        await ExecuteAsync(async () =>
        {
            var result = await _api.LoginAsync(UserEntry.Text ?? string.Empty, PasswordEntry.Text ?? string.Empty);
            PasswordEntry.Text = string.Empty;
            ApplyCapabilities(new MobileCapabilities(result.CanViewInventory, result.CanAdjustInventory));
            ShowState(true, true);
            ConnectionLabel.Text = $"Conectado: {result.DisplayName}";
            await EnableCameraAsync();
        });
    }

    private void OnOpenVerifier(object? sender, TappedEventArgs e)
    {
        if (!_canViewInventory) return;
        SearchEntry.Text = string.Empty;
        ProductCard.IsVisible = false;
        ResultsView.ItemsSource = null;
        EmptyLabel.IsVisible = true;
        EmptyLabel.Text = "Escanea un producto o escribe un código para consultarlo.";
        SetPanel(Panel.Verifier);
    }

    private async void OnOpenCount(object? sender, TappedEventArgs e)
    {
        if (!_canAdjustInventory) return;
        var proceed = await DisplayAlertAsync("Conteo de inventario", "Se recomienda contar fuera del horario de ventas. Durante la operación, una venta simultánea puede causar diferencias; verifica cada artículo antes de registrarlo.", "Continuar", "Ahora no");
        if (!proceed) return;

        await ExecuteAsync(async () =>
        {
            var session = await _api.StartCountSessionAsync();
            _countSessionId = session.SessionId;
            _sessionCounts.Clear();
            _selectedCountProduct = null;
            CountProductCard.IsVisible = false;
            CountResultsView.ItemsSource = null;
            CountSearchEntry.Text = string.Empty;
            CountSummaryLabel.Text = "Aún no hay artículos contados.";
            CountDifferenceSummary.Text = string.Empty;
            SetPanel(Panel.Count);
            CountSearchEntry.Focus();
        });
    }

    private async void OnOpenInventory(object? sender, TappedEventArgs e)
    {
        if (!_canViewInventory && !_canAdjustInventory) return;
        _inventoryStatus = "all";
        UpdateInventoryFilterButtons();
        SetPanel(Panel.Inventory);
        await LoadInventoryAsync(string.Empty);
    }

    private async void OnOpenShoppingList(object? sender, TappedEventArgs e)
    {
        SetPanel(Panel.ShoppingList);
        if (_shoppingListLoaded) return;

        await ExecuteAsync(async () =>
        {
            foreach (var item in await _shoppingListStore.LoadAsync())
            {
                item.PropertyChanged += OnShoppingItemPropertyChanged;
                _shoppingItems.Add(item);
            }
            _shoppingListLoaded = true;
            UpdateShoppingListSummary();
        });
    }

    private async void OnOpenPurchases(object? sender, TappedEventArgs e)
    {
        SetPanel(Panel.Purchases);
        await ExecuteAsync(async () =>
        {
            var suppliers = await _api.GetPurchaseSuppliersAsync();
            var options = new List<MobilePurchaseSupplier> { new(null, "Sin proveedor") };
            options.AddRange(suppliers);
            PurchaseSupplierPicker.ItemsSource = options;
            PurchaseSupplierPicker.SelectedIndex = 0;
            PurchaseScanMessage.Text = "Escanea un producto para agregarlo a la solicitud.";
        });
    }

    private async void OnBackToMenu(object? sender, EventArgs e)
    {
        await CloseCountSessionSafelyAsync();
        SetPanel(Panel.Home);
    }

    private void OnBackToCount(object? sender, EventArgs e) => SetPanel(Panel.Count);

    private async void OnOpenHistory(object? sender, EventArgs e)
    {
        _historyDifferencesOnly = false;
        HistoryFilterButton.Text = "Todos";
        await LoadHistoryAsync();
        SetPanel(Panel.History);
    }

    private async void OnToggleHistoryFilter(object? sender, EventArgs e)
    {
        _historyDifferencesOnly = !_historyDifferencesOnly;
        HistoryFilterButton.Text = _historyDifferencesOnly ? "Diferencias" : "Todos";
        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        await ExecuteAsync(async () => HistoryResultsView.ItemsSource = await _api.GetCountHistoryAsync(_historyDifferencesOnly));
    }

    private async void OnSearchClicked(object? sender, EventArgs e) => await SearchAsync(SearchEntry.Text);
    private async void OnSearchCompleted(object? sender, EventArgs e) => await SearchAsync(SearchEntry.Text);

    private async Task SearchAsync(string? value)
    {
        var query = value?.Trim() ?? string.Empty;
        if (query.Length == 0) return;
        await ExecuteAsync(async () =>
        {
            var exact = await _api.FindByCodeAsync(query);
            if (exact is not null) { ShowProduct(exact); return; }
            var matches = await _api.SearchAsync(query);
            ResultsView.ItemsSource = matches;
            ProductCard.IsVisible = false;
            EmptyLabel.IsVisible = true;
            EmptyLabel.Text = matches.Count == 0 ? "No se encontró un producto con ese código o nombre." : "Selecciona un producto para ver su existencia.";
        });
    }

    private async void OnCountSearchClicked(object? sender, EventArgs e) => await SearchCountProductAsync(CountSearchEntry.Text);
    private async void OnCountSearchCompleted(object? sender, EventArgs e) => await SearchCountProductAsync(CountSearchEntry.Text);

    private async Task SearchCountProductAsync(string? value)
    {
        var query = value?.Trim() ?? string.Empty;
        if (query.Length == 0) return;
        await ExecuteAsync(async () =>
        {
            var exact = await _api.FindByCodeAsync(query);
            if (exact is not null) { ShowCountProduct(exact); return; }
            CountProductCard.IsVisible = false;
            CountResultsView.ItemsSource = await _api.SearchAsync(query);
        });
    }

    private async void OnInventorySearchClicked(object? sender, EventArgs e) => await LoadInventoryAsync(InventorySearchEntry.Text);
    private async void OnInventorySearchCompleted(object? sender, EventArgs e) => await LoadInventoryAsync(InventorySearchEntry.Text);

    private async Task LoadPurchaseProductAsync(string code)
    {
        var query = code.Trim();
        if (query.Length == 0) return;
        await ExecuteAsync(async () =>
        {
            var product = await _api.FindPurchaseProductByCodeAsync(query);
            if (product is null)
            {
                PurchaseScannedProductCard.IsVisible = false;
                _selectedPurchaseProduct = null;
                PurchaseScanMessage.Text = $"No se encontró el código {query}. No se agregó ningún producto.";
                PurchaseScanMessage.TextColor = Color.FromArgb("#FFD16A");
                return;
            }

            _selectedPurchaseProduct = product;
            PurchaseScannedProductName.Text = product.Description;
            PurchaseScannedProductCode.Text = $"Código: {product.Code} · Unidad: {product.UnitOfMeasure}";
            PurchaseQuantityEntry.Text = string.Empty;
            PurchaseUnitCostEntry.Text = string.Empty;
            PurchaseScannedProductCard.IsVisible = true;
            PurchaseScanMessage.Text = "Producto reconocido. Captura piezas y costo del proveedor.";
            PurchaseScanMessage.TextColor = Color.FromArgb("#B8C7D8");
            PurchaseQuantityEntry.Focus();
        });
    }

    private async void OnAddPurchaseLine(object? sender, EventArgs e)
    {
        if (_selectedPurchaseProduct is not MobilePurchaseProduct product) return;
        if (!TryReadDecimal(PurchaseQuantityEntry.Text, out var quantity) || quantity <= 0m)
        {
            await DisplayAlertAsync("Cantidad no válida", "Escribe una cantidad mayor que cero.", "Aceptar");
            PurchaseQuantityEntry.Focus();
            return;
        }
        if (!TryReadDecimal(PurchaseUnitCostEntry.Text, out var unitCost) || unitCost < 0m)
        {
            await DisplayAlertAsync("Costo no válido", "Escribe el costo por pieza que indicó el proveedor.", "Aceptar");
            PurchaseUnitCostEntry.Focus();
            return;
        }

        quantity = decimal.Round(quantity, 3, MidpointRounding.AwayFromZero);
        unitCost = decimal.Round(unitCost, 2, MidpointRounding.AwayFromZero);
        var current = _purchaseLines.FirstOrDefault(item => item.ProductId == product.Id);
        if (current is not null)
        {
            if (current.UnitCost != unitCost)
            {
                await DisplayAlertAsync("Costo diferente", "Este producto ya está en la solicitud con otro costo. Quita la partida anterior y vuelve a capturarla para evitar mezclar precios.", "Aceptar");
                return;
            }
            current.Quantity += quantity;
        }
        else
        {
            _purchaseLines.Insert(0, new MobilePurchaseReceiptLine(product, quantity, unitCost));
        }

        _selectedPurchaseProduct = null;
        PurchaseScannedProductCard.IsVisible = false;
        PurchaseScanMessage.Text = $"{product.Description} agregado a la solicitud.";
        PurchaseScanMessage.TextColor = Color.FromArgb("#52D998");
        PurchaseQuantityEntry.Unfocus();
        PurchaseUnitCostEntry.Unfocus();
        UpdatePurchaseTotals();
    }

    private void OnRemovePurchaseLine(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is MobilePurchaseReceiptLine line)
            _purchaseLines.Remove(line);
    }

    private void OnPurchaseCashToggled(object? sender, ToggledEventArgs e)
    {
        PurchaseCashAmountPanel.IsVisible = e.Value;
        if (e.Value && string.IsNullOrWhiteSpace(PurchaseCashAmountEntry.Text))
            PurchaseCashAmountEntry.Text = _purchaseLines.Sum(line => line.LineTotal).ToString("0.00", CultureInfo.CurrentCulture);
    }

    private void UpdatePurchaseTotals()
    {
        var total = _purchaseLines.Sum(item => item.LineTotal);
        PurchaseTotalLabel.Text = total.ToString("C2", CultureInfo.CurrentCulture);
        PurchaseEmptyLabel.IsVisible = _purchaseLines.Count == 0;
        PurchaseLinesView.IsVisible = _purchaseLines.Count > 0;
        SendPurchaseRequestButton.IsEnabled = _purchaseLines.Count > 0 && !_submittingPurchase;
    }

    private async void OnSendPurchaseRequest(object? sender, EventArgs e)
    {
        if (_submittingPurchase) return;
        if (_purchaseLines.Count == 0)
        {
            await DisplayAlertAsync("Solicitud vacía", "Escanea y agrega los productos que entregó el proveedor.", "Aceptar");
            return;
        }
        decimal? withdrawn = null;
        if (PurchaseCashSwitch.IsToggled)
        {
            if (!TryReadDecimal(PurchaseCashAmountEntry.Text, out var amount) || amount < 0m)
            {
                await DisplayAlertAsync("Efectivo no válido", "Escribe el monto de efectivo que propones retirar de caja.", "Aceptar");
                PurchaseCashAmountEntry.Focus();
                return;
            }
            withdrawn = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        }

        var supplier = PurchaseSupplierPicker.SelectedItem as MobilePurchaseSupplier;
        var command = new CreateMobilePurchaseRequestCommand(_purchaseOperationId, supplier?.Id, PurchaseCashSwitch.IsToggled, withdrawn,
            _purchaseLines.Select(line => new MobilePurchaseRequestLineCommand(line.ProductId, line.Quantity, line.UnitCost)).ToList());
        _submittingPurchase = true;
        SendPurchaseRequestButton.IsEnabled = false;
        StopCameraViews();
        await ExecuteAsync(async () =>
        {
            var result = await _api.CreatePurchaseRequestAsync(command);
            _lastPurchaseRequestId = result.Id;
            _purchaseOperationId = Guid.NewGuid();
            _purchaseLines.Clear();
            PurchaseScannedProductCard.IsVisible = false;
            PurchaseCashSwitch.IsToggled = false;
            PurchaseCashAmountEntry.Text = string.Empty;
            PurchaseRequestStatusLabel.Text = $"Solicitud recibida por caja. Estado: {PurchaseRequestStatusText(result.Status)}.";
            PurchaseRequestStatusCard.IsVisible = true;
            RefreshPurchaseRequestButton.IsEnabled = true;
            UpdatePurchaseTotals();
            await DisplayAlertAsync("Solicitud enviada", "La caja puede continuar vendiendo. La mercancía y el retiro quedarán registrados cuando un usuario autorizado revise y acepte la solicitud.", "Aceptar");
        });
        _submittingPurchase = false;
        SendPurchaseRequestButton.IsEnabled = _purchaseLines.Count > 0;
        if (_loggedIn && PurchasePanel.IsVisible) UpdateCameraDetection();
    }

    private async void OnRefreshPurchaseRequest(object? sender, EventArgs e)
    {
        if (_lastPurchaseRequestId is not Guid requestId) return;
        RefreshPurchaseRequestButton.IsEnabled = false;
        await ExecuteAsync(async () =>
        {
            var request = await _api.GetPurchaseRequestAsync(requestId);
            if (request is null)
            {
                PurchaseRequestStatusLabel.Text = "No se encontró el estado de esta solicitud.";
                return;
            }
            var status = PurchaseRequestStatusText(request.Status);
            PurchaseRequestStatusLabel.Text = request.Status == "Rejected" && !string.IsNullOrWhiteSpace(request.RejectionReason)
                ? $"Solicitud {status.ToLowerInvariant()}. Motivo: {request.RejectionReason}"
                : $"Solicitud {status.ToLowerInvariant()}{(request.ReviewedAtUtc is DateTimeOffset reviewed ? $" · {reviewed.ToLocalTime():g}" : string.Empty)}.";
        });
        RefreshPurchaseRequestButton.IsEnabled = true;
    }

    private static string PurchaseRequestStatusText(string status) => status switch
    {
        "Pending" => "pendiente de autorización en caja",
        "Approved" => "aprobada; compra y retiro registrados",
        "Rejected" => "rechazada; no se modificó el inventario ni la caja",
        _ => "en revisión"
    };

    private static bool TryReadDecimal(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
        || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private async void OnShoppingSearchClicked(object? sender, EventArgs e) => await SearchShoppingProductAsync(ShoppingSearchEntry.Text);
    private async void OnShoppingSearchCompleted(object? sender, EventArgs e) => await SearchShoppingProductAsync(ShoppingSearchEntry.Text);

    private async Task SearchShoppingProductAsync(string? value)
    {
        var query = value?.Trim() ?? string.Empty;
        if (query.Length == 0) return;
        await ExecuteAsync(async () =>
        {
            var exact = await _api.FindByCodeAsync(query);
            if (exact is not null)
            {
                ShowShoppingProduct(exact);
                return;
            }

            ShoppingSelectedProduct.IsVisible = false;
            _selectedShoppingProduct = null;
            var matches = await _api.SearchAsync(query);
            ShoppingSearchResults.ItemsSource = matches;
            ShoppingSearchResults.IsVisible = matches.Count > 0;
            ShoppingSearchMessage.IsVisible = true;
            ShoppingSearchMessage.Text = matches.Count == 0
                ? "No se encontraron productos con esa búsqueda."
                : "Selecciona el producto que deseas agregar.";
        });
    }

    private void OnShoppingSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MobileProduct product) return;
        if (sender is CollectionView collection) collection.SelectedItem = null;
        ShowShoppingProduct(product);
    }

    private void ShowShoppingProduct(MobileProduct product)
    {
        _selectedShoppingProduct = product;
        ShoppingSelectedName.Text = product.Description;
        ShoppingSelectedCode.Text = $"Código: {product.Code} · {product.StockDisplay} disponibles";
        ShoppingQuantityEntry.Text = "1";
        ShoppingSelectedProduct.IsVisible = true;
        ShoppingSearchResults.IsVisible = false;
        ShoppingSearchMessage.IsVisible = false;
        ShoppingQuantityEntry.Focus();
    }

    private async void OnAddShoppingProduct(object? sender, EventArgs e)
    {
        if (_selectedShoppingProduct is not MobileProduct product) return;
        if (!decimal.TryParse(ShoppingQuantityEntry.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity)
            && !decimal.TryParse(ShoppingQuantityEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity))
        {
            await DisplayAlertAsync("Cantidad no válida", "Escribe una cantidad mayor que cero.", "Aceptar");
            return;
        }
        if (quantity <= 0m)
        {
            await DisplayAlertAsync("Cantidad no válida", "Escribe una cantidad mayor que cero.", "Aceptar");
            return;
        }

        var item = _shoppingItems.FirstOrDefault(existing => existing.ProductId == product.Id);
        if (item is null)
        {
            item = new MobileShoppingListItem
            {
                ProductId = product.Id,
                Code = product.Code,
                Description = product.Description,
                UnitOfMeasure = product.UnitOfMeasure,
                Quantity = quantity
            };
            item.PropertyChanged += OnShoppingItemPropertyChanged;
            _shoppingItems.Insert(0, item);
        }
        else
        {
            item.Quantity += quantity;
            item.IsObtained = false;
        }

        await SaveShoppingListAsync();
        ShoppingSearchEntry.Text = string.Empty;
        ShoppingSelectedProduct.IsVisible = false;
        _selectedShoppingProduct = null;
        UpdateShoppingListSummary();
    }

    private async void OnIncreaseShoppingQuantity(object? sender, EventArgs e)
    {
        if (GetShoppingItem(sender) is { } item)
        {
            item.Quantity += 1m;
            await SaveShoppingListAsync();
        }
    }

    private async void OnDecreaseShoppingQuantity(object? sender, EventArgs e)
    {
        if (GetShoppingItem(sender) is { } item)
        {
            item.Quantity = Math.Max(1m, item.Quantity - 1m);
            await SaveShoppingListAsync();
        }
    }

    private async void OnToggleShoppingItem(object? sender, EventArgs e)
    {
        if (GetShoppingItem(sender) is { } item)
        {
            item.IsObtained = !item.IsObtained;
            await SaveShoppingListAsync();
        }
    }

    private async void OnRemoveShoppingItem(object? sender, EventArgs e)
    {
        if (GetShoppingItem(sender) is not { } item) return;
        item.PropertyChanged -= OnShoppingItemPropertyChanged;
        _shoppingItems.Remove(item);
        await SaveShoppingListAsync();
    }

    private static MobileShoppingListItem? GetShoppingItem(object? sender) =>
        (sender as Button)?.CommandParameter as MobileShoppingListItem;

    private void OnShoppingItemPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateShoppingListSummary();

    private async Task SaveShoppingListAsync()
    {
        try { await _shoppingListStore.SaveAsync(_shoppingItems.ToList()); }
        catch (Exception exception) { await DisplayAlertAsync("No se guardó la lista", exception.Message, "Aceptar"); }
    }

    private void UpdateShoppingListSummary()
    {
        var pending = _shoppingItems.Count(item => !item.IsObtained);
        var obtained = _shoppingItems.Count - pending;
        ShoppingListCountLabel.Text = $"{pending} por surtir · {obtained} surtidos";
        ShoppingEmptyLabel.IsVisible = _shoppingItems.Count == 0;
        ShoppingListView.IsVisible = _shoppingItems.Count > 0;
    }

    private async Task LoadInventoryAsync(string? query)
    {
        await ExecuteAsync(async () =>
        {
            var products = await _api.BrowseInventoryAsync(query, _inventoryStatus);
            InventoryResultsView.ItemsSource = products;
            InventorySummaryLabel.Text = products.Count == 0 ? "No hay artículos que coincidan con esta búsqueda." : $"{products.Count} artículo(s) · muestra de hasta 100";
        });
    }

    private async void OnInventoryFilterAll(object? sender, EventArgs e) => await SetInventoryFilterAsync("all");
    private async void OnInventoryFilterLow(object? sender, EventArgs e) => await SetInventoryFilterAsync("low");
    private async void OnInventoryFilterOut(object? sender, EventArgs e) => await SetInventoryFilterAsync("out");

    private async Task SetInventoryFilterAsync(string status)
    {
        _inventoryStatus = status;
        UpdateInventoryFilterButtons();
        await LoadInventoryAsync(InventorySearchEntry.Text);
    }

    private void UpdateInventoryFilterButtons()
    {
        InventoryAllButton.BackgroundColor = _inventoryStatus == "all" ? Color.FromArgb("#0878E5") : Color.FromArgb("#12385A");
        InventoryLowButton.BackgroundColor = _inventoryStatus == "low" ? Color.FromArgb("#0878E5") : Color.FromArgb("#12385A");
        InventoryOutButton.BackgroundColor = _inventoryStatus == "out" ? Color.FromArgb("#0878E5") : Color.FromArgb("#12385A");
    }

    private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var code = e.Results.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(code) || _readingBarcode) return;
        _readingBarcode = true;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (sender is ZXing.Net.Maui.Controls.CameraBarcodeReaderView camera) camera.IsDetecting = false;
            switch (_activePanel)
            {
                case Panel.Verifier:
                    SearchEntry.Text = code;
                    await SearchAsync(code);
                    break;
                case Panel.Count:
                    CountSearchEntry.Text = code;
                    await SearchCountProductAsync(code);
                    break;
                case Panel.Inventory:
                    InventorySearchEntry.Text = code;
                    await LoadInventoryAsync(code);
                    break;
                case Panel.Purchases:
                    await LoadPurchaseProductAsync(code);
                    break;
            }
            await Task.Delay(800);
            _readingBarcode = false;
            UpdateCameraDetection();
        });
    }

    private void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is MobileProduct product) ShowProduct(product);
    }

    private void OnCountResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is MobileProduct product) ShowCountProduct(product);
    }

    private async void OnInventoryResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not MobileProduct product) return;
        if (sender is CollectionView collection) collection.SelectedItem = null;
        await DisplayAlertAsync(product.Description, $"Código: {product.Code}\nExistencia: {product.StockDisplay}\nEstado: {product.StockStatus}\nPrecio de venta: {product.Price:C2}\nMínimo: {product.MinimumStock:0.###}\nMáximo: {(product.MaximumStock > 0m ? product.MaximumStock.ToString("0.###") : "Sin definir")}", "Cerrar");
    }

    private void ShowProduct(MobileProduct product)
    {
        ResultsView.ItemsSource = null;
        ProductCard.IsVisible = true;
        EmptyLabel.IsVisible = false;
        ProductNameLabel.Text = product.Description;
        ProductCodeLabel.Text = $"Código: {product.Code}";
        StockStatusLabel.Text = product.StockStatus;
        StockStatusLabel.TextColor = product.StockColor;
        StockDot.Color = product.StockColor;
        StockLabel.Text = product.StockDisplay;
        StockLabel.TextColor = product.StockColor;
        PriceLabel.Text = product.Price.ToString("C2");
        MinimumLabel.Text = $"Mínimo: {product.MinimumStock:0.###}";
        MaximumLabel.Text = product.MaximumStock > 0m ? $"Máximo: {product.MaximumStock:0.###}" : "Máximo: sin definir";
    }

    private void ShowCountProduct(MobileProduct product)
    {
        _selectedCountProduct = product;
        CountResultsView.ItemsSource = null;
        CountProductCard.IsVisible = true;
        CountProductName.Text = product.Description;
        CountProductCode.Text = $"Código: {product.Code}";
        CountSystemStock.Text = product.StockDisplay;
        CountedStockEntry.Text = string.Empty;
        CountedStockEntry.Focus();
    }

    private async void OnRegisterCount(object? sender, EventArgs e)
    {
        if (_selectedCountProduct is null || _countSessionId is null)
        {
            await DisplayAlertAsync("Selecciona un artículo", "Escanea o busca el producto que vas a contar.", "Aceptar");
            return;
        }
        if (!decimal.TryParse(CountedStockEntry.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var counted) &&
            !decimal.TryParse(CountedStockEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out counted))
        {
            await DisplayAlertAsync("Cantidad no válida", "Escribe la cantidad física que encontraste. Puedes usar hasta tres decimales.", "Aceptar");
            CountedStockEntry.Focus();
            return;
        }
        if (counted < 0m || decimal.Round(counted, 3, MidpointRounding.AwayFromZero) != counted)
        {
            await DisplayAlertAsync("Cantidad no válida", "La cantidad debe ser cero o mayor y tener como máximo tres decimales.", "Aceptar");
            return;
        }

        var product = _selectedCountProduct;
        var difference = decimal.Round(counted - product.Stock, 3, MidpointRounding.AwayFromZero);
        if (difference != 0m)
        {
            var changeText = difference < 0m ? $"faltan {Math.Abs(difference):0.###}" : $"sobran {difference:0.###}";
            var confirm = await DisplayAlertAsync("Diferencia de inventario", $"{product.Description}\nEn sistema: {product.Stock:0.###}\nContado: {counted:0.###}\n\nSe actualizará el inventario: {changeText} {product.UnitOfMeasure}.", "Registrar conteo", "Revisar");
            if (!confirm) return;
        }

        await ExecuteAsync(async () =>
        {
            var operationId = Guid.NewGuid();
            var result = await _api.RegisterCountAsync(new MobileCountRequest(_countSessionId.Value, operationId, product.Id, product.Stock, counted));
            var index = -1;
            for (var i = 0; i < _sessionCounts.Count; i++)
                if (_sessionCounts[i].ProductId == result.ProductId) { index = i; break; }
            if (index >= 0) _sessionCounts[index] = result;
            else _sessionCounts.Insert(0, result);
            CountSystemStock.Text = $"{result.CurrentStock:0.###} {product.UnitOfMeasure}";
            _selectedCountProduct = product with { Stock = result.CurrentStock };
            CountedStockEntry.Text = string.Empty;
            UpdateCountSummary();
            await DisplayAlertAsync(result.Difference == 0m ? "Conteo registrado" : "Conteo y ajuste registrados",
                result.Difference == 0m ? $"{result.Description}: las existencias coinciden ({result.CountedStock:0.###})." : $"{result.Description}: existencia actualizada de {result.SystemStock:0.###} a {result.CountedStock:0.###}. Diferencia: {result.DifferenceDisplay}.", "Aceptar");
            CountSearchEntry.Text = string.Empty;
            CountSearchEntry.Focus();
        });
    }

    private void UpdateCountSummary()
    {
        var different = _sessionCounts.Count(item => item.Difference != 0m);
        CountSummaryLabel.Text = $"{_sessionCounts.Count} artículo(s) distintos contados";
        CountDifferenceSummary.Text = different == 0 ? "Sin diferencias" : $"{different} con diferencia";
        CountDifferenceSummary.TextColor = different == 0 ? Color.FromArgb("#52D998") : Color.FromArgb("#FFD16A");
    }

    private async Task CloseCountSessionSafelyAsync()
    {
        if (_countSessionId is not Guid sessionId) return;
        _countSessionId = null;
        try { await _api.CloseCountSessionAsync(sessionId); }
        catch (Exception) { }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        await CloseCountSessionSafelyAsync();
        await _api.LogoutAsync();
        _canViewInventory = false;
        _canAdjustInventory = false;
        ShowState(true, false);
    }

    private async Task EnableCameraAsync(bool requestPermission = true)
    {
        if (_cameraPermissionRequestInProgress) return;
        _cameraPermissionRequestInProgress = true;
        try
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (requestPermission && permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.Camera>();

            _cameraPermissionGranted = permission == PermissionStatus.Granted;
            _cameraAvailable = false;
            _hasFrontCamera = false;
            _hasRearCamera = false;

            UpdateCameraControls();
            UpdateCameraDetection();
        }
        finally
        {
            _cameraPermissionRequestInProgress = false;
        }
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            await action();
        }
        catch (MobileSessionExpiredException exception)
        {
            ShowState(true, false);
            await DisplayAlertAsync("Sesión vencida", exception.Message, "Aceptar");
        }
        catch (MobileCountConflictException exception)
        {
            await DisplayAlertAsync("Existencia actualizada", exception.Message, "Volver a consultar");
            if (_selectedCountProduct is not null)
            {
                var fresh = await _api.FindByCodeAsync(_selectedCountProduct.Code);
                if (fresh is not null) ShowCountProduct(fresh);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            await DisplayAlertAsync("No se pudo completar", exception is TaskCanceledException ? "JetVenta tardó demasiado en responder. Revisa que el teléfono esté en el Wi-Fi de la tienda." : exception.Message, "Aceptar");
        }
        catch (ArgumentException exception)
        {
            await DisplayAlertAsync("Revisa los datos", exception.Message, "Aceptar");
        }
        catch (Exception)
        {
            await DisplayAlertAsync("No se pudo completar", "Ocurrió un problema inesperado. Revisa los datos de conexión e inténtalo nuevamente.", "Aceptar");
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }
    }

    private enum Panel { Home, Verifier, Count, History, Inventory, ShoppingList, Purchases }
}
