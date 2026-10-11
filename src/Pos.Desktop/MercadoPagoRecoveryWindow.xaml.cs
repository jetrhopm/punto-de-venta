using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class MercadoPagoRecoveryWindow : Window
{
    private readonly ObservableCollection<PendingOrderView> _orders = [];

    public MercadoPagoRecoveryWindow()
    {
        InitializeComponent();
        OrdersGrid.ItemsSource = _orders;
        Loaded += async (_, _) => await LoadAsync();
    }

    public Guid? RecoveredSaleId { get; private set; }

    private async Task LoadAsync()
    {
        try
        {
            var orders = await ApiClient.Client.GetFromJsonAsync<List<PendingOrderResponse>>("api/integrations/mercado-pago/orders/pending") ?? [];
            _orders.Clear();
            foreach (var order in orders) _orders.Add(new PendingOrderView(order));
            StatusText.Text = _orders.Count == 0
                ? "No hay cobros Point pendientes en esta caja."
                : _orders.Count == 1 ? "Hay 1 cobro que requiere revisión." : $"Hay {_orders.Count} cobros que requieren revisión.";
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not PendingOrderView selected)
        {
            StatusText.Text = "Selecciona un cobro para consultar su estado.";
            return;
        }

        await SetBusyAsync(true);
        try
        {
            using var response = await ApiClient.Client.GetAsync($"api/integrations/mercado-pago/orders/{selected.OperationId}");
            StatusText.Text = response.IsSuccessStatusCode
                ? "Estado actualizado con Mercado Pago."
                : await ConfigurationFeedback.ReadErrorAsync(response, "Mercado Pago no pudo confirmar el estado del cobro.");
            await LoadAsync();
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
        finally { await SetBusyAsync(false); }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not PendingOrderView selected)
        {
            StatusText.Text = "Selecciona un cobro para cancelar en la terminal.";
            return;
        }

        if (selected.IsApproved)
        {
            StatusText.Text = "El cobro ya fue aprobado. No se cancela en terminal; confirma el ticket o procesa una devolución desde Historial.";
            return;
        }

        await SetBusyAsync(true);
        try
        {
            using var response = await ApiClient.Client.PostAsync($"api/integrations/mercado-pago/orders/{selected.OperationId}/cancel", null);
            StatusText.Text = response.IsSuccessStatusCode
                ? "Mercado Pago confirmó la cancelación."
                : await ConfigurationFeedback.ReadErrorAsync(response, "Mercado Pago no confirmó la cancelación.");
            await LoadAsync();
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
        finally { await SetBusyAsync(false); }
    }

    private async void OnCompleteClick(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not PendingOrderView selected)
        {
            StatusText.Text = "Selecciona un cobro aprobado para finalizar su ticket.";
            return;
        }
        if (!selected.IsApproved)
        {
            StatusText.Text = "Primero actualiza el estado. Sólo un cobro aprobado puede finalizar una venta.";
            return;
        }

        await SetBusyAsync(true);
        try
        {
            using var response = await ApiClient.Client.PostAsync($"api/integrations/mercado-pago/orders/{selected.OperationId}/complete", null);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo finalizar el ticket recuperado.");
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<RecoveredSaleResponse>();
            if (result is null)
            {
                StatusText.Text = "JetVenta no recibió la confirmación final de la venta.";
                return;
            }

            RecoveredSaleId = result.SaleId;
            DialogResult = true;
        }
        catch (HttpRequestException)
        {
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
        finally { await SetBusyAsync(false); }
    }

    private Task SetBusyAsync(bool busy)
    {
        RefreshButton.IsEnabled = !busy;
        CompleteButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        return Task.CompletedTask;
    }

    private sealed record PendingOrderResponse(Guid OperationId, Guid SaleOperationId, int? TicketNumber, decimal Amount, string Status, string StatusDetail, DateTimeOffset CreatedAtUtc, bool SaleConfirmed);
    private sealed record RecoveredSaleResponse(Guid SaleId, decimal Total, decimal Change, bool Existing, bool InventoryAttentionRequired);

    private sealed class PendingOrderView(PendingOrderResponse order)
    {
        public Guid OperationId { get; } = order.OperationId;
        public bool IsApproved => string.Equals(order.Status, "Approved", StringComparison.Ordinal);
        public string TicketLabel => order.TicketNumber is int number ? $"Ticket {number}" : "Sin ticket";
        public string AmountLabel => $"${order.Amount:0.00}";
        public string StatusLabel => order.Status switch
        {
            "AtTerminal" => "En terminal",
            "CreationPending" => "Creación pendiente",
            "PendingVerification" => "Verificando importe",
            "ActionRequired" => "Requiere acción",
            "PendingReview" => "Revisión necesaria",
            "Approved" => "Aprobado sin venta",
            _ => order.Status
        };
        public string CreatedLabel => order.CreatedAtUtc.ToLocalTime().ToString("dd/MM HH:mm");
        public string Detail => string.IsNullOrWhiteSpace(order.StatusDetail) ? "Sin detalle disponible." : order.StatusDetail;
    }
}
