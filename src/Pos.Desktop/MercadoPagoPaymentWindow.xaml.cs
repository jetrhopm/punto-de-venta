using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class MercadoPagoPaymentWindow : Window
{
    private readonly Guid _operationId;
    private Guid _attemptId;
    private readonly decimal _amount;
    private readonly CancellationTokenSource _polling = new();
    private bool _finished;
    public bool Approved { get; private set; }

    public MercadoPagoPaymentWindow(Guid operationId, Guid attemptId, decimal amount)
    {
        InitializeComponent(); _operationId = operationId; _attemptId = attemptId; _amount = decimal.Round(amount, 2); AmountText.Text = $"${_amount:0.00}"; Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            using var create = await ApiClient.Client.PostAsJsonAsync("api/integrations/mercado-pago/orders", new { saleOperationId = _operationId, attemptId = _attemptId, amount = _amount, description = "Venta JetVenta" }, _polling.Token);
            if (!create.IsSuccessStatusCode) { await FailAsync(ReadableError(await create.Content.ReadAsStringAsync())); return; }
            var state = await create.Content.ReadFromJsonAsync<OrderResult>(cancellationToken: _polling.Token);
            if (state is null) { await FailAsync("JetVenta no recibió el estado del cobro Point. No se confirmó la venta."); return; }
            var resumed = state.OperationId != _attemptId;
            _attemptId = state.OperationId;
            if (resumed) StatusText.Text = "Se retomó un cobro Point pendiente de este ticket. Consultando la terminal...";
            while (state is not null && !state.Finished)
            {
                ShowStatus(state.Status);
                await Task.Delay(TimeSpan.FromSeconds(2), _polling.Token);
                state = await RefreshWithRetryAsync();
            }
            if (state?.Approved == true) { Approved = true; _finished = true; StatusText.Text = "Pago aprobado. Confirmando la venta en JetVenta..."; DialogResult = true; }
            else if (state is not null) await FailAsync(StatusMessage(state));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await FailAsync("No se pudo conocer el resultado del cobro después de varios intentos. No se confirmó la venta. Consulta la terminal antes de iniciar otro cobro.\n\n" + exception.Message); }
    }

    private async Task<OrderResult?> RefreshWithRetryAsync()
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { return await ApiClient.Client.GetFromJsonAsync<OrderResult>($"api/integrations/mercado-pago/orders/{_attemptId}", _polling.Token); }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastError = exception;
                if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(attempt), _polling.Token);
            }
        }
        throw new HttpRequestException("No fue posible consultar Mercado Pago.", lastError);
    }

    private void ShowStatus(string status) => StatusText.Text = status switch { "AtTerminal" => "La terminal recibió el cobro. Pide al cliente insertar, acercar o deslizar su tarjeta.", "Created" => "Cobro enviado. Esperando que la terminal lo reciba...", _ => "Consultando el resultado autorizado de Mercado Pago..." };
    private static string StatusMessage(OrderResult state) => state.Status switch { "Rejected" => "Mercado Pago rechazó el pago. Puedes intentar otra tarjeta.", "Canceled" => "El cobro fue cancelado. Puedes intentar otra tarjeta.", "Expired" => "El cobro expiró. Puedes intentar otra tarjeta.", "AmountMismatch" => "El importe aprobado no coincide con el solicitado. La venta no fue registrada; requiere conciliación.", "CreationFailed" => "No se pudo crear el cobro. Puedes volver a intentarlo.", "CreationPending" => "JetVenta no pudo confirmar la creación. Vuelve a pulsar cobrar para retomarlo con la misma operación.", _ => $"El cobro terminó con estado {state.Status}. La venta no fue registrada." };
    private static string ReadableError(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            foreach (var field in new[] { "message", "detail", "title" })
            {
                if (root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
            }
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var first = errors.EnumerateObject().SelectMany(item => item.Value.ValueKind == JsonValueKind.Array ? item.Value.EnumerateArray() : []).FirstOrDefault(item => item.ValueKind == JsonValueKind.String);
                if (first.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(first.GetString())) return first.GetString()!;
            }
        }
        catch (JsonException) { }
        return "JetVenta no pudo completar la operación con Mercado Pago. Intenta nuevamente o revisa la terminal.";
    }
    private Task FailAsync(string message) { _finished = true; StatusText.Text = message; CancelButton.Content = "Cerrar"; return Task.CompletedTask; }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (_finished) { DialogResult = false; return; }
        CancelButton.IsEnabled = false;
        try
        {
            using var response = await ApiClient.Client.PostAsync($"api/integrations/mercado-pago/orders/{_attemptId}/cancel", null);
            if (!response.IsSuccessStatusCode) { StatusText.Text = "Mercado Pago no confirmó la cancelación. Revisa la terminal antes de volver a cobrar."; return; }
            var state = await response.Content.ReadFromJsonAsync<OrderResult>();
            if (state?.Status == "Canceled") { StatusText.Text = "Cobro cancelado. Puedes intentar otra tarjeta."; _finished = true; _polling.Cancel(); CancelButton.Content = "Cerrar"; }
            else StatusText.Text = "Mercado Pago no confirmó la cancelación. Revisa la terminal antes de volver a cobrar.";
        }
        catch { StatusText.Text = "No se pudo cancelar desde JetVenta. Revisa la terminal antes de volver a cobrar."; }
        finally { CancelButton.IsEnabled = true; }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_finished) { e.Cancel = true; StatusText.Text = "Cancela el cobro con el botón para evitar dejar el resultado sin revisar."; }
        else _polling.Cancel();
    }

    private sealed record OrderResult(Guid OperationId, string OrderId, string Status, string StatusDetail, decimal Amount, bool Approved, bool Finished);
}
