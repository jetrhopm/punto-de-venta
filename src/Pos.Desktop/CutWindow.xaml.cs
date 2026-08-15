using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class CutWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    public bool RequestCloseShift { get; private set; }

    public CutWindow()
    {
        InitializeComponent();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnMakeCutClick(object sender, RoutedEventArgs e) { RequestCloseShift = true; DialogResult = true; }

    private async Task LoadAsync()
    {
        try
        {
            StatusText.Text = "Consultando el corte del turno...";
            var result = await Client.GetFromJsonAsync<CutResult>("/api/shifts/cut");
            if (result is null) { StatusText.Text = "No hay un turno abierto para consultar."; return; }
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
            StatusText.Text = "Corte actualizado.";
        }
        catch (HttpRequestException) { StatusText.Text = "No se pudo consultar el corte. Revisa la conexión con JetVenta."; }
        catch (Exception exception) { StatusText.Text = $"No se pudo consultar el corte: {exception.Message}"; }
    }

    private sealed record CutResult(decimal InitialCash, decimal TotalSales, int SalesCount, decimal CashSales, decimal CardSales, decimal TransferSales, decimal CreditSales, decimal CashIn, decimal CashOut, decimal CashReturns, decimal Profit, decimal ExpectedCash);
}
