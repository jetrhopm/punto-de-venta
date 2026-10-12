using System.Net.Http.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Pos.Desktop;

public partial class InventoryMovementsWindow : Window
{
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _page = 1;
    private int _totalPages = 1;

    public InventoryMovementsWindow()
    {
        InitializeComponent();
        _filterTimer.Tick += async (_, _) => { _filterTimer.Stop(); _page = 1; await LoadAsync(); };
        Loaded += async (_, _) => await LoadAsync();
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (IsLoaded) { _filterTimer.Stop(); _filterTimer.Start(); } }
    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => ScheduleFilter();
    private void OnDateChanged(object? sender, SelectionChangedEventArgs e) => ScheduleFilter();
    private void ScheduleFilter()
    {
        if (!IsLoaded) return;
        _filterTimer.Stop();
        _filterTimer.Start();
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs e) { _page = 1; await LoadAsync(); }
    private async void OnPreviousPageClick(object sender, RoutedEventArgs e) { if (_page > 1) { _page--; await LoadAsync(); } }
    private async void OnNextPageClick(object sender, RoutedEventArgs e) { if (_page < _totalPages) { _page++; await LoadAsync(); } }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private async Task LoadAsync()
    {
        try
        {
            var parameters = new List<string> { $"q={Uri.EscapeDataString(SearchBox.Text.Trim())}", $"page={_page}" };
            var type = (TypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(type)) parameters.Add($"type={Uri.EscapeDataString(type)}");
            if (FromDatePicker.SelectedDate is DateTime fromDate) parameters.Add($"from={Uri.EscapeDataString(ToUtc(fromDate.Date).ToString("O", CultureInfo.InvariantCulture))}");
            if (ToDatePicker.SelectedDate is DateTime toDate) parameters.Add($"to={Uri.EscapeDataString(ToUtc(toDate.Date.AddDays(1)).ToString("O", CultureInfo.InvariantCulture))}");
            using var response = await ApiClient.Client.GetAsync($"api/inventory/movements?{string.Join("&", parameters)}");
            if (!response.IsSuccessStatusCode) { StatusText.Text = ConnectionHelp.ApiUnavailableRetry; return; }
            var result = await response.Content.ReadFromJsonAsync<MovementPage>();
            if (result is null) return;
            _totalPages = result.TotalPages; MovementsGrid.ItemsSource = result.Items;
            PageInfoText.Text = result.TotalCount == 0 ? "Sin movimientos." : $"Página {result.Page} de {result.TotalPages}. {result.TotalCount:N0} movimiento(s).";
            StatusText.Text = "Consulta actualizada.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron consultar los movimientos"); }
    }

    private static DateTimeOffset ToUtc(DateTime localDate) => new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate)).ToUniversalTime();

    private sealed class MovementPage { public List<MovementRow> Items { get; set; } = []; public int Page { get; set; } public int TotalPages { get; set; } public int TotalCount { get; set; } }
    private sealed class MovementRow { public string Code { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string MovementType { get; set; } = string.Empty; public decimal Quantity { get; set; } public decimal StockBefore { get; set; } public decimal StockAfter { get; set; } public string Reason { get; set; } = string.Empty; public string UserName { get; set; } = string.Empty; public DateTimeOffset CreatedAtUtc { get; set; } public string DateText => CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"); }
}
