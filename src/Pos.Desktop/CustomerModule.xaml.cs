using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MahApps.Metro.IconPacks;

namespace Pos.Desktop;

public partial class CustomerModule : UserControl
{
    private static HttpClient Client => ApiClient.Client;
    private readonly bool _creditMode;
    private CancellationTokenSource? _searchCancellation;
    private CustomerView? _selected;
    private CheckBox? _showInactiveBox;
    private Button? _reactivateCustomerButton;

    public CustomerModule(bool creditMode = false)
    {
        InitializeComponent();
        _creditMode = creditMode;
        ConfigureMode();
        ConfigureInactiveCustomerControls();
        Loaded += async (_, _) => await LoadCustomersAsync();
    }

    private void ConfigureMode()
    {
        if (!_creditMode) return;

        TitleText.Text = "Créditos de clientes";
        SubtitleText.Text = "Consulta cuentas con crédito, saldos pendientes, movimientos y abonos.";
        HeaderIcon.Kind = PackIconMaterialKind.AccountCashOutline;
        HeaderIcon.Foreground = System.Windows.Media.Brushes.White;
        HeaderIconBackground.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 138, 104));
        ListTitleText.Text = "Cuentas con crédito activo";
        CreditSummaryPanel.Visibility = Visibility.Visible;
        CustomerActionsPanel.Visibility = Visibility.Collapsed;
        CreditActionsPanel.Visibility = Visibility.Visible;
        CustomersList.ToolTip = "Doble clic para consultar la cuenta, movimientos y abonos";
    }

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(180, _searchCancellation.Token);
            await LoadCustomersAsync(_searchCancellation.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadCustomersAsync(CancellationToken cancellationToken = default, Guid? selectCustomerId = null)
    {
        try
        {
            var query = Uri.EscapeDataString(SearchTextBox.Text.Trim());
            var customers = await Client.GetFromJsonAsync<List<CustomerView>>($"/api/customers?q={query}&creditOnly={_creditMode.ToString().ToLowerInvariant()}&includeInactive={(_showInactiveBox?.IsChecked == true).ToString().ToLowerInvariant()}", cancellationToken) ?? [];
            CustomersList.ItemsSource = customers;
            _selected = selectCustomerId is null ? null : customers.FirstOrDefault(customer => customer.Id == selectCustomerId);
            CustomersList.SelectedItem = _selected;
            SetActionAvailability();
            if (_creditMode) CreditSummaryText.Text = customers.Sum(customer => customer.Balance).ToString("C2");
            if (customers.Count == 0) StatusText.Text = _creditMode ? "No hay clientes con crédito activo." : "No hay clientes activos que coincidan con la búsqueda.";
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private void OnCustomerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = CustomersList.SelectedItem as CustomerView;
        SetActionAvailability();
        if (_selected is not null) StatusText.Text = _creditMode
            ? $"Cuenta seleccionada: {_selected.Name}. Saldo pendiente: {_selected.Balance:C2}."
            : $"Cliente seleccionado: {_selected.Name}. Doble clic para consultar sus datos.";
    }

    private void SetActionAvailability()
    {
        EditCustomerButton.IsEnabled = _selected is not null;
        DeactivateCustomerButton.IsEnabled = _selected?.IsActive == true;
        if (_reactivateCustomerButton is not null) _reactivateCustomerButton.IsEnabled = _selected?.IsActive == false;
        OpenCreditAccountButton.IsEnabled = _selected is not null;
    }

    private async void OnCreateCustomerClick(object sender, RoutedEventArgs e) => await OpenCustomerEditorAsync(null);

    private async void OnEditCustomerClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) await OpenCustomerEditorAsync(_selected);
    }

    private async void OnDeactivateCustomerClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) await DeactivateCustomerAsync(_selected);
    }

    private async void OnReactivateCustomerClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsActive) return;
        if (MessageBox.Show($"¿Reactivar a {_selected.Name}? Volverá a estar disponible para ventas y créditos si estos están habilitados.", "Reactivar cliente", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await Client.PutAsJsonAsync($"/api/customers/{_selected.Id}/status", new { isActive = true });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await ReadErrorAsync(response); return; }
            await LoadCustomersAsync(selectCustomerId: _selected.Id);
            StatusText.Text = "Cliente reactivado correctamente.";
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private void ConfigureInactiveCustomerControls()
    {
        if (_creditMode || SearchTextBox.Parent is not StackPanel searchPanel) return;
        _showInactiveBox = new CheckBox { Content = "Mostrar bajas", Margin = new Thickness(0, 8, 0, 0) };
        _showInactiveBox.Checked += async (_, _) => await LoadCustomersAsync();
        _showInactiveBox.Unchecked += async (_, _) => await LoadCustomersAsync();
        searchPanel.Children.Add(_showInactiveBox);

        _reactivateCustomerButton = new Button { Content = "Reactivar cliente", Style = FindResource("ConfirmButtonStyle") as Style, MinWidth = 176, Margin = new Thickness(10, 0, 0, 0), IsEnabled = false };
        _reactivateCustomerButton.Click += OnReactivateCustomerClick;
        CustomerActionsPanel.Children.Add(_reactivateCustomerButton);
    }

    private async void OnOpenCreditAccountClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) await OpenCreditAccountAsync(_selected);
    }

    private async void OnCustomerDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selected is null) return;
        if (_creditMode)
        {
            await OpenCreditAccountAsync(_selected);
            return;
        }

        var dialog = new CustomerDetailsWindow(_selected) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        switch (dialog.Action)
        {
            case CustomerDetailsAction.Edit:
                await OpenCustomerEditorAsync(_selected);
                break;
            case CustomerDetailsAction.ManageCredit:
                await OpenCreditAccountAsync(_selected);
                break;
            case CustomerDetailsAction.Deactivate:
                await DeactivateCustomerAsync(_selected);
                break;
        }
    }

    private async Task OpenCustomerEditorAsync(CustomerView? customer)
    {
        var dialog = new CustomerEditorWindow(customer) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        try
        {
            var result = dialog.Result;
            using var response = customer is null
                ? await Client.PostAsJsonAsync("/api/customers", new { name = result.Name, phone = result.Phone, email = result.Email, taxId = result.TaxId, creditLimit = 0m, creditEnabled = false })
                : await Client.PutAsJsonAsync($"/api/customers/{customer.Id}", new { name = result.Name, phone = result.Phone, email = result.Email, taxId = result.TaxId, creditLimit = customer.CreditLimit, creditEnabled = customer.CreditEnabled });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ReadErrorAsync(response);
                return;
            }

            var saved = await response.Content.ReadFromJsonAsync<CustomerView>();
            await LoadCustomersAsync(selectCustomerId: saved?.Id);
            StatusText.Text = customer is null ? "Cliente creado correctamente. Puedes habilitarle crédito desde sus detalles." : "Datos generales del cliente actualizados.";
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private async Task OpenCreditAccountAsync(CustomerView customer)
    {
        var dialog = new CreditAccountWindow(customer) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        await LoadCustomersAsync(selectCustomerId: customer.Id);
    }

    private async Task DeactivateCustomerAsync(CustomerView customer)
    {
        if (MessageBox.Show($"¿Deseas desactivar a {customer.Name}? No aparecerá para ventas ni créditos.", "Desactivar cliente", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await Client.PutAsJsonAsync($"/api/customers/{customer.Id}/status", new { isActive = false });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ReadErrorAsync(response);
                return;
            }
            await LoadCustomersAsync();
            StatusText.Text = "Cliente desactivado correctamente.";
        }
        catch (HttpRequestException) { StatusText.Text = ConnectionHelp.ApiUnavailable; }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? "No se pudo completar la operación." : content;
    }
}
