using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

/// <summary>Historial de compras a proveedores por cajero, con el detalle de productos y el efectivo retirado.</summary>
public partial class PurchaseHistoryWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private int _detailRevision;

    public PurchaseHistoryWindow()
    {
        InitializeComponent();
        var today = DateTime.Today;
        FromPicker.SelectedDate = today.AddDays(-30);
        ToPicker.SelectedDate = today;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadFiltersAsync();
        await SearchAsync();
    }

    private async Task LoadFiltersAsync()
    {
        try
        {
            var cashiers = await Client.GetFromJsonAsync<List<FilterOption>>("/api/purchases/cashiers") ?? [];
            CashierPicker.ItemsSource = new[] { new FilterOption(null, "Todos los cajeros") }.Concat(cashiers).ToList();
            CashierPicker.SelectedIndex = 0;
            var suppliers = await Client.GetFromJsonAsync<List<PurchaseSupplier>>("/api/suppliers") ?? [];
            SupplierPicker.ItemsSource = new[] { new FilterOption(null, "Todos los proveedores") }.Concat(suppliers.Select(item => new FilterOption(item.Id, item.Name))).ToList();
            SupplierPicker.SelectedIndex = 0;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { StatusText.Text = PurchaseWindow.OutdatedApiMessage; }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar los filtros"); }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        var fromDate = (FromPicker.SelectedDate ?? DateTime.Today).Date;
        var toDate = (ToPicker.SelectedDate ?? DateTime.Today).Date;
        if (toDate < fromDate) { StatusText.Text = "La fecha final no puede ser anterior a la inicial."; return; }
        // Rango del día local completo, enviado en UTC.
        var from = new DateTimeOffset(DateTime.SpecifyKind(fromDate, DateTimeKind.Local)).ToUniversalTime();
        var to = new DateTimeOffset(DateTime.SpecifyKind(toDate.AddDays(1), DateTimeKind.Local)).ToUniversalTime();
        var query = $"/api/purchases?from={Uri.EscapeDataString(from.ToString("o", CultureInfo.InvariantCulture))}&to={Uri.EscapeDataString(to.ToString("o", CultureInfo.InvariantCulture))}";
        if ((CashierPicker.SelectedItem as FilterOption)?.Id is Guid cashierId) query += $"&userId={cashierId}";
        if ((SupplierPicker.SelectedItem as FilterOption)?.Id is Guid supplierId) query += $"&supplierId={supplierId}";
        if (CashOnlyCheckBox.IsChecked == true) query += "&paidFromCash=true";
        try
        {
            StatusText.Text = "Consultando compras...";
            var rows = await Client.GetFromJsonAsync<List<PurchaseHistoryItem>>(query) ?? [];
            PurchasesGrid.ItemsSource = rows.Select(item => new PurchaseRow(item)).ToList();
            LinesGrid.ItemsSource = null;
            DetailTitleText.Text = "Selecciona una compra para ver sus productos.";
            var culture = CultureInfo.CurrentCulture;
            CountText.Text = rows.Count.ToString("N0", culture);
            TotalText.Text = rows.Sum(item => item.Total).ToString("C2", culture);
            WithdrawnText.Text = rows.Sum(item => item.CashWithdrawn ?? 0m).ToString("C2", culture);
            StatusText.Text = rows.Count >= 1000 ? "Se muestran las 1,000 compras más recientes; acota las fechas para ver el resto." : $"{rows.Count} compra(s) del {fromDate:dd/MM/yyyy} al {toDate:dd/MM/yyyy}.";
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { StatusText.Text = PurchaseWindow.OutdatedApiMessage; }
        catch (HttpRequestException exception) when (exception.StatusCode is not null) { StatusText.Text = $"La API respondió {(int)exception.StatusCode}: {exception.Message}"; }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo consultar el historial"); }
    }

    private async void OnPurchaseSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PurchasesGrid.SelectedItem is not PurchaseRow row) return;
        var revision = ++_detailRevision;
        try
        {
            var detail = await Client.GetFromJsonAsync<PurchaseHistoryDetail>($"/api/purchases/{row.Item.Id}");
            if (revision != _detailRevision || detail is null) return;
            LinesGrid.ItemsSource = detail.Lines.Select(item => new LineRow(item)).ToList();
            DetailTitleText.Text = $"Productos recibidos por {row.Item.Cashier} de {row.Item.Supplier} el {row.DateText}";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo consultar el detalle"); }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private sealed record FilterOption(Guid? Id, string Name);
    private sealed record PurchaseHistoryItem(Guid Id, DateTimeOffset CreatedAtUtc, Guid SupplierId, string Supplier, Guid UserId, string Cashier, int LineCount, decimal Pieces, decimal Total, bool PaidFromCashRegister, decimal? CashWithdrawn, string? Notes);
    private sealed record PurchaseHistoryLine(string Code, string Description, string UnitOfMeasure, decimal Quantity, decimal UnitCost, decimal LineTotal);
    private sealed record PurchaseHistoryDetail(PurchaseHistoryItem Purchase, List<PurchaseHistoryLine> Lines);

    private sealed record PurchaseRow(PurchaseHistoryItem Item)
    {
        private static CultureInfo Culture => CultureInfo.CurrentCulture;
        public string DateText => Item.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Culture);
        public string Cashier => Item.Cashier;
        public string Supplier => Item.Supplier;
        public int LineCount => Item.LineCount;
        public string PiecesText => Item.Pieces.ToString("0.###", Culture);
        public string TotalText => Item.Total.ToString("C2", Culture);
        public string PaymentText => Item.PaidFromCashRegister ? "Efectivo de caja" : Item.CashWithdrawn.HasValue ? "Caja ($0)" : "Fuera de caja";
        public string WithdrawnText => Item.CashWithdrawn is decimal amount ? amount.ToString("C2", Culture) : "—";
        public string DifferenceText => Item.CashWithdrawn is decimal amount && amount != Item.Total ? (amount - Item.Total).ToString("C2", Culture) : string.Empty;
        public string NotesText => Item.Notes ?? string.Empty;
    }

    private sealed record LineRow(PurchaseHistoryLine Line)
    {
        public string Code => Line.Code;
        public string Description => Line.Description;
        public string QuantityText => $"{Line.Quantity.ToString("0.###", CultureInfo.CurrentCulture)} {Line.UnitOfMeasure}";
        public string UnitCostText => Line.UnitCost.ToString("C2", CultureInfo.CurrentCulture);
        public string LineTotalText => Line.LineTotal.ToString("C2", CultureInfo.CurrentCulture);
    }
}
