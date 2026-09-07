using System.Globalization;
using System.Windows;

namespace Pos.Desktop;

public partial class SaleQuantityWindow : Window
{
    public decimal? Quantity { get; private set; }

    public SaleQuantityWindow(string description, string unitOfMeasure, decimal currentQuantity)
    {
        InitializeComponent();
        var unit = string.IsNullOrWhiteSpace(unitOfMeasure) ? "Pieza" : unitOfMeasure.Trim();
        ProductText.Text = description;
        QuantityBox.Text = currentQuantity.ToString("0.###", CultureInfo.CurrentCulture);
        UnitHintText.Text = IsFractionalUnit(unit)
            ? $"Unidad: {unit}. Puedes escribir decimales, por ejemplo 0.350."
            : $"Unidad: {unit}. Escribe la cantidad que debe quedar en esta partida.";
        Loaded += (_, _) =>
        {
            QuantityBox.Focus();
            QuantityBox.SelectAll();
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryParse(QuantityBox.Text, out var quantity) || quantity <= 0m)
        {
            MessageText.Text = "Escribe una cantidad mayor a cero.";
            return;
        }

        Quantity = decimal.Round(quantity, 3);
        DialogResult = true;
    }

    private static bool TryParse(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static bool IsFractionalUnit(string unit) => unit.ToUpperInvariant() is "KILOGRAMO" or "GRAMO" or "LIBRA" or "ONZA" or "LITRO" or "MILILITRO" or "METRO";
}
