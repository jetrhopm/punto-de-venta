using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public enum MissingProductDecision
{
    Cancel,
    RegisterProduct,
    CommonProduct
}

public partial class MissingProductWindow : Window
{
    private readonly string _scannedCode;
    public MissingProductDecision Decision { get; private set; } = MissingProductDecision.Cancel;
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductDescription { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; } = 1m;
    public string UnitOfMeasure { get; private set; } = "Pieza";

    public MissingProductWindow(string scannedCode)
    {
        InitializeComponent();
        _scannedCode = scannedCode.Trim();
        CodeText.Text = $"Código leído: {_scannedCode}";
        CodeBox.Text = _scannedCode;
        DescriptionBox.Text = "Producto común";
        PriceBox.Text = "0.00";
        QuantityBox.Text = "1";
        Loaded += (_, _) =>
        {
            DescriptionBox.Focus();
            DescriptionBox.SelectAll();
        };
    }

    private void OnRegisterClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadValues()) return;
        ProductCode = CodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ProductCode))
        {
            MessageText.Text = "Para registrar el producto en inventario, escribe o confirma su código.";
            CodeBox.Focus();
            return;
        }
        ProductDescription = string.IsNullOrWhiteSpace(ProductDescription) ? "Producto sin nombre" : ProductDescription;
        Decision = MissingProductDecision.RegisterProduct;
        DialogResult = true;
    }

    private void OnCommonClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadValues()) return;
        if (string.IsNullOrWhiteSpace(ProductDescription))
        {
            MessageText.Text = "Escribe una descripción para el producto común.";
            DescriptionBox.Focus();
            return;
        }
        ProductCode = CodeBox.Text.Trim();
        Decision = MissingProductDecision.CommonProduct;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Decision = MissingProductDecision.Cancel;
        DialogResult = false;
    }

    private bool TryReadValues()
    {
        if (!decimal.TryParse(PriceBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var price) &&
            !decimal.TryParse(PriceBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out price))
        {
            MessageText.Text = "Escribe un precio valido.";
            return false;
        }

        if (price < 0m)
        {
            MessageText.Text = "El precio no puede ser negativo.";
            return false;
        }

        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var quantity) &&
            !decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity) || quantity <= 0m)
        {
            MessageText.Text = "Escribe una cantidad mayor que cero.";
            QuantityBox.Focus();
            QuantityBox.SelectAll();
            return false;
        }

        ProductDescription = DescriptionBox.Text.Trim();
        Price = decimal.Round(price, 2);
        Quantity = decimal.Round(quantity, 3);
        var selectedUnit = UnitBox.SelectedItem as ComboBoxItem;
        UnitOfMeasure = selectedUnit?.Tag?.ToString() ?? selectedUnit?.Content?.ToString() ?? "Pieza";
        return true;
    }

    private void OnSelectAll(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }

    private void OnSelectAllOnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not TextBox box) return;
        box.Focus();
        box.SelectAll();
        e.Handled = true;
    }
}
