using System.Globalization;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public sealed record ProductEditorModel(Guid Id, string Code, string Description, Guid? DepartmentId, string UnitOfMeasure, decimal Cost, decimal ProfitPercent, decimal Price, decimal WholesalePrice, decimal WholesaleProfitPercent, decimal WholesaleMinimumQuantity, decimal Stock, decimal MinimumStock, decimal MaximumStock, bool IsKit);

public partial class ProductEditorWindow : Window
{
    private readonly ProductEditorModel? _existing;
    public event EventHandler? Saved;

    public ProductEditorWindow(ProductEditorModel? existing = null)
    {
        _existing = existing;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            DepartmentBox.ItemsSource = await ApiClient.Client.GetFromJsonAsync<List<DepartmentRow>>("/api/departments") ?? [];
            if (_existing is null) { CodeBox.Focus(); return; }
            Title = "Editar producto"; TitleText.Text = "Editar producto"; SubtitleText.Text = "Actualiza datos comerciales y límites de inventario.";
            CodeBox.Text = _existing.Code; DescriptionBox.Text = _existing.Description; DepartmentBox.SelectedValue = _existing.DepartmentId;
            UnitBox.SelectedItem = UnitBox.Items.OfType<System.Windows.Controls.ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Content?.ToString(), _existing.UnitOfMeasure, StringComparison.OrdinalIgnoreCase)) ?? UnitBox.Items[0];
            CostBox.Text = Number(_existing.Cost); ProfitBox.Text = Number(_existing.ProfitPercent); PriceBox.Text = Number(_existing.Price); WholesalePriceBox.Text = Number(_existing.WholesalePrice); WholesaleProfitBox.Text = Number(_existing.WholesaleProfitPercent); WholesaleMinimumBox.Text = Number(_existing.WholesaleMinimumQuantity); MinimumStockBox.Text = Number(_existing.MinimumStock); MaximumStockBox.Text = Number(_existing.MaximumStock); IsKitBox.IsChecked = _existing.IsKit;
            InitialStockBox.Text = Number(_existing.Stock); InitialStockBox.IsEnabled = false; InventoryHintText.Text = $"Existencia actual: {_existing.Stock:0.###}. Para cambiarla usa Inventario > Ajustar inventario; aquí sólo se modifican límites.";
        }
        catch (Exception exception) { MessageText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar los departamentos"); }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryRead(out var command)) return;
        try
        {
            using var response = _existing is null
                ? await ApiClient.Client.PostAsJsonAsync("/api/products", command)
                : await ApiClient.Client.PutAsJsonAsync($"/api/products/{_existing.Id}", command);
            if (!response.IsSuccessStatusCode) { MessageText.Text = "No se pudo guardar. Revisa que el código sea único y que mínimos, máximos e importes sean válidos."; return; }
            Saved?.Invoke(this, EventArgs.Empty);
            DialogResult = true;
        }
        catch (Exception exception) { MessageText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar el producto"); }
    }

    private bool TryRead(out object command)
    {
        command = new { };
        if (string.IsNullOrWhiteSpace(CodeBox.Text) || string.IsNullOrWhiteSpace(DescriptionBox.Text) || !Decimal(CostBox.Text, out var cost) || !Decimal(ProfitBox.Text, out var profit) || !Decimal(PriceBox.Text, out var price) || !Decimal(WholesalePriceBox.Text, out var wholesalePrice) || !Decimal(WholesaleProfitBox.Text, out var wholesaleProfit) || !Decimal(WholesaleMinimumBox.Text, out var wholesaleMinimum) || !Decimal(InitialStockBox.Text, out var initialStock) || !Decimal(MinimumStockBox.Text, out var minimumStock) || !Decimal(MaximumStockBox.Text, out var maximumStock)) { MessageText.Text = "Completa código, descripción y valores numéricos válidos."; return false; }
        if (cost < 0 || price < 0 || profit < 0 || wholesalePrice < 0 || wholesaleProfit < 0 || wholesaleMinimum < 0 || initialStock < 0 || minimumStock < 0 || maximumStock < 0 || maximumStock > 0m && maximumStock < minimumStock) { MessageText.Text = "Los valores no pueden ser negativos y el máximo no puede ser menor al mínimo."; return false; }
        var departmentId = DepartmentBox.SelectedValue is Guid department ? department : (Guid?)null;
        var unit = (UnitBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Pieza";
        command = new { code = CodeBox.Text.Trim(), description = DescriptionBox.Text.Trim(), price, cost, profitPercent = profit, wholesalePrice, wholesaleProfitPercent = wholesaleProfit, wholesaleMinimumQuantity = wholesaleMinimum, isKit = IsKitBox.IsChecked == true, unitOfMeasure = unit, departmentId, initialStock, minimumStock, maximumStock };
        return true;
    }

    private static bool Decimal(string value, out decimal number) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out number) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number);
    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private sealed record DepartmentRow(Guid Id, string Name, bool IsActive);
}
