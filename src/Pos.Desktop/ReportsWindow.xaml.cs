using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Pos.Desktop;

public partial class ReportsWindow : UserControl
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
        var today = DateTime.Today;
        await SetPeriodAsync(StartOfWeek(today), today);
    }

    private async void OnQuickPeriodClick(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        switch (tag)
        {
            case "week":
                await SetPeriodAsync(StartOfWeek(today), today);
                break;
            case "month":
                await SetPeriodAsync(new DateTime(today.Year, today.Month, 1), today);
                break;
            case "previousMonth":
                var monthStart = new DateTime(today.Year, today.Month, 1);
                await SetPeriodAsync(monthStart.AddMonths(-1), monthStart.AddDays(-1));
                break;
            case "year":
                await SetPeriodAsync(new DateTime(today.Year, 1, 1), today);
                break;
        }
    }

    private async void OnApplyCustomPeriodClick(object sender, RoutedEventArgs e)
    {
        if (FromDatePicker.SelectedDate is not DateTime fromDate || ToDatePicker.SelectedDate is not DateTime toDate)
        {
            MessageBox.Show("Selecciona las dos fechas del periodo.", "Reportes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (toDate.Date < fromDate.Date)
        {
            MessageBox.Show("La fecha final no puede ser anterior a la fecha inicial.", "Reportes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await SetPeriodAsync(fromDate, toDate);
    }

    private async Task SetPeriodAsync(DateTime fromDate, DateTime toDate)
    {
        _from = ToUtcStart(fromDate);
        _to = ToUtcStart(toDate.AddDays(1));
        FromDatePicker.SelectedDate = fromDate.Date;
        ToDatePicker.SelectedDate = toDate.Date;
        RangeText.Text = $"Periodo: {fromDate:dd/MM/yyyy} al {toDate:dd/MM/yyyy}";
        await LoadReportsAsync();
    }

    private async Task LoadReportsAsync()
    {
        try
        {
            var encodedFrom = Uri.EscapeDataString(_from.ToString("O"));
            var encodedTo = Uri.EscapeDataString(_to.ToString("O"));
            var dashboardTask = Client.GetFromJsonAsync<SalesDashboardResult>($"/api/reports/dashboard?from={encodedFrom}&to={encodedTo}");
            var salesTask = Client.GetFromJsonAsync<List<SalesRow>>($"/api/reports/sales?from={encodedFrom}&to={encodedTo}");
            var analysisTask = Client.GetFromJsonAsync<SalesAnalysisResult>("/api/reports/analysis");
            await Task.WhenAll(dashboardTask, salesTask, analysisTask);

            RenderDashboard(await dashboardTask ?? EmptyDashboard());
            SalesList.ItemsSource = await salesTask ?? [];
            RenderAnalysis(await analysisTask);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"No se pudo consultar el reporte: {exception.Message}", "Reportes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RenderDashboard(SalesDashboardResult dashboard)
    {
        TotalSalesText.Text = dashboard.TotalSales.ToString("C2", CultureInfo.CurrentCulture);
        SalesCountText.Text = $"{dashboard.SalesCount:N0} venta(s) confirmada(s)";
        AverageSaleText.Text = dashboard.AverageSale.ToString("C2", CultureInfo.CurrentCulture);
        ProfitText.Text = dashboard.EstimatedGrossProfit.ToString("C2", CultureInfo.CurrentCulture);
        MarginText.Text = $"{dashboard.MarginPercent:N2}%";

        var maxDaily = Math.Max(dashboard.DailySales.MaxBy(item => item.Total)?.Total ?? 0m, 1m);
        DailySalesChart.ItemsSource = dashboard.DailySales.Select(item => new DailySalesView(item, maxDaily)).ToList();
        DailyEmptyText.Visibility = dashboard.DailySales.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var maxPayment = Math.Max(dashboard.Payments.MaxBy(item => item.Total)?.Total ?? 0m, 1m);
        PaymentChart.ItemsSource = dashboard.Payments.Select(item => new PaymentView(item, maxPayment)).ToList();
        PaymentEmptyText.Visibility = dashboard.Payments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var maxDepartmentSales = Math.Max(dashboard.Departments.MaxBy(item => item.Total)?.Total ?? 0m, 1m);
        var maxDepartmentProfit = Math.Max(dashboard.Departments.Select(item => Math.Max(item.EstimatedProfit, 0m)).DefaultIfEmpty(0m).Max(), 1m);
        DepartmentSalesChart.ItemsSource = dashboard.Departments.Select(item => new DepartmentView(item, maxDepartmentSales, maxDepartmentProfit)).ToList();
        DepartmentProfitChart.ItemsSource = dashboard.Departments.Select(item => new DepartmentView(item, maxDepartmentSales, maxDepartmentProfit)).ToList();
        var noDepartments = dashboard.Departments.Count == 0;
        DepartmentEmptyText.Visibility = noDepartments ? Visibility.Visible : Visibility.Collapsed;
        ProfitDepartmentEmptyText.Visibility = noDepartments ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderAnalysis(SalesAnalysisResult? analysis)
    {
        if (analysis is null)
        {
            BestSellersList.ItemsSource = Array.Empty<object>();
            RestockList.ItemsSource = Array.Empty<object>();
            LowMovementList.ItemsSource = Array.Empty<object>();
            NoMovementList.ItemsSource = Array.Empty<object>();
            return;
        }

        var maxSold = Math.Max(analysis.BestSellers.MaxBy(item => item.QuantitySold)?.QuantitySold ?? 0m, 1m);
        BestSellersList.ItemsSource = analysis.BestSellers.Select(item => new ProductAnalysisView(item, maxSold)).ToList();
        RestockList.ItemsSource = analysis.RestockNeeded.Select(item => new ProductAnalysisView(item, maxSold)).ToList();
        LowMovementList.ItemsSource = analysis.LowMovement.Select(item => new ProductAnalysisView(item, maxSold)).ToList();
        NoMovementList.ItemsSource = analysis.NoMovement.Select(item => new ProductAnalysisView(item, maxSold)).ToList();
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"ventas-{_from:yyyyMMdd}-{_to:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
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

    private static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-((int)date.DayOfWeek + 6) % 7);
    private static DateTimeOffset ToUtcStart(DateTime date) => new DateTimeOffset(date.Date, TimeZoneInfo.Local.GetUtcOffset(date.Date)).ToUniversalTime();
    private static SalesDashboardResult EmptyDashboard() => new(0m, 0, 0m, 0m, 0m, [], [], []);

    private sealed record SalesRow(DateTimeOffset CreatedAtUtc, Guid SaleId, string Status, decimal Total, string PaymentMethod, Guid? CustomerId);
    private sealed record SalesAnalysisResult(List<PeriodSummaryRow> Periods, List<ProductAnalysisRow> BestSellers, List<ProductAnalysisRow> RestockNeeded, List<ProductAnalysisRow> LowMovement, List<ProductAnalysisRow> NoMovement);
    private sealed record PeriodSummaryRow(string Period, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int SalesCount, decimal Total);
    private sealed record ProductAnalysisRow(Guid ProductId, string Code, string Description, string Category, string UnitOfMeasure, decimal QuantitySold, decimal TotalSold, decimal Stock, decimal MinimumStock, decimal MaximumStock);
    private sealed record SalesDashboardResult(decimal TotalSales, int SalesCount, decimal AverageSale, decimal EstimatedGrossProfit, decimal MarginPercent, List<DailySalesDashboardRow> DailySales, List<PaymentDashboardRow> Payments, List<DepartmentDashboardRow> Departments);
    private sealed record DailySalesDashboardRow(DateTime Date, int SalesCount, decimal Total, decimal EstimatedProfit);
    private sealed record PaymentDashboardRow(string Method, decimal Total);
    private sealed record DepartmentDashboardRow(string Department, decimal Total, decimal EstimatedProfit);

    private sealed class DailySalesView(DailySalesDashboardRow row, decimal maximum)
    {
        public string Label => row.Date.ToString("ddd dd", CultureInfo.CurrentCulture);
        public string TotalText => row.Total.ToString("C2", CultureInfo.CurrentCulture);
        public string ProfitText => $"Util. {row.EstimatedProfit.ToString("C2", CultureInfo.CurrentCulture)}";
        public double Percentage => (double)(row.Total / maximum * 100m);
    }

    private sealed class PaymentView(PaymentDashboardRow row, decimal maximum)
    {
        public string Label => row.Method switch { "Cash" => "Efectivo", "Card" => "Tarjeta", "Transfer" => "Transferencia", "Credit" => "Crédito", _ => row.Method };
        public string TotalText => row.Total.ToString("C2", CultureInfo.CurrentCulture);
        public double Percentage => (double)(row.Total / maximum * 100m);
        public string Brush => row.Method switch { "Cash" => "#16834A", "Card" => "#156EA8", "Transfer" => "#2B8C7F", "Credit" => "#A96300", _ => "#7C5799" };
    }

    private sealed class DepartmentView(DepartmentDashboardRow row, decimal maxSales, decimal maxProfit)
    {
        public string Department => row.Department;
        public string TotalText => row.Total.ToString("C2", CultureInfo.CurrentCulture);
        public string ProfitText => row.EstimatedProfit.ToString("C2", CultureInfo.CurrentCulture);
        public double Percentage => (double)(row.Total / maxSales * 100m);
        public double ProfitPercentage => (double)(Math.Max(row.EstimatedProfit, 0m) / maxProfit * 100m);
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
