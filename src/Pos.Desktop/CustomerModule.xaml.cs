using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class CustomerModule : UserControl
{
    private static HttpClient Client => ApiClient.Client;
    private readonly bool _creditMode;
    private CancellationTokenSource? _searchCancellation;
    private CustomerRow? _selected;

    public CustomerModule(bool creditMode = false)
    {
        InitializeComponent();
        _creditMode = creditMode;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        if (creditMode)
        {
            TitleText.Text = "Créditos de clientes";
            SubtitleText.Text = "Consulta saldos y registra abonos de los clientes con crédito.";
        }
        Loaded += async (_, _) => await LoadCustomersAsync();
    }

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        try { await Task.Delay(180, _searchCancellation.Token); await LoadCustomersAsync(_searchCancellation.Token); }
        catch (OperationCanceledException) { }
    }

    private async Task LoadCustomersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await Client.GetFromJsonAsync<List<CustomerResult>>($"/api/customers?q={Uri.EscapeDataString(SearchTextBox.Text.Trim())}", cancellationToken) ?? [];
            CustomersList.ItemsSource = data.Select(item => new CustomerRow(item)).ToList();
        }
        catch (HttpRequestException) { SelectedSummaryText.Text = "No se pudieron consultar los clientes."; }
    }

    private void OnCustomerSelected(object sender, MouseButtonEventArgs e)
    {
        if (CustomersList.SelectedItem is not CustomerRow row) return;
        _selected = row;
        NameTextBox.Text = row.Customer.Name;
        LimitTextBox.Text = row.Customer.CreditLimit.ToString("0.00", CultureInfo.InvariantCulture);
        SelectedSummaryText.Text = $"Cliente seleccionado: {row.Customer.Name}. Saldo actual: ${row.Customer.Balance:0.00}.";
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text)) { SelectedSummaryText.Text = "Escribe el nombre del cliente para crearlo."; return; }
        if (!TryParse(LimitTextBox.Text, out var limit) || limit < 0m) { SelectedSummaryText.Text = "Indica un límite de crédito válido o deja 0.00."; return; }
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/customers", new { name = NameTextBox.Text.Trim(), creditLimit = limit, creditEnabled = true });
            SelectedSummaryText.Text = response.IsSuccessStatusCode ? "Cliente creado correctamente." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) { NameTextBox.Clear(); LimitTextBox.Text = "0.00"; await LoadCustomersAsync(); }
        }
        catch (HttpRequestException) { SelectedSummaryText.Text = "No se pudo conectar con la API."; }
    }

    private async void OnPaymentClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { SelectedSummaryText.Text = "Selecciona primero al cliente que realiza el abono."; return; }
        if (!TryParse(AmountTextBox.Text, out var amount) || amount <= 0m) { SelectedSummaryText.Text = "Indica un importe de abono mayor a cero."; return; }
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/customers/credit-payments", new { operationId = Guid.NewGuid(), customerId = _selected.Customer.Id, amount, reason = "Abono registrado desde clientes" });
            SelectedSummaryText.Text = response.IsSuccessStatusCode ? "Abono registrado correctamente." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) { AmountTextBox.Clear(); await LoadCustomersAsync(); }
        }
        catch (HttpRequestException) { SelectedSummaryText.Text = "No se pudo conectar con la API."; }
    }

    private static bool TryParse(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record CustomerResult(Guid Id, string Name, string? Phone, string? Email, string? TaxId, decimal CreditLimit, bool CreditEnabled, bool IsActive, decimal Balance);
    private sealed record CustomerRow(CustomerResult Customer) { public string DisplayText => $"{Customer.Name} | Límite ${Customer.CreditLimit:0.00} | Saldo ${Customer.Balance:0.00}"; }
}
