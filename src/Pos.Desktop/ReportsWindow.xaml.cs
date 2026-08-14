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
        RangeText.Text = $"Ventas recientes desde {_from.ToLocalTime():dd/MM/yyyy} hasta {_to.AddDays(-1).ToLocalTime():dd/MM/yyyy}";

        try
        {
            var rows = await Client.GetFromJsonAsync<List<SalesRow>>($"/api/reports/sales?from={Uri.EscapeDataString(_from.ToString("O"))}&to={Uri.EscapeDataString(_to.ToString("O"))}") ?? [];
            SalesList.ItemsSource = rows;

            var analysis = await Client.GetFromJsonAsync<SalesAnalysisResult>("/api/reports/analysis");
            if (analysis is null) return;

            var maxPeriodTotal = Math.Max(analysis.Periods.Max(item => item.Total), 1m);
            PeriodSummaryList.ItemsSource = analysis.Periods.Select(item => new PeriodSummaryView(item, maxPeriodTotal)).ToList();

            var maxSold = Math.Max(analysis.BestSellers.MaxBy(item => item.QuantitySold)?.QuantitySold ?? 0m, 1m);
            BestSellersList.ItemsSource = analysis.BestSellers.Select(item => new ProductAnalysisView(item, maxSold)).ToList();
            RestockList.ItemsSource = analysis.RestockNeeded.Select(item => new ProductAnalysisView(item, maxSold)).ToList();
            LowMovementList.ItemsSource = analysis.LowMovement.Select(item => new ProductAnalysisView(item, maxSold)).ToList();
            NoMovementList.ItemsSource = analysis.NoMovement.Select(item => new ProductAnalysisView(item, maxSold)).ToList();
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
    private sealed record SalesAnalysisResult(List<PeriodSummaryRow> Periods, List<ProductAnalysisRow> BestSellers, List<ProductAnalysisRow> RestockNeeded, List<ProductAnalysisRow> LowMovement, List<ProductAnalysisRow> NoMovement);
    private sealed record PeriodSummaryRow(string Period, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int SalesCount, decimal Total);
    private sealed record ProductAnalysisRow(Guid ProductId, string Code, string Description, string Category, string UnitOfMeasure, decimal QuantitySold, decimal TotalSold, decimal Stock, decimal MinimumStock, decimal MaximumStock);

    private sealed class PeriodSummaryView(PeriodSummaryRow row, decimal maxTotal)
    {
        public string Period => row.Period;
        public string TotalText => row.Total.ToString("C2", CultureInfo.CurrentCulture);
        public string SalesText => $"{row.SalesCount:N0} venta(s)";
        public double Percentage => (double)(row.Total / maxTotal * 100m);
    }

    private sealed class ProductAnalysisView(ProductAnalysisRow row, decimal maxSold)
    {
        public string Code => row.Code;
        public string Description => row.Description;
        public string Category => string.IsNullOrWhiteSpace(row.Category) ? "Sin categoría" : row.Category;
        public string QuantitySoldText => $"{row.QuantitySold:0.###} {row.UnitOfMeasure}";
        public string TotalSoldText => row.TotalSold.ToString("C2", CultureInfo.CurrentCulture);
        public string StockText => $"{row.Stock:0.###}";
        public string MinimumStockText => $"{row.MinimumStock:0.###}";
        public double Percentage => (double)(row.QuantitySold / maxSold * 100m);
        public string Suggestion
        {
            get
            {
                var target = row.MaximumStock > row.MinimumStock ? row.MaximumStock : row.MinimumStock;
                var missing = Math.Max(target - row.Stock, 0m);
                return missing <= 0m ? "Revisar configuración de mínimos" : $"Surtir aprox. {missing:0.###} {row.UnitOfMeasure}";
            }
        }
        public string MovementNote => row.QuantitySold <= 0m ? "Sin ventas registradas en el último año" : "Venta baja; revisar precio, ubicación o surtido";
    }
}
