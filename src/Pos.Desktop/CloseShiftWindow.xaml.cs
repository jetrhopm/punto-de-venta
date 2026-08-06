using System.Globalization;
using System.Windows;

namespace Pos.Desktop;

public partial class CloseShiftWindow : Window
{
    public decimal? CountedCash { get; private set; }

    public CloseShiftWindow()
    {
        InitializeComponent();
        CountedCashTextBox.Text = "0.00";
        CountedCashTextBox.Focus();
        CountedCashTextBox.SelectAll();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(CountedCashTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0m)
        {
            MessageText.Text = "Escribe el efectivo contado valido.";
            return;
        }

        CountedCash = decimal.Round(amount, 2);
        DialogResult = true;
    }
}
