using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class SalesHistoryWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private HistoryRow? _selected;
    private Detail? _detail;

    public SalesHistoryWindow() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        FromPicker.SelectedDate = DateTime.Today;
        ToPicker.SelectedDate = DateTime.Today;
        try
        {
            var cashiers = await Client.GetFromJsonAsync<List<Cashier>>("/api/sales/history/cashiers") ?? [];
            CashierCombo.ItemsSource = new[] { new Cashier(null, "Todos los cajeros") }.Concat(cashiers.Select(item => new Cashier(item.Id, item.DisplayName))).ToList();
            CashierCombo.SelectedIndex = 0;
            await SearchAsync();
        }
        catch (Exception exception) { MessageBox.Show($"No se pudo cargar el historial: {exception.Message}", "Historial", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        if (FromPicker.SelectedDate is not DateTime from || ToPicker.SelectedDate is not DateTime to) return;
        if (to < from) { MessageBox.Show("La fecha final no puede ser anterior a la fecha inicial.", "Historial", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var end = to.Date.AddDays(1).ToUniversalTime();
        var query = $"/api/sales/history?from={Uri.EscapeDataString(from.Date.ToUniversalTime().ToString("O"))}&to={Uri.EscapeDataString(end.ToString("O"))}";
        if (CashierCombo.SelectedItem is Cashier { Id: not null } cashier) query += $"&userId={cashier.Id}";
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text)) query += $"&q={Uri.EscapeDataString(SearchTextBox.Text.Trim())}";
        try
        {
            var rows = await Client.GetFromJsonAsync<List<HistoryRow>>(query) ?? [];
            SalesList.ItemsSource = rows;
            ResultText.Text = $"{rows.Count:N0} venta(s)";
            _selected = null; _detail = null; LinesList.ItemsSource = null; DetailText.Text = "Selecciona una venta para consultar sus partidas."; PaymentText.Text = string.Empty;
        }
        catch (Exception exception) { MessageBox.Show($"No se pudo consultar el historial: {exception.Message}", "Historial", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void OnSaleSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SalesList.SelectedItem is not HistoryRow row) return;
        _selected = row;
        try
        {
            _detail = await Client.GetFromJsonAsync<Detail>($"/api/sales/history/{row.SaleId}");
            if (_detail is null) return;
            LinesList.ItemsSource = _detail.Lines.Select(item => new LineView(item)).ToList();
            DetailText.Text = $"Folio: {row.ShortId}\nCajero: {_detail.Cashier}\nFecha: {_detail.CreatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}\nTotal: {_detail.Total:C2}\nEstado: {_detail.Status}";
            PaymentText.Text = "Pago: " + string.Join(" | ", _detail.Payments.Select(item => $"{item.Method} {item.Amount:C2}"));
        }
        catch (Exception exception) { DetailText.Text = $"No se pudo cargar el detalle: {exception.Message}"; }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !SessionContext.HasPermission("CancelSales")) { MessageBox.Show("No tienes permiso para cancelar ventas o no has seleccionado una venta.", "Historial", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var window = new CancelSaleWindow { Owner = this };
        if (window.ShowDialog() != true) return;
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/sales/cancel", new { operationId = Guid.NewGuid(), saleId = _selected.SaleId, reason = window.Reason });
            if (!response.IsSuccessStatusCode) { MessageBox.Show(await ReadMessage(response), "No se pudo cancelar", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            await SearchAsync();
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "No se pudo cancelar", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OnReturnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !SessionContext.HasPermission("ProcessReturns")) { MessageBox.Show("No tienes permiso para procesar devoluciones o no has seleccionado una venta.", "Historial", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        new ReturnSaleWindow(_selected.SaleId) { Owner = this }.ShowDialog();
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            using var response = await Client.GetAsync($"/api/sales/{_selected.SaleId}/ticket.pdf");
            if (!response.IsSuccessStatusCode) { MessageBox.Show("No se pudo generar la copia del ticket.", "Historial", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var dialog = new SaveFileDialog { Title = "Guardar copia del ticket", Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"Copia-{_selected.ShortId}.pdf" };
            if (dialog.ShowDialog(this) == true) await File.WriteAllBytesAsync(dialog.FileName, await response.Content.ReadAsByteArrayAsync());
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "No se pudo imprimir", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static async Task<string> ReadMessage(HttpResponseMessage response) => (await response.Content.ReadAsStringAsync()).Replace("{\"message\":\"", string.Empty).TrimEnd('}', '"');
    private sealed record Cashier(Guid? Id, string DisplayName);
    private sealed record HistoryRow(Guid SaleId, DateTimeOffset CreatedAtUtc, string Status, decimal Total, string PaymentMethod, decimal Paid, string Cashier, Guid UserId, int Items)
    { public string ShortId => SaleId.ToString("N")[..8].ToUpperInvariant(); public string DateText => CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"); public string TotalText => Total.ToString("C2", CultureInfo.CurrentCulture); }
    private sealed record Detail(Guid SaleId, DateTimeOffset CreatedAtUtc, string Status, decimal Total, string Cashier, List<HistoryLine> Lines, List<Payment> Payments);
    private sealed record HistoryLine(Guid ProductId, string Code, string Description, decimal Quantity, decimal UnitPrice, decimal Total);
    private sealed record Payment(string Method, decimal Amount, decimal Received, decimal Change);
    private sealed class LineView(HistoryLine line) { public decimal Quantity => line.Quantity; public string Description => line.Description; public string TotalText => line.Total.ToString("C2", CultureInfo.CurrentCulture); }
}
