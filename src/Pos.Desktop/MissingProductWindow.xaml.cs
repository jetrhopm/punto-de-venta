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
    public string UnitOfMeasure { get; private set; } = "Pieza";

    public MissingProductWindow(string scannedCode)
    {
        InitializeComponent();
        _scannedCode = scannedCode.Trim();
        CodeText.Text = $"Codigo leido: {_scannedCode}";
        DescriptionBox.Text = "";
        PriceBox.Text = "0.00";
        Loaded += (_, _) =>
        {
            DescriptionBox.Focus();
            DescriptionBox.SelectAll();
        };
    }

    private void OnRegisterClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadValues()) return;
        ProductCode = _scannedCode;
        ProductDescription = string.IsNullOrWhiteSpace(ProductDescription) ? "Producto sin nombre" : ProductDescription;
        Decision = MissingProductDecision.RegisterProduct;
        DialogResult = true;
    }

    private void OnCommonClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadValues()) return;
        ProductCode = $"COMUN-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        ProductDescription = string.IsNullOrWhiteSpace(ProductDescription) ? $"Producto comun ({_scannedCode})" : ProductDescription;
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

        ProductDescription = DescriptionBox.Text.Trim();
        Price = decimal.Round(price, 2);
        UnitOfMeasure = (UnitBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Pieza";
        return true;
    }
}
