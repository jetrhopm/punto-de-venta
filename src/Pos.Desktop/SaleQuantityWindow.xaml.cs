using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class SaleQuantityWindow : Window
{
    public decimal? Quantity { get; private set; }

    public SaleQuantityWindow(string description, string unitOfMeasure, decimal currentQuantity)
    {
        InitializeComponent();
        ProductText.Text = description;
        UnitText.Text = $"Unidad de venta: {unitOfMeasure}. Puedes usar decimales cuando corresponda.";
        QuantityBox.Text = currentQuantity.ToString("0.###", CultureInfo.CurrentCulture);
        Loaded += (_, _) => { QuantityBox.Focus(); QuantityBox.SelectAll(); };
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(QuantityBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var quantity) || quantity <= 0m)
        {
            MessageText.Text = "Escribe una cantidad mayor que cero.";
            QuantityBox.Focus();
            return;
        }
        Quantity = decimal.Round(quantity, 3, MidpointRounding.AwayFromZero);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } }
}
