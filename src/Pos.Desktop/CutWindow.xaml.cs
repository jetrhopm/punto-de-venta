using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class CutWindow : UserControl
{
    private static HttpClient Client => ApiClient.Client;
    private bool _cashierMode;
    public bool RequestCloseShift { get; private set; }
    public event EventHandler? CloseRequested;
    public event EventHandler? CloseShiftRequested;

    public CutWindow()
    {
        InitializeComponent();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        CutDatePicker.SelectedDate = DateTime.Today;
        await LoadCashiersAsync();
        await LoadDayAsync(false);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAndExplainAsync(_cashierMode, "Corte actualizado");
    private async void OnCashierCutClick(object sender, RoutedEventArgs e) => await LoadAndExplainAsync(true, "Corte de cajero actualizado");
    private async void OnDayCutClick(object sender, RoutedEventArgs e) => await LoadAndExplainAsync(false, "Corte del día actualizado");
    private async void OnDateChanged(object sender, RoutedEventArgs e) { if (IsLoaded) { await LoadCashiersAsync(); await LoadDayAsync(_cashierMode); } }
    private async void OnCashierChanged(object sender, RoutedEventArgs e) { if (IsLoaded && _cashierMode) await LoadDayAsync(true); }
    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnMakeCutClick(object sender, RoutedEventArgs e) { RequestCloseShift = true; CloseShiftRequested?.Invoke(this, EventArgs.Empty); }

    private async Task LoadAndExplainAsync(bool cashierMode, string title)
    {
        var result = await LoadDayAsync(cashierMode);
        var owner = Window.GetWindow(this);
        if (owner is not null) OperationFeedback.Show(owner, title, result.Message, result.Kind);
    }

    private async Task LoadCashiersAsync()
    {
        if (CutDatePicker.SelectedDate is not DateTime date) return;
        try
        {
            var options = await Client.GetFromJsonAsync<List<CashierOption>>($"/api/shifts/cut/cashiers?date={date:yyyy-MM-dd}") ?? [];
            CashierPicker.ItemsSource = new[] { new CashierOption(null, "Todos los cajeros") }.Concat(options).ToList();
            CashierPicker.SelectedIndex = 0;
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private async Task<CutLoadResult> LoadDayAsync(bool cashierMode)
    {
        try
        {
            _cashierMode = cashierMode;
            if (CutDatePicker.SelectedDate is not DateTime date)
            {
                const string dateMessage = "Selecciona una fecha para consultar el corte.";
                StatusText.Text = dateMessage;
                return new CutLoadResult(dateMessage, OperationResultKind.Warning);
            }
            StatusText.Text = cashierMode ? "Consultando el corte del cajero..." : "Consultando el corte consolidado del día...";
            var cashier = (CashierPicker.SelectedItem as CashierOption)?.Id;
            var suffix = cashierMode && cashier.HasValue ? $"&cashierId={cashier.Value}" : string.Empty;
            var result = await Client.GetFromJsonAsync<CutResult>($"/api/shifts/cut/day?date={date:yyyy-MM-dd}{suffix}");
            if (result is null)
            {
                var emptyMessage = cashierMode ? "No hay turnos del cajero seleccionado en la fecha indicada." : "No hay turnos registrados en la fecha seleccionada.";
                StatusText.Text = emptyMessage;
                return new CutLoadResult(emptyMessage, OperationResultKind.Information);
            }
            var culture = CultureInfo.GetCultureInfo("es-MX");
            TotalSalesText.Text = result.TotalSales.ToString("C2", culture);
            SalesCountText.Text = $"{result.SalesCount:N0} venta(s)";
            ExpectedCashText.Text = result.ExpectedCash.ToString("C2", culture);
            ProfitText.Text = result.Profit.ToString("C2", culture);
            InitialCashText.Text = $"Fondo de caja: {result.InitialCash.ToString("C2", culture)}";
            CashSalesInBoxText.Text = $"Ventas en efectivo: +{result.CashSales.ToString("C2", culture)}";
            CashSalesText.Text = $"Efectivo: {result.CashSales.ToString("C2", culture)}";
            CardSalesText.Text = $"Tarjeta: {result.CardSales.ToString("C2", culture)}";
            TransferSalesText.Text = $"Transferencia: {result.TransferSales.ToString("C2", culture)}";
            CreditSalesText.Text = $"Crédito: {result.CreditSales.ToString("C2", culture)}";
            CashInText.Text = $"Entradas de efectivo: +{result.CashIn.ToString("C2", culture)}";
            CashOutText.Text = $"Salidas de efectivo: -{result.CashOut.ToString("C2", culture)}";
            CashReturnsText.Text = $"Devoluciones en efectivo: -{result.CashReturns.ToString("C2", culture)}";
            ExpectedCashDetailText.Text = result.ExpectedCash.ToString("C2", culture);
            ProfitDetailText.Text = result.Profit.ToString("C2", culture);
            var message = cashierMode
                ? $"Se actualizó la consulta del cajero para {date:dd/MM/yyyy}. No se cerró ningún turno ni se registró movimiento de efectivo."
                : $"Se actualizó el corte consolidado para {date:dd/MM/yyyy}. No se cerró ningún turno ni se registró movimiento de efectivo.";
            StatusText.Text = message;
            return new CutLoadResult(message, OperationResultKind.Information);
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
            return new CutLoadResult(StatusText.Text, OperationResultKind.Error);
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo consultar el corte");
            return new CutLoadResult(StatusText.Text, OperationResultKind.Error);
        }
    }

    private sealed record CashierOption(Guid? Id, string Name);
    private sealed record CutResult(decimal InitialCash, decimal TotalSales, int SalesCount, decimal CashSales, decimal CardSales, decimal TransferSales, decimal CreditSales, decimal CashIn, decimal CashOut, decimal CashReturns, decimal Profit, decimal ExpectedCash);
    private sealed record CutLoadResult(string Message, OperationResultKind Kind);
}
