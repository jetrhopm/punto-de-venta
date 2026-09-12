using Pos.Infrastructure;

public sealed class MercadoPagoWebhookHostedService(IServiceScopeFactory scopeFactory, ILogger<MercadoPagoWebhookHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var point = scope.ServiceProvider.GetRequiredService<MercadoPagoPointService>();
                var processed = await point.ReconcileWebhookQueueAsync(stoppingToken);
                if (processed > 0) logger.LogInformation("Se conciliaron {Count} notificación(es) de Mercado Pago.", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "No se pudo conciliar la cola de Mercado Pago."); }

            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
