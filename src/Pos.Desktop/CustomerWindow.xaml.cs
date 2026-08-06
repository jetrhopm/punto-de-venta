using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class CustomerWindow : Window
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:5000") };
    private readonly bool _selectOnly;
    private CancellationTokenSource? _searchCancellation;
    private CustomerRow? _selected;
    public Guid? SelectedCustomerId { get; private set; }

    public CustomerWindow(bool selectOnly = false)
    {
        InitializeComponent(); _selectOnly = selectOnly;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        if (selectOnly) { TitleText.Text = "Seleccionar cliente para credito"; NameTextBox.Visibility = Visibility.Collapsed; LimitTextBox.Visibility = Visibility.Collapsed; AmountTextBox.Visibility = Visibility.Collapsed; ActionButton.Visibility = Visibility.Collapsed; }
        Loaded += async (_, _) => await LoadCustomersAsync();
    }

    private async void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { _searchCancellation?.Cancel(); _searchCancellation = new CancellationTokenSource(); try { await Task.Delay(180, _searchCancellation.Token); await LoadCustomersAsync(_searchCancellation.Token); } catch (OperationCanceledException) { } }
    private async Task LoadCustomersAsync(CancellationToken cancellationToken = default)
    {
        try { var data = await Client.GetFromJsonAsync<List<CustomerResult>>($"/api/customers?q={Uri.EscapeDataString(SearchTextBox.Text.Trim())}", cancellationToken) ?? []; CustomersList.ItemsSource = data.Select(item => new CustomerRow(item)).ToList(); }
        catch (HttpRequestException) { MessageText.Text = "No se pudo consultar clientes."; }
    }
    private void OnCustomerSelected(object sender, MouseButtonEventArgs e)
    {
        if (CustomersList.SelectedItem is not CustomerRow row) return;
        _selected = row; SelectedCustomerId = row.Customer.Id;
        if (_selectOnly) { DialogResult = true; return; }
        NameTextBox.Text = row.Customer.Name; LimitTextBox.Text = row.Customer.CreditLimit.ToString("0.00", CultureInfo.InvariantCulture); MessageText.Text = $"Saldo actual: ${row.Customer.Balance:0.00}";
    }
    private async void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null && string.IsNullOrWhiteSpace(NameTextBox.Text)) { MessageText.Text = "Selecciona un cliente o escribe un nombre."; return; }
        try
        {
            if (_selected is null)
            {
                if (!TryParse(LimitTextBox.Text, out var limit)) { MessageText.Text = "Indica un limite valido."; return; }
                using var create = await Client.PostAsJsonAsync("/api/customers", new { name = NameTextBox.Text.Trim(), creditLimit = limit, creditEnabled = true });
                MessageText.Text = create.IsSuccessStatusCode ? "Cliente creado." : await create.Content.ReadAsStringAsync();
                await LoadCustomersAsync(); return;
            }
            if (!TryParse(AmountTextBox.Text, out var amount) || amount <= 0m) { MessageText.Text = "Indica un abono positivo."; return; }
            using var payment = await Client.PostAsJsonAsync("/api/customers/credit-payments", new { operationId = Guid.NewGuid(), customerId = _selected.Customer.Id, amount, reason = "Abono registrado desde clientes" });
            MessageText.Text = payment.IsSuccessStatusCode ? "Abono registrado." : await payment.Content.ReadAsStringAsync(); await LoadCustomersAsync();
        }
        catch (HttpRequestException) { MessageText.Text = "No se pudo conectar con la API."; }
    }
    private static bool TryParse(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record CustomerResult(Guid Id, string Name, string? Phone, string? Email, string? TaxId, decimal CreditLimit, bool CreditEnabled, bool IsActive, decimal Balance);
    private sealed record CustomerRow(CustomerResult Customer) { public string DisplayText => $"{Customer.Name} | Limite ${Customer.CreditLimit:0.00} | Saldo ${Customer.Balance:0.00}"; }
}
