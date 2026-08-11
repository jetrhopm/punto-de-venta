using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class PromotionWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private ProductRow? _selected;
    public PromotionWindow() { InitializeComponent(); EndDate.SelectedDate = DateTime.Today.AddDays(30); }
    private async void OnProductTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ProductBox.Text.Trim().Length < 2) { ProductList.Visibility = Visibility.Collapsed; return; }
        try { var rows = await Client.GetFromJsonAsync<List<ProductRow>>($"/api/products/search?q={Uri.EscapeDataString(ProductBox.Text.Trim())}") ?? []; ProductList.ItemsSource = rows; ProductList.DisplayMemberPath = nameof(ProductRow.Display); ProductList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible; } catch { ProductList.Visibility = Visibility.Collapsed; }
    }
    private void OnProductSelected(object sender, MouseButtonEventArgs e) { if (ProductList.SelectedItem is ProductRow row) { _selected = row; ProductBox.Text = row.Display; ProductList.Visibility = Visibility.Collapsed; } }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !decimal.TryParse(PercentBox.Text, out var percent) || EndDate.SelectedDate is null) { StatusText.Text = "Selecciona producto, porcentaje y fecha final validos."; return; }
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        var body = new { productId = _selected.Id, name = $"Promocion {percent:0.##}%", percent, startsAtUtc = DateTimeOffset.UtcNow, endsAtUtc = new DateTimeOffset(EndDate.SelectedDate.Value.AddDays(1), TimeSpan.Zero) };
        try { var response = await Client.PostAsJsonAsync("/api/promotions", body); response.EnsureSuccessStatusCode(); StatusText.Text = "Promocion guardada correctamente."; } catch (Exception exception) { StatusText.Text = $"No se pudo guardar: {exception.Message}"; }
    }
    private sealed record ProductRow(Guid Id, string Code, string Description) { public string Display => $"{Code} | {Description}"; }
}
