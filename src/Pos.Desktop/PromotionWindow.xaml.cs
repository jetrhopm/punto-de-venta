using System.Globalization;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class PromotionWindow : Window
{
    private ProductRow? _selectedProduct;
    private PromotionRow? _selectedPromotion;
    private CancellationTokenSource? _productSearchCancellation;
    private Button? _deactivateButton;
    private Button? _reactivateButton;
    private CheckBox? _showInactiveBox;

    public PromotionWindow()
    {
        InitializeComponent();
        ConfigureInactiveControls();
        Loaded += async (_, _) => await LoadPromotionsAsync();
        Closed += (_, _) => _productSearchCancellation?.Cancel();
    }

    private async void OnProductTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ProductBox.Text.Trim().Length < 2) { ProductList.Visibility = Visibility.Collapsed; return; }
        _productSearchCancellation?.Cancel();
        _productSearchCancellation = new CancellationTokenSource();
        var cancellationToken = _productSearchCancellation.Token;
        try
        {
            await Task.Delay(180, cancellationToken);
            var rows = await ApiClient.Client.GetFromJsonAsync<List<ProductRow>>($"/api/products/search?q={Uri.EscapeDataString(ProductBox.Text.Trim())}", cancellationToken) ?? [];
            ProductList.ItemsSource = rows;
            ProductList.DisplayMemberPath = nameof(ProductRow.Display);
            ProductList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch { ProductList.Visibility = Visibility.Collapsed; }
    }
    private void OnProductSelected(object sender, MouseButtonEventArgs e) { if (ProductList.SelectedItem is ProductRow row) { _selectedProduct = row; ProductBox.Text = row.Display; ProductList.Visibility = Visibility.Collapsed; } }
    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        // WPF can raise SelectionChanged while InitializeComponent is still creating the controls.
        if (ValueOneLabel is null || ValueTwoLabel is null || ValueOneBox is null || ValueTwoBox is null) return;
        var type = (TypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        ValueOneLabel.Text = type == "percent" ? "Descuento %" : type == "amount" ? "Descuento en pesos" : "Cantidad a comprar";
        ValueTwoLabel.Text = type == "buyPay" ? "Cantidad a pagar" : "Campo no utilizado";
        ValueOneBox.IsEnabled = true; ValueTwoBox.IsEnabled = type == "buyPay"; if (type != "buyPay") ValueTwoBox.Text = "0";
    }
    private async Task LoadPromotionsAsync()
    {
        try { PromotionsGrid.ItemsSource = await ApiClient.Client.GetFromJsonAsync<List<PromotionRow>>($"/api/promotions?includeInactive={(_showInactiveBox?.IsChecked == true).ToString().ToLowerInvariant()}") ?? []; StatusText.Text = "Las promociones se aplican durante el cobro y quedan auditadas."; }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar las promociones"); }
    }
    private void OnPromotionSelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedPromotion = PromotionsGrid.SelectedItem as PromotionRow;
        if (_deactivateButton is not null) _deactivateButton.IsEnabled = _selectedPromotion?.IsActive == true;
        if (_reactivateButton is not null) _reactivateButton.IsEnabled = _selectedPromotion?.IsActive == false;
    }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct is null || string.IsNullOrWhiteSpace(NameBox.Text)) { StatusText.Text = "Selecciona un producto y escribe un nombre único."; return; }
        var type = (TypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!TryDecimal(ValueOneBox.Text, out var first) || !TryDecimal(ValueTwoBox.Text, out var second)) { StatusText.Text = "Los valores de la promoción deben ser numéricos."; return; }
        decimal percent = type == "percent" ? first : 0m, amount = type == "amount" ? first : 0m, buy = type == "buyPay" ? first : 0m, pay = type == "buyPay" ? second : 0m;
        if (type == "percent" && (percent <= 0 || percent >= 100) || type == "amount" && amount <= 0 || type == "buyPay" && (buy <= 0 || pay <= 0 || pay >= buy)) { StatusText.Text = "Revisa los valores de la promoción."; return; }
        var body = new { productId = _selectedProduct.Id, name = NameBox.Text.Trim(), percent, discountAmount = amount, buyQuantity = buy, payQuantity = pay, startsAtUtc = ToUtc(StartDate.SelectedDate, false), endsAtUtc = ToUtc(EndDate.SelectedDate, true) };
        try { using var response = await ApiClient.Client.PostAsJsonAsync("/api/promotions", body); if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; } StatusText.Text = "Promoción guardada."; ClearForm(); await LoadPromotionsAsync(); }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar"); }
    }
    private async void OnDeactivateClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPromotion is null) { StatusText.Text = "Selecciona una promoción."; return; }
        if (MessageBox.Show($"¿Desactivar {_selectedPromotion.Name}?", "Desactivar promoción", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { using var response = await ApiClient.Client.DeleteAsync($"/api/promotions/{_selectedPromotion.Id}"); if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; } await LoadPromotionsAsync(); StatusText.Text = "Promoción desactivada."; }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo desactivar"); }
    }
    private async void OnReactivateClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPromotion is null) { StatusText.Text = "Selecciona una promoción inactiva."; return; }
        if (MessageBox.Show($"¿Reactivar {_selectedPromotion.Name}? Volverá a aplicarse durante el cobro cuando esté vigente.", "Reactivar promoción", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync($"/api/promotions/{_selectedPromotion.Id}/status", new { isActive = true });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo reactivar la promoción."); return; }
            await LoadPromotionsAsync(); StatusText.Text = "Promoción reactivada.";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo reactivar"); }
    }
    private void ConfigureInactiveControls()
    {
        PromotionsGrid.Columns.Add(new DataGridTextColumn { Header = "Estado", Binding = new Binding(nameof(PromotionRow.State)), Width = 95 });
        if (Content is not Grid root) return;
        var footer = root.Children.OfType<Grid>().FirstOrDefault(item => Grid.GetRow(item) == 3);
        if (footer is null) return;
        _deactivateButton = footer.Children.OfType<Button>().FirstOrDefault();
        if (_deactivateButton is not null)
        {
            footer.Children.Remove(_deactivateButton);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(_deactivateButton);
            _reactivateButton = new Button { Content = "Reactivar seleccionada", Style = FindResource("ConfirmButtonStyle") as Style, Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
            _reactivateButton.Click += OnReactivateClick;
            actions.Children.Add(_reactivateButton);
            footer.Children.Add(actions);
        }
        footer.Children.Remove(StatusText);
        var information = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _showInactiveBox = new CheckBox { Content = "Mostrar bajas", VerticalAlignment = VerticalAlignment.Center };
        _showInactiveBox.Checked += async (_, _) => await LoadPromotionsAsync();
        _showInactiveBox.Unchecked += async (_, _) => await LoadPromotionsAsync();
        information.Children.Add(_showInactiveBox);
        StatusText.Margin = new Thickness(14, 0, 0, 0);
        information.Children.Add(StatusText);
        footer.Children.Add(information);
    }
    private void ClearForm() { _selectedProduct = null; ProductBox.Clear(); NameBox.Clear(); StartDate.SelectedDate = null; EndDate.SelectedDate = null; TypeBox.SelectedIndex = 0; ValueOneBox.Text = "10"; ValueTwoBox.Text = "0"; }
    private static DateTimeOffset? ToUtc(DateTime? date, bool end) { if (date is null) return null; var local = DateTime.SpecifyKind(date.Value.Date.AddDays(end ? 1 : 0), DateTimeKind.Local); return new DateTimeOffset(local).ToUniversalTime(); }
    private static bool TryDecimal(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record ProductRow(Guid Id, string Code, string Description) { public string Display => $"{Code} | {Description}"; }
    private sealed record PromotionRow(Guid Id, Guid ProductId, string Name, decimal Percent, decimal DiscountAmount, decimal BuyQuantity, decimal PayQuantity, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc, bool IsActive)
    {
        public string State => IsActive ? "Activa" : "Inactiva";
    }
}
