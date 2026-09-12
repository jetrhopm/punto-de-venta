using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Pos.Desktop;

public partial class SalesHistoryWindow : UserControl
{
    private static HttpClient Client => ApiClient.Client;
    private readonly bool _allowSaleActions;
    private readonly DispatcherTimer _filterDelay;
    private HistoryRow? _selected;
    private Detail? _detail;
    private bool _filtersReady;
    private int _searchGeneration;

    public SalesHistoryWindow(bool allowSaleActions = true)
    {
        InitializeComponent();
        _allowSaleActions = allowSaleActions;
        _filterDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _filterDelay.Tick += OnFilterDelayElapsed;
        Unloaded += (_, _) => _filterDelay.Stop();
        if (!allowSaleActions)
        {
            ReturnButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
        }
    }

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
            _filtersReady = true;
            await SearchAsync();
        }
        catch (Exception exception) { MessageBox.Show(ConnectionHelp.FromException(exception, "No se pudo cargar el historial"), "Historial", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await SearchAsync();

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_filtersReady) return;
        _filterDelay.Stop();
        _filterDelay.Start();
    }

    private async void OnFilterDelayElapsed(object? sender, EventArgs e)
    {
        _filterDelay.Stop();
        await SearchAsync();
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _filterDelay.Stop();
        await SearchAsync();
        e.Handled = true;
    }

    private async Task SearchAsync()
    {
        if (FromPicker.SelectedDate is not DateTime from || ToPicker.SelectedDate is not DateTime to) return;
        if (to < from) { MessageBox.Show("La fecha final no puede ser anterior a la fecha inicial.", "Historial", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var generation = ++_searchGeneration;
        ResultText.Text = "Buscando...";
        var end = to.Date.AddDays(1).ToUniversalTime();
        var query = $"/api/sales/history?from={Uri.EscapeDataString(from.Date.ToUniversalTime().ToString("O"))}&to={Uri.EscapeDataString(end.ToString("O"))}";
        if (CashierCombo.SelectedItem is Cashier { Id: not null } cashier) query += $"&userId={cashier.Id}";
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text)) query += $"&q={Uri.EscapeDataString(SearchTextBox.Text.Trim())}";
        try
        {
            var rows = await Client.GetFromJsonAsync<List<HistoryRow>>(query) ?? [];
            if (generation != _searchGeneration || !IsLoaded) return;
            SalesList.ItemsSource = rows;
            ResultText.Text = $"{rows.Count:N0} venta(s)";
            _selected = null; _detail = null; LinesList.ItemsSource = null; DetailText.Text = "Selecciona una venta para consultar sus partidas."; PaymentText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            if (generation != _searchGeneration || !IsLoaded) return;
            MessageBox.Show(ConnectionHelp.FromException(exception, "No se pudo consultar el historial"), "Historial", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            DetailText.Text = $"Folio: {row.FolioText}\nCajero: {_detail.Cashier}\nFecha: {_detail.CreatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}\nTotal: {_detail.Total:C2}\nEstado: {LocalizeSaleStatus(_detail.Status)}";
            PaymentText.Text = "Pago: " + string.Join(" | ", _detail.Payments.Select(item => $"{item.Method} {item.Amount:C2}"));
        }
        catch (Exception exception) { DetailText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar el detalle"); }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { MessageBox.Show("Selecciona una venta antes de cancelarla.", "Historial", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        await using var authorization = await PermissionAuthorization.RequestAsync(owner, "CancelSales", "Cancelar una venta confirmada requiere autorización.");
        if (authorization is null) return;
        var window = new CancelSaleWindow { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true) return;
        try
        {
            using var response = await Client.PostAsJsonAsync("/api/sales/cancel", new { operationId = Guid.NewGuid(), saleId = _selected.SaleId, reason = window.Reason });
            if (!response.IsSuccessStatusCode) { MessageBox.Show(await ReadMessage(response), "No se pudo cancelar", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            await SearchAsync();
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "No se pudo cancelar", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void OnReturnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { MessageBox.Show("Selecciona una venta antes de procesar la devolución.", "Historial", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        await using var authorization = await PermissionAuthorization.RequestAsync(owner, "ProcessReturns", "Procesar una devolución requiere autorización.");
        if (authorization is null) return;
        new ReturnSaleWindow(_selected.SaleId) { Owner = owner }.ShowDialog();
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        await using var authorization = await PermissionAuthorization.RequestAsync(owner, "ReprintTickets", "Reimprimir un ticket requiere autorización.");
        if (authorization is null) return;
        try
        {
            using var response = await Client.GetAsync($"/api/sales/{_selected.SaleId}/ticket.pdf");
            if (!response.IsSuccessStatusCode) { MessageBox.Show("No se pudo generar la copia del ticket.", "Historial", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var dialog = new SaveFileDialog { Title = "Guardar copia del ticket", Filter = "Documento PDF (*.pdf)|*.pdf", FileName = $"Copia-{_selected.FolioText}.pdf" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) await File.WriteAllBytesAsync(dialog.FileName, await response.Content.ReadAsByteArrayAsync());
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "No se pudo imprimir", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static async Task<string> ReadMessage(HttpResponseMessage response) => (await response.Content.ReadAsStringAsync()).Replace("{\"message\":\"", string.Empty).TrimEnd('}', '"');
    private sealed record Cashier(Guid? Id, string DisplayName);
    private static string LocalizeSaleStatus(string status) => status switch
    {
        "Completed" => "Completada",
        "Cancelled" => "Cancelada",
        "Returned" => "Devuelta",
        "PartiallyReturned" => "Devolución parcial",
        _ => status
    };
    private sealed record HistoryRow(Guid SaleId, long Folio, DateTimeOffset CreatedAtUtc, string Status, decimal Total, string PaymentMethod, decimal Paid, string Cashier, Guid UserId, int Items)
    { public string ShortId => SaleId.ToString("N")[..8].ToUpperInvariant(); public string FolioText => Folio > 0 ? Folio.ToString("N0", CultureInfo.CurrentCulture) : "N/D"; public string DateText => CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"); public string TotalText => Total.ToString("C2", CultureInfo.CurrentCulture); public string StatusText => LocalizeSaleStatus(Status); }
    private sealed record Detail(Guid SaleId, long Folio, DateTimeOffset CreatedAtUtc, string Status, decimal Total, string Cashier, List<HistoryLine> Lines, List<Payment> Payments);
    private sealed record HistoryLine(Guid ProductId, string Code, string Description, decimal Quantity, decimal ReturnedQuantity, decimal UnitPrice, decimal Total);
    private sealed record Payment(string Method, decimal Amount, decimal Received, decimal Change);
    private sealed class LineView(HistoryLine line)
    {
        public decimal Quantity => line.Quantity;
        public string Description => line.Description;
        public string ReturnText => line.ReturnedQuantity <= 0m ? "Entregado" : line.ReturnedQuantity >= line.Quantity ? "Devuelto" : $"Devuelto {line.ReturnedQuantity:0.###}";
        public string TotalText => line.Total.ToString("C2", CultureInfo.CurrentCulture);
    }
}
