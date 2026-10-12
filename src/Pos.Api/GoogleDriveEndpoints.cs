using System.Net;
using Pos.Infrastructure.GoogleDrive;

// Respaldos en Google Drive. Se registra con builder.Services.AddGoogleDriveBackups() y
// app.MapGoogleDriveEndpoints(). Las acciones que suben o administran sólo se aceptan desde
// la propia computadora de la base (127.0.0.1): es la caja principal.
public static class GoogleDriveEndpoints
{
    public static IServiceCollection AddGoogleDriveBackups(this IServiceCollection services)
    {
        services.AddSingleton<GoogleDriveStateStore>();
        services.AddSingleton<GoogleDriveAuthorizationFlow>();
        services.AddScoped<GoogleDriveBackupService>();
        services.AddHttpClient<GoogleDriveClient>(client => client.Timeout = TimeSpan.FromMinutes(5));
        return services;
    }

    public static WebApplication MapGoogleDriveEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/backups/google-drive");

        group.MapGet("/status", async (HttpContext context, GoogleDriveBackupService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetStatusAsync(Token(context), IsPrincipal(context), cancellationToken);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });

        group.MapPost("/connect", async (HttpContext context, GoogleDriveBackupService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var url = await service.BeginConnectAsync(Token(context), IsPrincipal(context), cancellationToken);
                return url is null ? Results.Unauthorized() : Results.Ok(new { url });
            }
            catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
        });

        group.MapPost("/disconnect", async (HttpContext context, GoogleDriveBackupService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DisconnectAsync(Token(context), cancellationToken);
            return result is null ? Results.Unauthorized() : Results.Ok();
        });

        group.MapPut("/password", async (HttpContext context, SetGoogleDrivePasswordCommand command, GoogleDriveBackupService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.SetPasswordAsync(Token(context), command, cancellationToken);
                return result is null ? Results.Unauthorized() : Results.Ok();
            }
            catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
        });

        // Al cerrar JetVenta. El escritorio corta la espera con "Omitir por hoy" o a los 60 s;
        // en ese caso el respaldo queda pendiente para el siguiente inicio.
        group.MapPost("/on-close", async (HttpContext context, GoogleDriveBackupService service) =>
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                limit.CancelAfter(TimeSpan.FromSeconds(60));
                var result = await service.OnCloseAsync(Token(context), IsPrincipal(context), limit.Token);
                return result is null ? Results.Unauthorized() : Results.Ok(result);
            }
            catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
            {
                return Results.Conflict(new { message = "No se pudo crear el respaldo para Google Drive: " + exception.Message });
            }
        });

        // Al iniciar sesión en la caja principal: recuperación tras apagón y subida de pendientes
        // en segundo plano, sin detener la venta.
        group.MapPost("/on-startup", async (HttpContext context, GoogleDriveBackupService service, IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILogger<GoogleDriveBackupService> logger) =>
        {
            bool? work;
            try { work = await service.OnStartupAsync(Token(context), IsPrincipal(context), context.RequestAborted); }
            catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
            {
                logger.LogWarning(exception, "No se pudo crear el respaldo de recuperación para Google Drive.");
                work = true;
            }
            if (work is null) return Results.Unauthorized();
            if (work.Value)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<GoogleDriveBackupService>().ProcessPendingAsync(lifetime.ApplicationStopping);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception exception) { logger.LogWarning(exception, "No se pudieron subir los respaldos pendientes a Google Drive."); }
                });
            }
            return Results.Ok(new { started = work.Value });
        });

        group.MapPost("/upload-now", async (HttpContext context, GoogleDriveBackupService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.UploadNowAsync(Token(context), IsPrincipal(context), cancellationToken);
                return result is null ? Results.Unauthorized() : Results.Ok(result);
            }
            catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException) { return Results.Conflict(new { message = exception.Message }); }
        });

        group.MapGet("/files", async (HttpContext context, GoogleDriveBackupService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.ListFilesAsync(Token(context), cancellationToken);
                return result is null ? Results.Unauthorized() : Results.Ok(result);
            }
            catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
            catch (GoogleDriveException exception) { return Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status502BadGateway); }
            catch (HttpRequestException) { return Results.Json(new { message = "Sin conexión a internet." }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        });

        group.MapPost("/restore", async (HttpContext context, RestoreFromGoogleDriveCommand command, GoogleDriveBackupService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.DownloadForRestoreAsync(Token(context), IsPrincipal(context), command, cancellationToken);
                return result is null ? Results.Unauthorized() : Results.Ok(result);
            }
            catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
            catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
            catch (GoogleDriveException exception) { return Results.Json(new { message = exception.Message }, statusCode: StatusCodes.Status502BadGateway); }
            catch (HttpRequestException) { return Results.Json(new { message = "Sin conexión a internet." }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        });

        return app;
    }

    private static string Token(HttpContext context) => context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrincipal(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address);
    }
}
