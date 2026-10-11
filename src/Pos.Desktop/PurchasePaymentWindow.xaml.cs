using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pos.Desktop;

/// <summary>
/// Pregunta si la compra se paga con efectivo de caja y cuánto se retiró realmente.
/// El monto retirado puede diferir del total (pagó otra persona, regalo del proveedor) y no se bloquea.
/// </summary>
public partial class PurchasePaymentWindow : Window
{
    private readonly decimal _total;

    public PurchasePaymentWindow(IReadOnlyList<PurchaseSupplier> suppliers, PurchaseSupplier selected, decimal total, int lineCount, decimal pieces)
    {
        InitializeComponent();
        _total = total;
        TotalText.Text = total.ToString("C2", CultureInfo.CurrentCulture);
        LinesText.Text = $"{lineCount} producto(s)\n{pieces:0.###} pieza(s)";
        WithdrawnBox.Text = total.ToString("0.00", CultureInfo.CurrentCulture);
        List<PurchaseSupplier> items = suppliers.Count > 0 ? suppliers.ToList() : [PurchaseSupplier.None];
        SupplierPicker.ItemsSource = items;
        SupplierPicker.SelectedItem = items.FirstOrDefault(item => item.Id == selected.Id) ?? items[0];
        // Por defecto se paga con caja: es el caso más común al recibir mercancía.
        CashRadio.IsChecked = true;
        Loaded += (_, _) =>
        {
            WithdrawnBox.Focus();
            WithdrawnBox.SelectAll();
        };
    }

    public PurchaseSupplier SelectedSupplier => SupplierPicker.SelectedItem as PurchaseSupplier ?? PurchaseSupplier.None;
    public bool PayFromCashRegister { get; private set; }
    public decimal? CashWithdrawn { get; private set; }
    public string? Notes { get; private set; }

    private void OnPaymentModeChanged(object sender, RoutedEventArgs e)
    {
        if (CashPanel is null) return;
        CashPanel.Visibility = CashRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateSupplierHint();
        UpdateDifference();
    }

    private void OnSupplierChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateSupplierHint();

    private void UpdateSupplierHint()
    {
        if (CashDisabledText is null || CashRadio is null || SupplierPicker is null) return;
        // Pagar sin proveedor está permitido; sólo se sugiere identificarlo para el control del corte.
        CashDisabledText.Visibility = CashRadio.IsChecked == true && SelectedSupplier.Id is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWithdrawnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateDifference();

    private void UpdateDifference()
    {
        if (DifferenceText is null) return;
        if (CashRadio.IsChecked != true) { DifferenceText.Text = string.Empty; return; }
        if (!PurchasePricing.TryParse(WithdrawnBox.Text, out var withdrawn) || withdrawn < 0m)
        {
            DifferenceText.Foreground = Brushes.DarkRed;
            DifferenceText.Text = "Escribe una cantidad válida (puede ser 0).";
            return;
        }
        withdrawn = decimal.Round(withdrawn, 2, MidpointRounding.AwayFromZero);
        var difference = withdrawn - _total;
        var culture = CultureInfo.CurrentCulture;
        if (difference == 0m)
        {
            DifferenceText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x83, 0x4A));
            DifferenceText.Text = "Coincide con el total. Se registrará como salida de efectivo para el corte.";
        }
        else
        {
            DifferenceText.Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0x63, 0x00));
            DifferenceText.Text = difference < 0m
                ? string.Create(culture, $"Retiras {(-difference):C2} menos que el total. Sólo {withdrawn:C2} saldrán de caja en el corte. Explica la diferencia en la nota.")
                : string.Create(culture, $"Retiras {difference:C2} más que el total. Saldrán {withdrawn:C2} de caja en el corte. Explica la diferencia en la nota.");
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        if (CashRadio.IsChecked == true)
        {
            if (!PurchasePricing.TryParse(WithdrawnBox.Text, out var withdrawn) || withdrawn < 0m)
            {
                MessageText.Text = "Escribe cuánto efectivo retiraste de caja (puede ser 0).";
                WithdrawnBox.Focus();
                return;
            }
            PayFromCashRegister = true;
            CashWithdrawn = decimal.Round(withdrawn, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            PayFromCashRegister = false;
            CashWithdrawn = null;
        }
        Notes = notes;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
