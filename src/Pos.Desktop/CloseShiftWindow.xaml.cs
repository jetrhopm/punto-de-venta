using System.Globalization;
using System.Windows;

namespace Pos.Desktop;

public partial class CloseShiftWindow : Window
{
    private readonly decimal _expectedCash;
    public decimal? CountedCash { get; private set; }

    public CloseShiftWindow(decimal expectedCash)
    {
        InitializeComponent();
        _expectedCash = decimal.Round(expectedCash, 2);
        ExpectedCashText.Text = _expectedCash.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
        CountedCashTextBox.Text = "0.00";
        CountedCashTextBox.TextChanged += OnCountedCashChanged;
        CountedCashTextBox.Focus();
        CountedCashTextBox.SelectAll();
        UpdateDifference();
    }

    private void OnCountedCashChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateDifference();

    private void UpdateDifference()
    {
        if (!TryParseAmount(CountedCashTextBox.Text, out var counted))
        {
            DifferenceText.Text = "Escribe el efectivo contado";
            return;
        }

        DifferenceText.Text = (counted - _expectedCash).ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseAmount(CountedCashTextBox.Text, out var amount) || amount < 0m)
        {
            MessageText.Text = "Escribe el efectivo contado valido.";
            return;
        }

        CountedCash = decimal.Round(amount, 2);
        DialogResult = true;
    }

    private static bool TryParseAmount(string? text, out decimal amount)
    {
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out amount)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}
