using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Json;

namespace Pos.Desktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:5000") };
    private CancellationTokenSource? _searchCancellation;
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
            NavigateTo(section);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            ShowPendingFeature("Cobro");
            e.Handled = true;
        }
    }

    private void OnSalesActionClick(object sender, RoutedEventArgs e) =>
        ShowPendingFeature("Accion de ventas");

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

    private sealed record SaleResponse(decimal Total, decimal Change);

    private async void OnChargeClick(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0) { StatusText.Text = "Agrega al menos un producto antes de cobrar."; return; }
        var cashWindow = new CashWindow(_cart.Sum(item => item.Total)) { Owner = this };
        if (cashWindow.ShowDialog() != true || cashWindow.Received is null) return;
        try
        {
            var command = new { operationId = Guid.NewGuid(), lines = _cart.Select(item => new { productId = item.ProductId, quantity = item.Quantity }).ToArray(), cashReceived = cashWindow.Received.Value };
            using var response = await Client.PostAsJsonAsync("/api/sales/complete", command);
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<SaleResponse>();
            _cart.Clear(); CartList.Items.Refresh(); StatusText.Text = result is null ? "Venta confirmada." : $"Venta confirmada. Cambio: ${result.Change:0.00}";
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo conectar con la API para confirmar la venta."; }
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
