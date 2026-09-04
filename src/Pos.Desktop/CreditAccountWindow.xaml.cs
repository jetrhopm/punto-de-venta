using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class CreditAccountWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private CustomerView _customer;

    public CreditAccountWindow(CustomerView customer)
    {
        InitializeComponent();
        _customer = customer;
        ApplyCustomer();
        Loaded += async (_, _) => await LoadStatementAsync();
    }

    private void ApplyCustomer()
    {
        CustomerNameText.Text = _customer.Name;
        BalanceText.Text = _customer.Balance.ToString("C2");
        CurrentLimitText.Text = _customer.CreditLimit.ToString("C2");
        AvailableText.Text = _customer.AvailableCredit.ToString("C2");
        CreditEnabledBox.IsChecked = _customer.CreditEnabled;
        CreditEnabledBox.IsEnabled = _customer.Balance <= 0m;
        CreditEnabledBox.ToolTip = CreditEnabledBox.IsEnabled ? "Permitir o bloquear nuevas ventas a crédito" : "No se puede deshabilitar el crédito mientras exista saldo pendiente.";
        CreditLimitBox.Text = _customer.CreditLimit.ToString("0.00", CultureInfo.InvariantCulture);
        PaymentButton.IsEnabled = _customer.CreditEnabled && _customer.Balance > 0m;
        PaymentButton.ToolTip = PaymentButton.IsEnabled ? "Registrar el efectivo recibido del cliente" : "Se requiere crédito activo y saldo pendiente para registrar un abono.";
    }

    private async Task LoadStatementAsync()
    {
        try
        {
            StatementGrid.ItemsSource = await Client.GetFromJsonAsync<List<CreditStatementView>>($"/api/customers/{_customer.Id}/statement") ?? [];
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private async void OnSaveCreditClick(object sender, RoutedEventArgs e)
    {
        if (!TryParse(CreditLimitBox.Text, out var limit))
        {
            StatusText.Text = "Indica un límite de crédito válido.";
            CreditLimitBox.Focus();
            return;
        }
        if (limit < _customer.Balance)
        {
            StatusText.Text = "El límite no puede ser menor que el saldo pendiente.";
            CreditLimitBox.Focus();
            return;
        }

        try
        {
            using var response = await Client.PutAsJsonAsync($"/api/customers/{_customer.Id}", new
            {
                name = _customer.Name,
                phone = _customer.Phone,
                email = _customer.Email,
                taxId = _customer.TaxId,
                creditLimit = limit,
                creditEnabled = CreditEnabledBox.IsChecked == true
            });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ReadErrorAsync(response);
                return;
            }
            var saved = await response.Content.ReadFromJsonAsync<CustomerView>();
            if (saved is not null) _customer = saved;
            ApplyCustomer();
            StatusText.Text = "Configuración de crédito guardada.";
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private async void OnApplyPaymentClick(object sender, RoutedEventArgs e)
    {
        if (!TryParse(PaymentAmountBox.Text, out var amount) || amount <= 0m)
        {
            StatusText.Text = "Indica un importe de abono mayor a cero.";
            PaymentAmountBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(PaymentReasonBox.Text))
        {
            StatusText.Text = "Escribe el motivo o referencia del abono.";
            PaymentReasonBox.Focus();
            return;
        }

        try
        {
            using var response = await Client.PostAsJsonAsync("/api/customers/credit-payments", new { operationId = Guid.NewGuid(), customerId = _customer.Id, amount, reason = PaymentReasonBox.Text.Trim() });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ReadErrorAsync(response);
                return;
            }
            var payment = await response.Content.ReadFromJsonAsync<CreditPaymentResult>();
            if (payment is not null) _customer = _customer with { Balance = payment.BalanceAfter };
            PaymentAmountBox.Clear();
            ApplyCustomer();
            await LoadStatementAsync();
            StatusText.Text = "Abono registrado correctamente.";
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private static bool TryParse(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? "No se pudo completar la operación." : content;
    }

    private sealed record CreditPaymentResult(Guid TransactionId, Guid CustomerId, decimal Amount, decimal BalanceBefore, decimal BalanceAfter);
}
