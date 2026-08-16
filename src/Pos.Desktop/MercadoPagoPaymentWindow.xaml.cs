using System.ComponentModel;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class MercadoPagoPaymentWindow : Window
{
    private readonly Guid _operationId;
    private readonly decimal _amount;
    private readonly CancellationTokenSource _polling = new();
    private bool _finished;
    public bool Approved { get; private set; }

    public MercadoPagoPaymentWindow(Guid operationId, decimal amount)
    {
        InitializeComponent(); _operationId = operationId; _amount = decimal.Round(amount, 2); AmountText.Text = $"${_amount:0.00}"; Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            using var create = await ApiClient.Client.PostAsJsonAsync("api/integrations/mercado-pago/orders", new { operationId = _operationId, amount = _amount, description = "Venta JetVenta" }, _polling.Token);
            if (!create.IsSuccessStatusCode) { await FailAsync(await create.Content.ReadAsStringAsync()); return; }
            var state = await create.Content.ReadFromJsonAsync<OrderResult>(cancellationToken: _polling.Token);
            while (state is not null && !state.Finished)
            {
                ShowStatus(state.Status);
                await Task.Delay(TimeSpan.FromSeconds(2), _polling.Token);
                state = await ApiClient.Client.GetFromJsonAsync<OrderResult>($"api/integrations/mercado-pago/orders/{_operationId}", _polling.Token);
            }
            if (state?.Approved == true) { Approved = true; _finished = true; StatusText.Text = "Pago aprobado. Confirmando la venta en JetVenta..."; DialogResult = true; }
            else if (state is not null) await FailAsync(StatusMessage(state));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await FailAsync("No se pudo conocer el resultado del cobro. No se confirmó la venta. Consulta la terminal y vuelve a revisar antes de cobrar otra vez.\n\n" + exception.Message); }
    }

    private void ShowStatus(string status) => StatusText.Text = status switch { "AtTerminal" => "La terminal recibió el cobro. Pide al cliente insertar, acercar o deslizar su tarjeta.", "Created" => "Cobro enviado. Esperando que la terminal lo reciba...", _ => "Consultando el resultado autorizado de Mercado Pago..." };
    private static string StatusMessage(OrderResult state) => state.Status switch { "Rejected" => "Mercado Pago rechazó el pago. La venta no fue registrada.", "Canceled" => "El cobro fue cancelado. La venta no fue registrada.", "Expired" => "El cobro expiró. La venta no fue registrada.", "Unknown" => "La terminal pide revisar el resultado. La venta no se confirmó para evitar un cobro duplicado.", _ => $"El cobro terminó con estado {state.Status}. La venta no fue registrada." };
    private Task FailAsync(string message) { _finished = true; StatusText.Text = message; CancelButton.Content = "Cerrar"; return Task.CompletedTask; }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (_finished) { DialogResult = false; return; }
        CancelButton.IsEnabled = false;
        try { using var _ = await ApiClient.Client.PostAsync($"api/integrations/mercado-pago/orders/{_operationId}/cancel", null); StatusText.Text = "Solicitud de cancelación enviada. Si la terminal ya recibió el cobro, cancélalo también en la terminal."; }
        catch { StatusText.Text = "No se pudo cancelar desde JetVenta. Revisa la terminal antes de volver a cobrar."; }
        _finished = true; _polling.Cancel(); CancelButton.Content = "Cerrar"; CancelButton.IsEnabled = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_finished) { e.Cancel = true; StatusText.Text = "Cancela el cobro con el botón para evitar dejar el resultado sin revisar."; }
        else _polling.Cancel();
    }

    private sealed record OrderResult(Guid OperationId, string OrderId, string Status, string StatusDetail, decimal Amount, bool Approved, bool Finished);
}
