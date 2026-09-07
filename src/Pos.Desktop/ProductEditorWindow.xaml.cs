using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pos.Desktop;

public partial class ProductEditorWindow : Window
{
    private readonly ProductEditModel? _original;
    private readonly string _weightUnit;
    private readonly bool _autoPriceWithProfit;
    private readonly decimal _defaultProfitPercent;
    private bool _loading;

    public ProductEditModel? SavedProduct { get; private set; }

    public ProductEditorWindow(ProductEditModel? product, IReadOnlyList<DepartmentOption> departments, string weightUnit, bool autoPriceWithProfit, decimal defaultProfitPercent)
    {
        InitializeComponent();
        _original = product;
        _weightUnit = weightUnit;
        _autoPriceWithProfit = autoPriceWithProfit;
        _defaultProfitPercent = defaultProfitPercent;
        DepartmentBox.ItemsSource = new[] { new DepartmentOption(null, "Sin departamento") }.Concat(departments).ToArray();
        UnitBox.ItemsSource = new[] { "Pieza", "Granel (unidad configurada)", "Kilogramo", "Gramo", "Libra", "Onza", "Litro", "Mililitro", "Metro", "Servicio" };
        Loaded += (_, _) => LoadForm();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; Close(); e.Handled = true; } };
    }

    private void LoadForm()
    {
        _loading = true;
        var product = _original;
        TitleText.Text = product is null ? "Nuevo producto" : "Editar producto";
        SubtitleText.Text = product is null ? "Los datos se agregarán al catálogo." : "Modifica la información del producto seleccionado.";
        CodeBox.Text = product?.Code ?? string.Empty;
        DescriptionBox.Text = product?.Description ?? string.Empty;
        DepartmentBox.SelectedValue = product?.DepartmentId;
        UnitBox.SelectedItem = product?.UnitOfMeasure ?? "Pieza";
        CostBox.Text = Money(product?.Cost ?? 0m);
        ProfitPercentBox.Text = product is null ? (_autoPriceWithProfit ? Percent(_defaultProfitPercent) : string.Empty) : Percent(product.ProfitPercent);
        PriceBox.Text = Money(product?.Price ?? 0m);
        WholesalePriceBox.Text = Money(product?.WholesalePrice ?? 0m);
        WholesaleProfitPercentBox.Text = product is null ? "0" : Percent(product.WholesaleProfitPercent);
        WholesaleMinimumBox.Text = Quantity(product?.WholesaleMinimumQuantity ?? 0m);
        InitialStockBox.Text = Quantity(product?.Stock ?? 0m);
        InitialStockBox.IsEnabled = product is null;
        InitialStockLabel.Text = product is null ? "Existencia inicial" : "Existencia actual";
        MinimumStockBox.Text = Quantity(product?.MinimumStock ?? 0m);
        MaximumStockBox.Text = Quantity(product?.MaximumStock ?? 0m);
        StockHintText.Text = product is null ? "La existencia inicial se registra como movimiento. Después podrás ajustarla desde Inventario." : "La existencia actual se conserva aquí. Para cambiarla usa Inventario > Ajustar y deja un movimiento auditable.";
        IsKitBox.IsChecked = product?.IsKit == true;
        _loading = false;
        UpdateProfitAmount();
        UpdateWholesaleProfitAmount();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadForm(out var command)) return;
        SaveButton.IsEnabled = false;
        try
        {
            using var response = _original is null
                ? await ApiClient.Client.PostAsJsonAsync("/api/products", command)
                : await ApiClient.Client.PutAsJsonAsync($"/api/products/{_original.Id}", command);
            if (!response.IsSuccessStatusCode)
            {
                var message = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo guardar el producto.");
                ShowResult("Producto no guardado", message, OperationResultKind.Error);
                return;
            }
            SavedProduct = new ProductEditModel(_original?.Id ?? Guid.Empty, CodeBox.Text.Trim(), DescriptionBox.Text.Trim(), DepartmentBox.SelectedValue is Guid id && id != Guid.Empty ? id : null, UnitValue(), Parse(CostBox.Text), Parse(PriceBox.Text), Parse(ProfitPercentBox.Text), Parse(WholesalePriceBox.Text), Parse(WholesaleProfitPercentBox.Text), Parse(WholesaleMinimumBox.Text), Parse(InitialStockBox.Text), Parse(MinimumStockBox.Text), Parse(MaximumStockBox.Text), IsKitBox.IsChecked == true);
            DialogResult = true;
            Close();
        }
        catch (Exception exception) { ShowResult("Producto no guardado", ConnectionHelp.FromException(exception, "No se pudo guardar el producto."), OperationResultKind.Error); }
        finally { SaveButton.IsEnabled = true; }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void OnPricingChanged(object sender, RoutedEventArgs e) { if (!_loading && TryDecimal(CostBox.Text, out var cost) && TryDecimal(ProfitPercentBox.Text, out var profit)) PriceBox.Text = Money(cost * (1m + profit / 100m)); UpdateProfitAmount(); }
    private void OnWholesalePricingChanged(object sender, RoutedEventArgs e) { if (!_loading && TryDecimal(CostBox.Text, out var cost) && TryDecimal(WholesaleProfitPercentBox.Text, out var profit)) WholesalePriceBox.Text = Money(cost * (1m + profit / 100m)); UpdateWholesaleProfitAmount(); }
    private void OnPriceChanged(object sender, TextChangedEventArgs e) => UpdateProfitAmount();
    private void OnWholesalePriceChanged(object sender, TextChangedEventArgs e) => UpdateWholesaleProfitAmount();
    private void OnStockLimitChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || !TryDecimal(MinimumStockBox.Text, out var minimum) || !TryDecimal(MaximumStockBox.Text, out var maximum)) return;
        MaximumStockBox.BorderBrush = maximum > 0m && maximum < minimum ? Brushes.IndianRed : (Brush)FindResource("LineBrush");
        MaximumStockBox.BorderThickness = maximum > 0m && maximum < minimum ? new Thickness(2) : new Thickness(1);
    }
    private void UpdateProfitAmount() => ProfitAmountText.Text = TryDecimal(PriceBox.Text, out var price) && TryDecimal(CostBox.Text, out var cost) ? $"Ganancia: {Money(price - cost)}" : "";
    private void UpdateWholesaleProfitAmount() => WholesaleProfitAmountText.Text = TryDecimal(WholesalePriceBox.Text, out var price) && TryDecimal(CostBox.Text, out var cost) && price > 0m ? $"Ganancia: {Money(price - cost)}" : "";

    private bool TryReadForm(out object command)
    {
        command = new { };
        if (string.IsNullOrWhiteSpace(CodeBox.Text) || string.IsNullOrWhiteSpace(DescriptionBox.Text)) { ShowResult("Revisa el producto", "Código y descripción son obligatorios.", OperationResultKind.Warning); return false; }
        if (!TryDecimal(CostBox.Text, out var cost) || !TryDecimal(PriceBox.Text, out var price)) { ShowResult("Revisa el producto", "Costo y precio de venta deben ser números válidos.", OperationResultKind.Warning); return false; }
        var profit = OptionalDecimal(ProfitPercentBox.Text); var wholesalePrice = OptionalDecimal(WholesalePriceBox.Text); var wholesaleProfit = OptionalDecimal(WholesaleProfitPercentBox.Text); var wholesaleMinimum = OptionalDecimal(WholesaleMinimumBox.Text); var initialStock = OptionalDecimal(InitialStockBox.Text); var minimum = OptionalDecimal(MinimumStockBox.Text); var maximum = OptionalDecimal(MaximumStockBox.Text);
        if (new[] { cost, price, profit, wholesalePrice, wholesaleProfit, wholesaleMinimum, initialStock, minimum, maximum }.Any(value => value < 0m)) { ShowResult("Revisa el producto", "Los importes, porcentajes y existencias no pueden ser negativos.", OperationResultKind.Warning); return false; }
        if (maximum > 0m && maximum < minimum) { ShowResult("Revisa el inventario", "El máximo de existencia debe ser igual o mayor al mínimo.", OperationResultKind.Warning); MaximumStockBox.Focus(); return false; }
        if (price <= 0m && profit <= 0m) { ShowResult("Revisa el producto", "Indica un precio de venta mayor que cero o una ganancia válida.", OperationResultKind.Warning); return false; }
        if (wholesalePrice > 0m && wholesaleMinimum <= 0m) { ShowResult("Revisa mayoreo", "El precio de mayoreo requiere un mínimo de artículos mayor que cero.", OperationResultKind.Warning); return false; }
        command = new { code = CodeBox.Text.Trim(), description = DescriptionBox.Text.Trim(), price, cost, profitPercent = profit, wholesalePrice, wholesaleProfitPercent = wholesaleProfit, wholesaleMinimumQuantity = wholesaleMinimum, isKit = IsKitBox.IsChecked == true, unitOfMeasure = UnitValue(), departmentId = DepartmentBox.SelectedValue is Guid department && department != Guid.Empty ? department : (Guid?)null, initialStock, minimumStock = minimum, maximumStock = maximum };
        return true;
    }

    private string UnitValue() => string.Equals(UnitBox.SelectedItem?.ToString(), "Granel (unidad configurada)", StringComparison.OrdinalIgnoreCase) ? _weightUnit : UnitBox.SelectedItem?.ToString() ?? "Pieza";
    private void ShowResult(string title, string message, OperationResultKind kind) => new OperationResultWindow(title, message, kind) { Owner = this }.ShowDialog();
    private static decimal OptionalDecimal(string value) => TryDecimal(value, out var result) ? result : 0m;
    private static decimal Parse(string value) => OptionalDecimal(value);
    private static bool TryDecimal(string? value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Percent(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Quantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public sealed record ProductEditModel(Guid Id, string Code, string Description, Guid? DepartmentId, string UnitOfMeasure, decimal Cost, decimal Price, decimal ProfitPercent, decimal WholesalePrice, decimal WholesaleProfitPercent, decimal WholesaleMinimumQuantity, decimal Stock, decimal MinimumStock, decimal MaximumStock, bool IsKit);
    public sealed record DepartmentOption(Guid? Id, string Name);
}
