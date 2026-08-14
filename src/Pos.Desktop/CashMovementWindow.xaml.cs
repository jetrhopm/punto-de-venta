using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class CashMovementWindow : Window
{
    public string Type { get; private set; } = "In";
    public decimal? Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    public CashMovementWindow(string? presetType = null)
    {
        InitializeComponent();
        if (presetType == "Out") TypeComboBox.SelectedIndex = 1;
        else if (presetType == "In") TypeComboBox.SelectedIndex = 0;
        AmountTextBox.Focus();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (TypeComboBox.SelectedItem is not ComboBoxItem item || !decimal.TryParse(AmountTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0m || string.IsNullOrWhiteSpace(ReasonTextBox.Text))
        {
            MessageText.Text = "Indica tipo, importe positivo y concepto.";
            return;
        }

        Type = item.Tag?.ToString() ?? "In";
        Amount = decimal.Round(amount, 2);
        Reason = ReasonTextBox.Text.Trim();
        DialogResult = true;
    }
}
