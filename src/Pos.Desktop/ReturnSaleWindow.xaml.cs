using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class ReturnSaleWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private readonly Guid _saleId;
    private LineRow? _selected;
    public ReturnSaleWindow(Guid saleId)
    {
        InitializeComponent(); _saleId = saleId; Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken); Loaded += async (_, _) => await LoadLinesAsync();
    }
    private async Task LoadLinesAsync()
    {
        try { var lines = await Client.GetFromJsonAsync<List<ReturnLine>>($"/api/sales/{_saleId}/return-lines") ?? []; LinesList.ItemsSource = lines.Where(item => item.SoldQuantity > item.ReturnedQuantity).Select(item => new LineRow(item)).ToList(); }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailable; }
    }
    private void OnLineSelected(object sender, MouseButtonEventArgs e) { if (LinesList.SelectedItem is LineRow row) { _selected = row; SelectedText.Text = $"{row.Line.Description} | Disponible para devolver: {row.Line.SoldQuantity - row.Line.ReturnedQuantity:0.###}"; QuantityTextBox.Text = "1"; ReasonTextBox.Focus(); } }
    private async void OnReturnClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !TryParse(QuantityTextBox.Text, out var quantity) || quantity <= 0m || string.IsNullOrWhiteSpace(ReasonTextBox.Text)) { MessageText.Text = "Selecciona un producto, indica una cantidad positiva y escribe el motivo."; return; }
        try { using var response = await Client.PostAsJsonAsync("/api/sales/return", new { operationId = Guid.NewGuid(), saleId = _saleId, lines = new[] { new { productId = _selected.Line.ProductId, quantity } }, reason = ReasonTextBox.Text.Trim() }); MessageText.Text = response.IsSuccessStatusCode ? "Devolucion registrada." : await response.Content.ReadAsStringAsync(); if (response.IsSuccessStatusCode) await LoadLinesAsync(); }
        catch (HttpRequestException) { MessageText.Text = ConnectionHelp.ApiUnavailable; }
    }
    private static bool TryParse(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record ReturnLine(Guid ProductId, string Description, decimal SoldQuantity, decimal ReturnedQuantity, decimal UnitPrice);
    private sealed record LineRow(ReturnLine Line) { public string DisplayText => $"{Line.Description} | Vendido {Line.SoldQuantity:0.###} | Devuelto {Line.ReturnedQuantity:0.###} | ${Line.UnitPrice:0.00}"; }
}
