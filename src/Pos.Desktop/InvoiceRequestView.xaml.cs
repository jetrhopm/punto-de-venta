using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Desktop;

public partial class InvoiceRequestView : UserControl
{
    private CancellationTokenSource? _searchCancellation;
    private InvoiceSale? _selectedSale;

    public InvoiceRequestView()
    {
        InitializeComponent();
        PacSettingsButton.Visibility = InstallationRoleContext.IsServer ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await LoadRequestsAsync();
    }

    private void OnPacSettingsClick(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        new FacturamaSettingsWindow { Owner = owner }.ShowDialog();
    }

    private async void OnFindSaleClick(object sender, RoutedEventArgs e) => await FindSaleAsync();

    private async void OnSaleFolioKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        await FindSaleAsync();
        e.Handled = true;
    }

    private async Task FindSaleAsync()
    {
        _selectedSale = null;
        SaleSummaryBorder.Visibility = Visibility.Collapsed;
        if (!long.TryParse(SaleFolioBox.Text.Trim(), out var folio) || folio <= 0)
        {
            FormStatusText.Text = "Escribe el folio de una venta confirmada.";
            SaleFolioBox.Focus();
            return;
        }
        try
        {
            using var response = await ApiClient.Client.GetAsync($"api/invoice-requests/sales/{folio}");
            if (!response.IsSuccessStatusCode)
            {
                FormStatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se encontró una venta confirmada con ese folio.");
                return;
            }
            _selectedSale = await response.Content.ReadFromJsonAsync<InvoiceSale>();
            if (_selectedSale is null) { FormStatusText.Text = "No se pudo leer la venta seleccionada."; return; }
            SaleSummaryText.Text = $"Venta { _selectedSale.Folio } · total ${_selectedSale.Total:0.00}";
            SaleSummaryBorder.Visibility = Visibility.Visible;
            FormStatusText.Text = "Captura los datos del receptor y registra la solicitud.";
            TaxIdBox.Focus();
        }
        catch (Exception exception) { FormStatusText.Text = ConnectionHelp.FromException(exception, "No se pudo consultar la venta"); }
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSale is null) { FormStatusText.Text = "Busca primero una venta confirmada."; return; }
        var command = new { saleFolio = _selectedSale.Folio, receiverTaxId = TaxIdBox.Text, receiverName = ReceiverNameBox.Text, receiverEmail = EmailBox.Text, notes = NotesBox.Text };
        try
        {
            using var response = await ApiClient.Client.PostAsJsonAsync("api/invoice-requests", command);
            if (!response.IsSuccessStatusCode)
            {
                FormStatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo registrar la solicitud.");
                OperationFeedback.Show(Window.GetWindow(this)!, "Solicitud no registrada", FormStatusText.Text, OperationResultKind.Warning);
                return;
            }
            OperationFeedback.Show(Window.GetWindow(this)!, "Solicitud registrada", "La solicitud quedó pendiente de timbrado. No se generó una factura fiscal.", OperationResultKind.Success);
            ClearForm();
            await LoadRequestsAsync();
        }
        catch (Exception exception)
        {
            FormStatusText.Text = ConnectionHelp.FromException(exception, "No se pudo registrar la solicitud");
            OperationFeedback.Show(Window.GetWindow(this)!, "Solicitud no registrada", FormStatusText.Text, OperationResultKind.Error);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadRequestsAsync();

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(350, _searchCancellation.Token);
            await LoadRequestsAsync(_searchCancellation.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadRequestsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var query = SearchBox.Text.Trim();
            var uri = string.IsNullOrWhiteSpace(query) ? "api/invoice-requests" : $"api/invoice-requests?query={Uri.EscapeDataString(query)}";
            var rows = await ApiClient.Client.GetFromJsonAsync<List<InvoiceRequestRow>>(uri, cancellationToken) ?? [];
            RequestsGrid.ItemsSource = rows.Select(item => new InvoiceRequestRowView(item)).ToList();
            HistoryStatusText.Text = rows.Count == 0 ? "No hay solicitudes registradas." : $"{rows.Count} solicitud(es) mostrada(s).";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { HistoryStatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar el historial de solicitudes"); }
    }

    private void ClearForm()
    {
        _selectedSale = null;
        SaleFolioBox.Clear(); TaxIdBox.Clear(); ReceiverNameBox.Clear(); EmailBox.Clear(); NotesBox.Clear();
        SaleSummaryBorder.Visibility = Visibility.Collapsed;
        FormStatusText.Text = "Solicitud guardada. Selecciona otra venta si necesitas registrar una nueva.";
        SaleFolioBox.Focus();
    }

    private sealed record InvoiceSale(long Folio, Guid SaleId, decimal Total, DateTimeOffset CreatedAtUtc);
    private sealed record InvoiceRequestRow(Guid Id, long SaleFolio, decimal SaleTotal, DateTimeOffset SaleCreatedAtUtc, string ReceiverTaxId, string ReceiverName, string? ReceiverEmail, string Notes, string Status, DateTimeOffset RequestedAtUtc, string RequestedBy);
    private sealed record InvoiceRequestRowView(InvoiceRequestRow Source)
    {
        public long SaleFolio => Source.SaleFolio;
        public string SaleTotalText => $"${Source.SaleTotal:0.00}";
        public string ReceiverTaxId => Source.ReceiverTaxId;
        public string ReceiverName => Source.ReceiverName;
        public string StatusText => Source.Status == "PendingStamping" ? "Pendiente de timbrado" : Source.Status;
        public string RequestedAtText => Source.RequestedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public string RequestedBy => Source.RequestedBy;
    }
}
