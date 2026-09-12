using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pos.Infrastructure;

public sealed record MaintenanceModeStatus(bool IsActive, string? Reason, DateTimeOffset? StartedAtUtc);

/// <summary>Persists a short server-wide maintenance lock outside PostgreSQL so it remains visible while a database restore replaces the database.</summary>
public sealed class MaintenanceModeService(PosDbContext database)
{
    private static readonly string StatusPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "config", "maintenance.json");

    public MaintenanceModeStatus GetStatus()
    {
        try
        {
            if (!File.Exists(StatusPath)) return new(false, null, null);
            var status = JsonSerializer.Deserialize<MaintenanceModeStatus>(File.ReadAllText(StatusPath));
            return status is { IsActive: true } ? status : new(false, null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, null, null);
        }
    }

    public async Task<bool> BeginRestoreAsync(string token, CancellationToken cancellationToken)
    {
        if (!await IsAdministratorAsync(token, cancellationToken)) return false;
        Save(new(true, "El servidor está restaurando un respaldo. Las ventas y cambios se reanudarán al terminar.", DateTimeOffset.UtcNow));
        return true;
    }

    public async Task<bool> EndRestoreAsync(string token, CancellationToken cancellationToken)
    {
        if (!await IsAdministratorAsync(token, cancellationToken)) return false;
        try { if (File.Exists(StatusPath)) File.Delete(StatusPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { throw new InvalidOperationException("No se pudo liberar el modo de mantenimiento.", exception); }
        return true;
    }

    private static void Save(MaintenanceModeStatus status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)!);
        var temporary = $"{StatusPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(status));
        File.Move(temporary, StatusPath, true);
    }

    private async Task<bool> IsAdministratorAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var userId = await database.Sessions.AsNoTracking().Where(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow).Select(item => (Guid?)item.UserId).SingleOrDefaultAsync(cancellationToken);
        return userId is not null && await database.Users.AsNoTracking().AnyAsync(item => item.Id == userId && item.IsActive && item.IsAdministrator, cancellationToken);
    }
}
