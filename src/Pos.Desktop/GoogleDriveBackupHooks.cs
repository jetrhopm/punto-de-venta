using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

// Respaldos en Google Drive desde el escritorio. Sólo actúa en la caja principal.
internal static class GoogleDriveBackupHooks
{
    private static bool _startupNotified;

    // Cliente con el mismo servidor y sesión que ApiClient, pero con más tiempo de espera:
    // crear y subir un respaldo puede tardar más que los 10 s del cliente normal.
    public static HttpClient CreateLongRunningClient(TimeSpan timeout)
    {
        var source = ApiClient.Client;
        var client = new HttpClient { BaseAddress = source.BaseAddress, Timeout = timeout };
        foreach (var header in source.DefaultRequestHeaders) client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        return client;
    }

    // Al iniciar sesión: sube lo pendiente y, si el uso anterior terminó mal, crea un respaldo
    // de recuperación. Corre en segundo plano y nunca detiene la venta.
    public static async Task NotifyStartupAsync(Action<string> showNotice)
    {
        if (_startupNotified || !InstallationRoleContext.IsServer) return;
        _startupNotified = true;
        try
        {
            using var client = CreateLongRunningClient(TimeSpan.FromMinutes(2));
            using var response = await client.PostAsync("api/backups/google-drive/on-startup", null);
            var status = await client.GetFromJsonAsync<GoogleDriveStatusDto>("api/backups/google-drive/status");
            if (status is null || !status.Connected) return;
            if (status.NeedsReconnect) showNotice("Google Drive: vuelve a conectar tu cuenta en Respaldos para seguir guardando tus respaldos.");
            else if (status.StaleWarning) showNotice("Google Drive: hace más de 3 días que no se guarda un respaldo en la nube. Revisa Respaldos > Google Drive.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException) { }
    }

    // Al cerrar JetVenta correctamente. Si Google Drive no está conectado no hace nada.
    public static async Task RunOnExitAsync(Window owner)
    {
        if (!InstallationRoleContext.IsServer) return;
        GoogleDriveStatusDto? status;
        try
        {
            using var client = CreateLongRunningClient(TimeSpan.FromSeconds(5));
            status = await client.GetFromJsonAsync<GoogleDriveStatusDto>("api/backups/google-drive/status");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException) { return; }
        if (status is null || !status.Connected || !status.Configured) return;
        new CloudBackupProgressWindow { Owner = owner }.ShowDialog();
    }
}

internal sealed record GoogleDriveLastUploadDto(string FileName, long SizeBytes, bool Encrypted, bool Verified, DateTimeOffset UploadedAtUtc);
internal sealed record GoogleDriveStatusDto(bool Configured, bool IsPrincipal, bool CanManage, bool Connected, string Email, bool NeedsReconnect, bool PasswordEnabled, GoogleDriveLastUploadDto? LastUpload, int PendingCount, string LastError, string ConnectState, string ConnectMessage, bool StaleWarning);
internal sealed record GoogleDriveCloseDto(bool Uploaded, bool Pending, string Message);
internal sealed record GoogleDriveFileDto(string Id, string Name, DateOnly Day, long SizeBytes, bool Encrypted, DateTimeOffset CreatedAtUtc)
{
    public string DateDisplay => CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public string SizeDisplay => SizeBytes >= 1024 * 1024 ? $"{SizeBytes / 1024d / 1024d:0.0} MB" : $"{Math.Max(1, SizeBytes / 1024)} KB";
    public string ProtectedDisplay => Encrypted ? "Sí" : "No";
}
internal sealed record GoogleDriveRestoreDto(string FileName, long SizeBytes);
