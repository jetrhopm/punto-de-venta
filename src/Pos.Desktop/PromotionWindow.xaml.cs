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
    private Button? _editButton;
    private Button? _showInactiveButton;
    private bool _includeInactive;
    private Guid? _editingPromotionId;

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
        try
        {
            PromotionsGrid.ItemsSource = await ApiClient.Client.GetFromJsonAsync<List<PromotionRow>>($"/api/promotions?includeInactive={_includeInactive.ToString().ToLowerInvariant()}") ?? [];
            StatusText.Text = _includeInactive ? "Mostrando promociones activas y dadas de baja." : "Las promociones se aplican durante el cobro y quedan auditadas.";
        }
        catch (Exception exception)
        {
            var message = ConnectionHelp.FromException(exception, "No se pudieron cargar las promociones");
            StatusText.Text = message;
            ShowResult("Promociones no disponibles", message, OperationResultKind.Error);
        }
    }
    private void OnPromotionSelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedPromotion = PromotionsGrid.SelectedItem as PromotionRow;
        if (_editButton is not null) _editButton.IsEnabled = _selectedPromotion is not null;
        if (_deactivateButton is not null) _deactivateButton.IsEnabled = _selectedPromotion?.IsActive == true;
        if (_reactivateButton is not null) _reactivateButton.IsEnabled = _selectedPromotion?.IsActive == false;
    }

    private void OnPromotionDoubleClick(object sender, MouseButtonEventArgs e) => BeginEditSelected();

    private void OnEditClick(object sender, RoutedEventArgs e) => BeginEditSelected();

    private void BeginEditSelected()
    {
        if (_selectedPromotion is null)
        {
            ShowResult("Selecciona una promoción", "Selecciona una promoción de la lista para editarla.", OperationResultKind.Information);
            return;
        }

        var promotion = _selectedPromotion;
        _editingPromotionId = promotion.Id;
        _selectedProduct = new ProductRow(promotion.ProductId, promotion.ProductCode, promotion.ProductDescription);
        ProductBox.Text = _selectedProduct.Display;
        ProductList.Visibility = Visibility.Collapsed;
        NameBox.Text = promotion.Name;
        var type = promotion.Percent > 0m ? "percent" : promotion.DiscountAmount > 0m ? "amount" : "buyPay";
        TypeBox.SelectedItem = TypeBox.Items.OfType<ComboBoxItem>().First(item => string.Equals(item.Tag?.ToString(), type, StringComparison.Ordinal));
        ValueOneBox.Text = (type == "percent" ? promotion.Percent : type == "amount" ? promotion.DiscountAmount : promotion.BuyQuantity).ToString("0.###", CultureInfo.CurrentCulture);
        ValueTwoBox.Text = (type == "buyPay" ? promotion.PayQuantity : 0m).ToString("0.###", CultureInfo.CurrentCulture);
        StartDate.SelectedDate = promotion.StartsAtUtc?.ToLocalTime().Date;
        EndDate.SelectedDate = promotion.EndsAtUtc?.ToLocalTime().Date.AddDays(-1);
        SaveButtonText.Text = "Guardar cambios";
        StatusText.Text = $"Editando: {promotion.Name}.";
    }
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct is null || string.IsNullOrWhiteSpace(NameBox.Text)) { ShowResult("Revisa la promoción", "Selecciona un producto y escribe un nombre único.", OperationResultKind.Warning); return; }
        var type = (TypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!TryDecimal(ValueOneBox.Text, out var first) || !TryDecimal(ValueTwoBox.Text, out var second)) { ShowResult("Revisa la promoción", "Los valores de la promoción deben ser numéricos.", OperationResultKind.Warning); return; }
        decimal percent = type == "percent" ? first : 0m, amount = type == "amount" ? first : 0m, buy = type == "buyPay" ? first : 0m, pay = type == "buyPay" ? second : 0m;
        if (type == "percent" && (percent <= 0 || percent >= 100) || type == "amount" && amount <= 0 || type == "buyPay" && (buy <= 0 || pay <= 0 || pay >= buy)) { ShowResult("Revisa la promoción", "Los valores de la promoción no son válidos para el tipo seleccionado.", OperationResultKind.Warning); return; }
        var body = new { productId = _selectedProduct.Id, name = NameBox.Text.Trim(), percent, discountAmount = amount, buyQuantity = buy, payQuantity = pay, startsAtUtc = ToUtc(StartDate.SelectedDate, false), endsAtUtc = ToUtc(EndDate.SelectedDate, true) };
        var promotionId = _editingPromotionId;
        var editing = promotionId.HasValue;
        var targetId = promotionId.GetValueOrDefault();
        try
        {
            using var response = editing
                ? await ApiClient.Client.PutAsJsonAsync($"/api/promotions/{targetId}", body)
                : await ApiClient.Client.PostAsJsonAsync("/api/promotions", body);
            if (!response.IsSuccessStatusCode)
            {
                var message = await ConfigurationFeedback.ReadErrorAsync(response, editing ? "No se pudo actualizar la promoción." : "No se pudo guardar la promoción.");
                StatusText.Text = message;
                ShowResult(editing ? "Promoción no actualizada" : "Promoción no guardada", message, OperationResultKind.Error);
                return;
            }
            StatusText.Text = editing ? "Promoción actualizada." : "Promoción guardada.";
            ClearForm();
            await LoadPromotionsAsync();
        }
        catch (Exception exception)
        {
            var message = ConnectionHelp.FromException(exception, editing ? "No se pudo actualizar la promoción." : "No se pudo guardar la promoción.");
            StatusText.Text = message;
            ShowResult(editing ? "Promoción no actualizada" : "Promoción no guardada", message, OperationResultKind.Error);
        }
    }
    private async void OnDeactivateClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPromotion is null) { ShowResult("Selecciona una promoción", "Selecciona una promoción activa para darla de baja.", OperationResultKind.Information); return; }
        if (MessageBox.Show($"¿Desactivar {_selectedPromotion.Name}?", "Desactivar promoción", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.DeleteAsync($"/api/promotions/{_selectedPromotion.Id}");
            if (!response.IsSuccessStatusCode)
            {
                var message = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo desactivar la promoción.");
                StatusText.Text = message;
                ShowResult("Promoción no desactivada", message, OperationResultKind.Error);
                return;
            }
            await LoadPromotionsAsync();
            StatusText.Text = "Promoción desactivada.";
        }
        catch (Exception exception)
        {
            var message = ConnectionHelp.FromException(exception, "No se pudo desactivar la promoción.");
            StatusText.Text = message;
            ShowResult("Promoción no desactivada", message, OperationResultKind.Error);
        }
    }
    private async void OnReactivateClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPromotion is null) { ShowResult("Selecciona una promoción", "Selecciona una promoción inactiva para reactivarla.", OperationResultKind.Information); return; }
        if (MessageBox.Show($"¿Reactivar {_selectedPromotion.Name}? Volverá a aplicarse durante el cobro cuando esté vigente.", "Reactivar promoción", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync($"/api/promotions/{_selectedPromotion.Id}/status", new { isActive = true });
            if (!response.IsSuccessStatusCode) { var message = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo reactivar la promoción."); StatusText.Text = message; ShowResult("Promoción no reactivada", message, OperationResultKind.Error); return; }
            await LoadPromotionsAsync(); StatusText.Text = "Promoción reactivada.";
        }
        catch (Exception exception) { var message = ConnectionHelp.FromException(exception, "No se pudo reactivar la promoción."); StatusText.Text = message; ShowResult("Promoción no reactivada", message, OperationResultKind.Error); }
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
            _editButton = new Button { Content = "Editar seleccionada", Style = FindResource("PrimaryButtonStyle") as Style, IsEnabled = false };
            _editButton.Click += OnEditClick;
            actions.Children.Add(_editButton);
            _deactivateButton.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(_deactivateButton);
            _reactivateButton = new Button { Content = "Reactivar seleccionada", Style = FindResource("ConfirmButtonStyle") as Style, Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
            _reactivateButton.Click += OnReactivateClick;
            actions.Children.Add(_reactivateButton);
            footer.Children.Add(actions);
        }
        footer.Children.Remove(StatusText);
        var information = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _showInactiveButton = new Button { Style = FindResource("PreviousButtonStyle") as Style, MinWidth = 115 };
        _showInactiveButton.Click += OnToggleInactiveClick;
        UpdateInactiveButton();
        information.Children.Add(_showInactiveButton);
        StatusText.Margin = new Thickness(14, 0, 0, 0);
        information.Children.Add(StatusText);
        footer.Children.Add(information);
    }
    private async void OnToggleInactiveClick(object sender, RoutedEventArgs e)
    {
        _includeInactive = !_includeInactive;
        UpdateInactiveButton();
        await LoadPromotionsAsync();
    }
    private void UpdateInactiveButton()
    {
        if (_showInactiveButton is null) return;
        _showInactiveButton.Content = _includeInactive ? "Ocultar bajas" : "Mostrar bajas";
        _showInactiveButton.Style = FindResource(_includeInactive ? "PrimaryButtonStyle" : "PreviousButtonStyle") as Style;
    }
    private void ClearForm()
    {
        _editingPromotionId = null;
        _selectedProduct = null;
        _selectedPromotion = null;
        PromotionsGrid.UnselectAll();
        ProductBox.Clear(); NameBox.Clear(); StartDate.SelectedDate = null; EndDate.SelectedDate = null; TypeBox.SelectedIndex = 0; ValueOneBox.Text = "10"; ValueTwoBox.Text = "0";
        SaveButtonText.Text = "Guardar";
    }
    private void ShowResult(string title, string message, OperationResultKind kind) => new OperationResultWindow(title, message, kind) { Owner = this }.ShowDialog();
    private static DateTimeOffset? ToUtc(DateTime? date, bool end) { if (date is null) return null; var local = DateTime.SpecifyKind(date.Value.Date.AddDays(end ? 1 : 0), DateTimeKind.Local); return new DateTimeOffset(local).ToUniversalTime(); }
    private static bool TryDecimal(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private sealed record ProductRow(Guid Id, string Code, string Description) { public string Display => $"{Code} | {Description}"; }
    private sealed record PromotionRow(Guid Id, Guid ProductId, string Name, decimal Percent, decimal DiscountAmount, decimal BuyQuantity, decimal PayQuantity, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc, bool IsActive, string ProductCode = "", string ProductDescription = "")
    {
        public string ProductDisplay => string.IsNullOrWhiteSpace(ProductCode) ? ProductDescription : $"{ProductCode} | {ProductDescription}";
        public string State => IsActive ? "Activa" : "Inactiva";
    }
}
