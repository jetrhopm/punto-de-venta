using System.Globalization;
using System.Windows;

namespace Pos.Desktop;

public partial class ShiftWindow : Window
{
    public decimal? InitialCash { get; private set; }

    public ShiftWindow()
    {
        InitializeComponent();
        InitialCashTextBox.Text = "0.00";
        InitialCashTextBox.Focus();
        InitialCashTextBox.SelectAll();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(InitialCashTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0m)
        {
            MessageText.Text = "Escribe un fondo inicial valido.";
            return;
        }

        InitialCash = decimal.Round(amount, 2);
        DialogResult = true;
    }
}
