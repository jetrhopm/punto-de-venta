using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class CommonProductWindow : Window
{
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductDescription { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; }
    public string UnitOfMeasure { get; private set; } = "Pieza";

    public CommonProductWindow(string? suggestedCode = null)
    {
        InitializeComponent();
        CodeBox.Text = suggestedCode?.Trim() ?? string.Empty;
        Loaded += (_, _) => DescriptionBox.Focus();
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DescriptionBox.Text)) { ShowError("Escribe una descripción para el artículo.", DescriptionBox); return; }
        if (!TryDecimal(PriceBox.Text, out var price) || price < 0m) { ShowError("Escribe un precio válido.", PriceBox); return; }
        if (!TryDecimal(QuantityBox.Text, out var quantity) || quantity <= 0m) { ShowError("Escribe una cantidad mayor que cero.", QuantityBox); return; }
        ProductCode = CodeBox.Text.Trim();
        ProductDescription = DescriptionBox.Text.Trim();
        Price = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        Quantity = decimal.Round(quantity, 3, MidpointRounding.AwayFromZero);
        var selected = UnitBox.SelectedItem as ComboBoxItem;
        UnitOfMeasure = selected?.Tag?.ToString() ?? selected?.Content?.ToString() ?? "Pieza";
        DialogResult = true;
    }

    private static bool TryDecimal(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private void ShowError(string message, Control control) { MessageText.Text = message; control.Focus(); }
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } }
}
