using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class CashWindow : Window
{
    private readonly decimal _total;
    private bool _controlsReady;
    private string _paymentMethod = "Cash";
    public decimal? Received { get; private set; }
    public decimal CardAmount { get; private set; }
    public decimal TransferAmount { get; private set; }
    public string PaymentMethod => _paymentMethod;
    public Guid? CustomerId { get; private set; }
    public bool CreditRequested { get; private set; }

    public CashWindow(decimal total)
    {
        InitializeComponent();
        _total = decimal.Round(total, 2);
        _controlsReady = true;
        TotalText.Text = $"Total: ${_total:0.00}";
        ReceivedTextBox.Text = _total.ToString("0.00", CultureInfo.InvariantCulture);
        ReceivedTextBox.Focus();
        ReceivedTextBox.SelectAll();
        UpdatePaymentView();
    }

    private void OnPaymentMethodClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string method }) { _paymentMethod = method; UpdatePaymentView(); } }

    private void UpdatePaymentView()
    {
        MethodText.Text = _paymentMethod switch { "Card" => "Pago completo con tarjeta", "Transfer" => "Pago completo por transferencia", "Mixed" => "Distribuye el total entre efectivo, tarjeta y transferencia", _ => "Pago completo en efectivo" };
        CashPanel.Visibility = _paymentMethod is "Cash" or "Mixed" ? Visibility.Visible : Visibility.Collapsed;
        MixedPanel.Visibility = _paymentMethod == "Mixed" ? Visibility.Visible : Visibility.Collapsed;
        UpdateAmounts();
    }

    private void OnAmountChanged(object sender, TextChangedEventArgs e) => UpdateAmounts();

    private void UpdateAmounts()
    {
        if (!_controlsReady) return;
        var received = ParseAmount(ReceivedTextBox.Text); var card = _paymentMethod == "Card" ? _total : ParseAmount(CardAmountTextBox.Text); var transfer = _paymentMethod == "Transfer" ? _total : ParseAmount(TransferAmountTextBox.Text);
        var cashDue = _paymentMethod switch { "Card" or "Transfer" => 0m, "Mixed" => _total - card - transfer, _ => _total };
        if (_paymentMethod == "Mixed") MixedCashDueText.Text = cashDue >= 0m ? $"Efectivo requerido: ${cashDue:0.00}" : "Los importes superan el total de la venta.";
        var change = _paymentMethod is "Cash" or "Mixed" ? received - cashDue : 0m;
        ChangeText.Text = change >= 0m ? $"${change:0.00}" : "Falta efectivo";
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var received = ParseAmount(ReceivedTextBox.Text); var card = _paymentMethod == "Card" ? _total : ParseAmount(CardAmountTextBox.Text); var transfer = _paymentMethod == "Transfer" ? _total : ParseAmount(TransferAmountTextBox.Text);
        var cashDue = _paymentMethod switch { "Card" or "Transfer" => 0m, "Mixed" => _total - card - transfer, _ => _total };
        if (cashDue < 0m || card < 0m || transfer < 0m || received < cashDue || decimal.Round(cashDue + card + transfer, 2) != _total) { MessageText.Text = "Verifica que los importes cubran exactamente el total y que el efectivo recibido sea suficiente."; return; }
        Received = decimal.Round(received, 2); CardAmount = decimal.Round(card, 2); TransferAmount = decimal.Round(transfer, 2); DialogResult = true;
    }

    private void OnCreditClick(object sender, RoutedEventArgs e)
    {
        var customers = new CustomerWindow(true) { Owner = this }; if (customers.ShowDialog() != true || customers.SelectedCustomerId is null) return;
        CustomerId = customers.SelectedCustomerId; CreditRequested = true; _paymentMethod = "Credit"; Received = 0m; DialogResult = true;
    }

    private static decimal ParseAmount(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var amount) ? decimal.Round(amount, 2) : -1m;
}
