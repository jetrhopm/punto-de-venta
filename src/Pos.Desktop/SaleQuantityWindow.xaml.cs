using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class SaleQuantityWindow : Window
{
    public decimal? Quantity { get; private set; }
    private readonly string _unitOfMeasure;

    public SaleQuantityWindow(string description, string unitOfMeasure, decimal currentQuantity, bool allowScale = false)
    {
        InitializeComponent();
        _unitOfMeasure = unitOfMeasure;
        ProductText.Text = description;
        UnitText.Text = $"Unidad de venta: {unitOfMeasure}. Puedes usar decimales cuando corresponda.";
        QuantityBox.Text = currentQuantity.ToString("0.###", CultureInfo.CurrentCulture);
        ReadScaleButton.Visibility = allowScale && ApiClient.Scale.Enabled ? Visibility.Visible : Visibility.Collapsed;
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

    private async void OnReadScaleClick(object sender, RoutedEventArgs e)
    {
        ReadScaleButton.IsEnabled = false;
        try
        {
            var reading = await Task.Run(() => ScaleReadingService.Read(ApiClient.Scale));
            var quantity = ScaleReadingService.ConvertWeight(reading.Weight, ApiClient.Scale.Unit, _unitOfMeasure);
            QuantityBox.Text = decimal.Round(quantity, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.CurrentCulture);
            QuantityBox.SelectAll();
            MessageText.Foreground = System.Windows.Media.Brushes.ForestGreen;
            MessageText.Text = $"Peso recibido: {reading.Weight:0.###} {ApiClient.Scale.Unit.ToLowerInvariant()}.";
        }
        catch (Exception exception)
        {
            MessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
            MessageText.Text = $"No se pudo tomar el peso: {exception.Message}";
        }
        finally { ReadScaleButton.IsEnabled = true; }
    }
}
