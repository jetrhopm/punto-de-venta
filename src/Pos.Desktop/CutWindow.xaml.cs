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

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await (_cashierMode ? LoadDayAsync(true) : LoadDayAsync(false));
    private async void OnCashierCutClick(object sender, RoutedEventArgs e) => await LoadDayAsync(true);
    private async void OnDayCutClick(object sender, RoutedEventArgs e) => await LoadDayAsync(false);
    private async void OnDateChanged(object sender, RoutedEventArgs e) { if (IsLoaded) { await LoadCashiersAsync(); await LoadDayAsync(_cashierMode); } }
    private async void OnCashierChanged(object sender, RoutedEventArgs e) { if (IsLoaded && _cashierMode) await LoadDayAsync(true); }
    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnMakeCutClick(object sender, RoutedEventArgs e) { RequestCloseShift = true; CloseShiftRequested?.Invoke(this, EventArgs.Empty); }

    private async Task LoadCashiersAsync()
    {
        if (CutDatePicker.SelectedDate is not DateTime date) return;
        try
        {
            var options = await Client.GetFromJsonAsync<List<CashierOption>>($"/api/shifts/cut/cashiers?date={date:yyyy-MM-dd}") ?? [];
            CashierPicker.ItemsSource = new[] { new CashierOption(null, "Todos los cajeros") }.Concat(options).ToList();
            CashierPicker.SelectedIndex = 0;
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo consultar la lista de cajeros."; }
    }

    private async Task LoadDayAsync(bool cashierMode)
    {
        try
        {
            _cashierMode = cashierMode;
            if (CutDatePicker.SelectedDate is not DateTime date) return;
            StatusText.Text = cashierMode ? "Consultando el corte del cajero..." : "Consultando el corte consolidado del día...";
            var cashier = (CashierPicker.SelectedItem as CashierOption)?.Id;
            var suffix = cashierMode && cashier.HasValue ? $"&cashierId={cashier.Value}" : string.Empty;
            var result = await Client.GetFromJsonAsync<CutResult>($"/api/shifts/cut/day?date={date:yyyy-MM-dd}{suffix}");
            if (result is null) { StatusText.Text = cashierMode ? "No hay turnos de ese cajero en la fecha seleccionada." : "No hay turnos registrados en la fecha seleccionada."; return; }
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
            StatusText.Text = cashierMode ? $"Corte de cajero actualizado para {date:dd/MM/yyyy}." : $"Corte consolidado actualizado para {date:dd/MM/yyyy}.";
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo consultar el corte. Revisa la conexión con JetVenta."; }
        catch (Exception exception) { StatusText.Text = $"No se pudo consultar el corte: {exception.Message}"; }
    }

    private sealed record CashierOption(Guid? Id, string Name);
    private sealed record CutResult(decimal InitialCash, decimal TotalSales, int SalesCount, decimal CashSales, decimal CardSales, decimal TransferSales, decimal CreditSales, decimal CashIn, decimal CashOut, decimal CashReturns, decimal Profit, decimal ExpectedCash);
}
