using Pos.Infrastructure;

public sealed class DailyBackupHostedService(IServiceScopeFactory scopeFactory, ILogger<DailyBackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<DatabaseMaintenanceService>();
                if (await maintenance.EnsureDailyAutomaticBackupAsync(stoppingToken)) logger.LogInformation("Respaldo automático diario creado correctamente.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "No se pudo crear el respaldo automático diario."); }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
