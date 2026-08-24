using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class CashMovementWindow : Window
{
    public string Type { get; private set; } = "In";
    public decimal? Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? ProviderName { get; private set; }
    private CancellationTokenSource? _supplierCancellation;

    public CashMovementWindow(string? presetType = null)
    {
        InitializeComponent();
        Type = presetType == "Out" ? "Out" : "In";
        TypeText.Text = Type == "Out" ? "F8 · Salida de efectivo" : "F7 · Entrada de efectivo";
        if (Type == "In") ReasonTextBox.Text = "Entrada de dinero";
        SupplierPanel.Visibility = Type == "Out" ? Visibility.Visible : Visibility.Collapsed;
        ApiClient.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        Closed += (_, _) => _supplierCancellation?.Cancel();
        AmountTextBox.Focus();
    }

    private async void OnSupplierSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Type != "Out") return;
        _supplierCancellation?.Cancel();
        _supplierCancellation = new CancellationTokenSource();
        var token = _supplierCancellation.Token;
        var query = SupplierSearchTextBox.Text.Trim();
        if (query.Length == 0) { SupplierList.Visibility = Visibility.Collapsed; return; }
        try
        {
            await Task.Delay(180, token);
            var suppliers = await ApiClient.Client.GetFromJsonAsync<List<SupplierResult>>($"/api/suppliers?q={Uri.EscapeDataString(query)}", token) ?? [];
            SupplierList.ItemsSource = suppliers.Select(item => new SupplierRow(item)).ToList();
            SupplierList.Visibility = suppliers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private void OnSupplierSelected(object sender, MouseButtonEventArgs e)
    {
        if (SupplierList.SelectedItem is not SupplierRow row) return;
        ProviderName = row.Supplier.Name;
        ReasonTextBox.Text = $"Pago a proveedor: {ProviderName}";
        SupplierList.Visibility = Visibility.Collapsed;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountTextBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) && !decimal.TryParse(AmountTextBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) || amount <= 0m || string.IsNullOrWhiteSpace(ReasonTextBox.Text))
        {
            MessageText.Text = "Indica tipo, importe positivo y concepto.";
            return;
        }

        Amount = decimal.Round(amount, 2);
        Reason = ReasonTextBox.Text.Trim();
        DialogResult = true;
    }

    private sealed record SupplierResult(Guid Id, string Name, string? Phone, string? Email);
    private sealed record SupplierRow(SupplierResult Supplier) { public string DisplayText => Supplier.Name; }
}
