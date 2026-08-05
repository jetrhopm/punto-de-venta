using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Json;

namespace Pos.Desktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:5000") };
    private CancellationTokenSource? _searchCancellation;
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
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

    private sealed record ProductSearchResult(Guid Id, string Code, string Description, decimal Price);
    private sealed record ProductSearchRow(ProductSearchResult Product)
    {
        public string DisplayText => $"{Product.Code} | {Product.Description} | ${Product.Price:0.00}";
    }

    private void OnChargeClick(object sender, RoutedEventArgs e) =>
        ShowPendingFeature("Cobro");

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
}
