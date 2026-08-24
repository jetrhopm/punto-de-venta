using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class CustomerWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private readonly bool _selectOnly;
    private CancellationTokenSource? _searchCancellation;
    private CustomerRow? _selected;
    public Guid? SelectedCustomerId { get; private set; }
    public string? SelectedCustomerName { get; private set; }

    public CustomerWindow(bool selectOnly = false, bool creditMode = false)
    {
        InitializeComponent(); _selectOnly = selectOnly;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        if (selectOnly) { TitleText.Text = "Seleccionar cliente para venta a crédito"; ManagementPanel.Visibility = Visibility.Collapsed; }
        else if (creditMode) TitleText.Text = "Créditos de clientes";
        Loaded += async (_, _) => await LoadCustomersAsync();
    }

    private async void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { _searchCancellation?.Cancel(); _searchCancellation = new CancellationTokenSource(); try { await Task.Delay(180, _searchCancellation.Token); await LoadCustomersAsync(_searchCancellation.Token); } catch (OperationCanceledException) { } }
    private async Task LoadCustomersAsync(CancellationToken cancellationToken = default)
    {
        try { var data = await Client.GetFromJsonAsync<List<CustomerResult>>($"/api/customers?q={Uri.EscapeDataString(SearchTextBox.Text.Trim())}", cancellationToken) ?? []; CustomersList.ItemsSource = data.Select(item => new CustomerRow(item)).ToList(); }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailable; }
    }
    private void OnCustomerSelected(object sender, MouseButtonEventArgs e)
    {
        if (CustomersList.SelectedItem is not CustomerRow row) return;
        _selected = row; SelectedCustomerId = row.Customer.Id; SelectedCustomerName = row.Customer.Name;
        if (_selectOnly) { DialogResult = true; return; }
        NameTextBox.Text = row.Customer.Name; LimitTextBox.Text = row.Customer.CreditLimit.ToString("0.00", CultureInfo.InvariantCulture); SelectedSummaryText.Text = $"Cliente seleccionado: {row.Customer.Name}. Saldo actual: ${row.Customer.Balance:0.00}."; MessageText.Text = string.Empty;
    }
    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text)) { MessageText.Text = "Escribe el nombre del cliente para crearlo."; return; }
        try
        {
            if (!TryParse(LimitTextBox.Text, out var limit) || limit < 0m) { MessageText.Text = "Indica un límite de crédito válido o deja 0.00."; return; }
            using var create = await Client.PostAsJsonAsync("/api/customers", new { name = NameTextBox.Text.Trim(), creditLimit = limit, creditEnabled = true });
            MessageText.Text = create.IsSuccessStatusCode ? "Cliente creado correctamente." : await create.Content.ReadAsStringAsync();
            if (create.IsSuccessStatusCode) { NameTextBox.Clear(); LimitTextBox.Text = "0.00"; await LoadCustomersAsync(); }
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailable; }
    }
    private async void OnPaymentClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { MessageText.Text = "Selecciona primero al cliente que realiza el abono."; return; }
        if (!TryParse(AmountTextBox.Text, out var amount) || amount <= 0m) { MessageText.Text = "Indica un importe de abono mayor a cero."; return; }
        try
        {
            using var payment = await Client.PostAsJsonAsync("/api/customers/credit-payments", new { operationId = Guid.NewGuid(), customerId = _selected.Customer.Id, amount, reason = "Abono registrado desde clientes" });
            MessageText.Text = payment.IsSuccessStatusCode ? "Abono registrado correctamente." : await payment.Content.ReadAsStringAsync();
            if (payment.IsSuccessStatusCode) { AmountTextBox.Clear(); await LoadCustomersAsync(); }
        }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailable; }
    }
    private static bool TryParse(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record CustomerResult(Guid Id, string Name, string? Phone, string? Email, string? TaxId, decimal CreditLimit, bool CreditEnabled, bool IsActive, decimal Balance);
    private sealed record CustomerRow(CustomerResult Customer) { public string DisplayText => $"{Customer.Name} | Limite ${Customer.CreditLimit:0.00} | Saldo ${Customer.Balance:0.00}"; }
}
