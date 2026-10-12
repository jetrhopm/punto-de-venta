using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class MobilePurchaseRequestsWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private bool _busy;

    public MobilePurchaseRequestsWindow()
    {
        InitializeComponent();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        Loaded += async (_, _) => await LoadRequestsAsync();
    }

    public MobilePurchaseRequestReview? SelectedRequest { get; private set; }

    private async Task LoadRequestsAsync()
    {
        if (_busy) return;
        _busy = true;
        SetBusy(true);
        try
        {
            var selectedId = (RequestsGrid.SelectedItem as MobilePurchaseRequestQueueItem)?.Id;
            var rows = await Client.GetFromJsonAsync<List<MobilePurchaseRequestQueueItem>>("/api/purchases/mobile-requests") ?? [];
            RequestsGrid.ItemsSource = rows;
            RequestsGrid.SelectedItem = selectedId is Guid id ? rows.FirstOrDefault(item => item.Id == id) : rows.FirstOrDefault();
            StatusText.Text = rows.Count == 0 ? "No hay solicitudes pendientes." : $"{rows.Count} solicitud(es) pendientes.";
            if (RequestsGrid.SelectedItem is MobilePurchaseRequestQueueItem selected)
                await LoadRequestDetailsAsync(selected);
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudieron cargar solicitudes");
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RequestsGrid.SelectedItem is not MobilePurchaseRequestQueueItem item || _busy) return;
        _busy = true;
        SetBusy(true);
        try { await LoadRequestDetailsAsync(item); }
        finally { _busy = false; SetBusy(false); }
    }

    private async Task LoadRequestDetailsAsync(MobilePurchaseRequestQueueItem item)
    {
        try
        {
            using var response = await Client.GetAsync($"/api/purchases/mobile-requests/{item.Id}");
            if (!response.IsSuccessStatusCode) { StatusText.Text = await PurchasePricing.ReadErrorAsync(response); return; }
            var request = await response.Content.ReadFromJsonAsync<MobilePurchaseRequestReview>();
            if (request is null) { StatusText.Text = "No se pudo leer la solicitud."; return; }
            RequestTitle.Text = $"{request.Supplier} · {request.Total:C2}";
            RequestMeta.Text = $"Capturó {request.CreatedBy} desde {request.DeviceName} · {request.CreatedAtUtc.ToLocalTime():f}";
            LinesGrid.ItemsSource = request.Lines;
            ProposalText.Text = request.ProposedPayFromCashRegister
                ? $"El teléfono propone retirar {request.ProposedCashWithdrawn.GetValueOrDefault():C2} de caja. Confirma el monto real al registrar."
                : "El teléfono indica que el pago no saldrá de caja. Puedes revisarlo al registrar.";
            RejectReasonBox.Clear();
            RejectButton.IsEnabled = true;
            ReviewButton.IsEnabled = true;
            StatusText.Text = $"{request.Lines.Count} producto(s) · Total {request.Total:C2}";
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo abrir la solicitud"); }
    }

    private async void OnRejectClick(object sender, RoutedEventArgs e)
    {
        if (RequestsGrid.SelectedItem is not MobilePurchaseRequestQueueItem item || _busy) return;
        if (MessageBox.Show($"¿Rechazar la solicitud de {item.Supplier} por {item.Total:C2}? No cambiará inventario ni caja.",
                "Rechazar solicitud", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _busy = true;
        SetBusy(true);
        try
        {
            using var response = await Client.PostAsJsonAsync($"/api/purchases/mobile-requests/{item.Id}/reject", new { reason = RejectReasonBox.Text.Trim() });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await PurchasePricing.ReadErrorAsync(response); return; }
            StatusText.Text = "Solicitud rechazada. El teléfono verá el resultado al actualizar.";
            _busy = false;
            await LoadRequestsAsync();
            _busy = true;
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo rechazar la solicitud"); }
        finally { _busy = false; SetBusy(false); }
    }

    private async void OnReviewClick(object sender, RoutedEventArgs e)
    {
        if (RequestsGrid.SelectedItem is not MobilePurchaseRequestQueueItem item || _busy) return;
        _busy = true;
        SetBusy(true);
        try
        {
            using var response = await Client.GetAsync($"/api/purchases/mobile-requests/{item.Id}");
            if (!response.IsSuccessStatusCode) { StatusText.Text = await PurchasePricing.ReadErrorAsync(response); return; }
            SelectedRequest = await response.Content.ReadFromJsonAsync<MobilePurchaseRequestReview>();
            if (SelectedRequest is null) { StatusText.Text = "No se pudo leer la solicitud."; return; }
            DialogResult = true;
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la solicitud"); }
        finally { _busy = false; SetBusy(false); }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadRequestsAsync();

    private void SetBusy(bool busy)
    {
        RequestsGrid.IsEnabled = !busy;
        RejectButton.IsEnabled = !busy && RequestsGrid.SelectedItem is not null;
        ReviewButton.IsEnabled = !busy && RequestsGrid.SelectedItem is not null;
    }
}
