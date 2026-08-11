using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using Microsoft.Win32;

namespace Pos.Desktop;

public partial class ReportsWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private DateTimeOffset _from;
    private DateTimeOffset _to;

    public ReportsWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        _from = DateTimeOffset.Now.Date.AddDays(-30).ToUniversalTime();
        _to = DateTimeOffset.Now.Date.AddDays(1).ToUniversalTime();
        RangeText.Text = $"Desde {_from.ToLocalTime():dd/MM/yyyy} hasta {_to.AddDays(-1).ToLocalTime():dd/MM/yyyy}";
        try
        {
            var rows = await Client.GetFromJsonAsync<List<SalesRow>>($"/api/reports/sales?from={Uri.EscapeDataString(_from.ToString("O"))}&to={Uri.EscapeDataString(_to.ToString("O"))}") ?? [];
            SalesList.ItemsSource = rows;
            CountText.Text = rows.Count.ToString(CultureInfo.CurrentCulture);
            TotalText.Text = rows.Sum(item => item.Total).ToString("C2", CultureInfo.CurrentCulture);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"No se pudo consultar el reporte: {exception.Message}", "Reportes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"ventas-{_from:yyyyMMdd}-{_to:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bytes = await Client.GetByteArrayAsync($"/api/reports/sales.csv?from={Uri.EscapeDataString(_from.ToString("O"))}&to={Uri.EscapeDataString(_to.ToString("O"))}");
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            MessageBox.Show("Reporte exportado correctamente.", "Reportes", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"No se pudo exportar el reporte: {exception.Message}", "Reportes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed record SalesRow(DateTimeOffset CreatedAtUtc, Guid SaleId, string Status, decimal Total, string PaymentMethod, Guid? CustomerId);
}
