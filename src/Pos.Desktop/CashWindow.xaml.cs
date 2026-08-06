using System.Globalization;
using System.Windows;

namespace Pos.Desktop;

public partial class CashWindow : Window
{
    private readonly decimal _total;
    public decimal? Received { get; private set; }
    public Guid? CustomerId { get; private set; }
    public bool CreditRequested { get; private set; }
    public CashWindow(decimal total)
    {
        InitializeComponent(); _total = total; TotalText.Text = $"Total: ${total:0.00}"; ReceivedTextBox.Text = total.ToString("0.00", CultureInfo.InvariantCulture); ReceivedTextBox.Focus(); ReceivedTextBox.SelectAll();
    }
    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(ReceivedTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var received) || received < _total) { MessageText.Text = "El efectivo recibido no es suficiente."; return; }
        Received = decimal.Round(received, 2); DialogResult = true;
    }

    private void OnCreditClick(object sender, RoutedEventArgs e)
    {
        var customers = new CustomerWindow(true) { Owner = this };
        if (customers.ShowDialog() != true || customers.SelectedCustomerId is null) return;
        CustomerId = customers.SelectedCustomerId; CreditRequested = true; Received = 0m; DialogResult = true;
    }
}
