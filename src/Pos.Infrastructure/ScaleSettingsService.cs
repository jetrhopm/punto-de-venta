using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record ScaleSettingsResult(bool Enabled, string Port, int BaudRate, string Parity, int DataBits, string StopBits, string Terminator, string Unit, int ReadTimeoutMs);
public sealed record SetScaleSettingsCommand(bool Enabled, string Port, int BaudRate, string Parity, int DataBits, string StopBits, string Terminator, string Unit, int ReadTimeoutMs);

public sealed class ScaleSettingsService(PosDbContext database)
{
    public async Task<ScaleSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (!await HasSessionAsync(token, cancellationToken)) return null;
        return ToResult(await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken));
    }

    public async Task<ScaleSettingsResult?> UpdateAsync(string token, SetScaleSettingsCommand command, CancellationToken cancellationToken)
    {
        if (!await HasConfigurePermissionAsync(token, cancellationToken)) return null;
        Validate(command);
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        store.ScaleEnabled = command.Enabled;
        store.ScalePort = command.Port.Trim().ToUpperInvariant();
        store.ScaleBaudRate = command.BaudRate;
        store.ScaleParity = command.Parity.Trim();
        store.ScaleDataBits = command.DataBits;
        store.ScaleStopBits = command.StopBits.Trim();
        store.ScaleTerminator = command.Terminator.Trim().ToUpperInvariant();
        store.ScaleUnit = command.Unit.Trim();
        store.ScaleReadTimeoutMs = command.ReadTimeoutMs;
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(store);
    }

    private async Task<bool> HasSessionAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return await database.Sessions.AnyAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
    }

    private async Task<bool> HasConfigurePermissionAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return false;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken);
    }

    private static void Validate(SetScaleSettingsCommand command)
    {
        if (command.Port.Length > 20 || command.Parity.Length > 10 || command.StopBits.Length > 10 || command.Terminator.Length > 10 || command.Unit.Length > 20) throw new ArgumentException("La configuración de la báscula contiene un valor demasiado largo.");
        if (command.Enabled && string.IsNullOrWhiteSpace(command.Port)) throw new ArgumentException("Selecciona el puerto COM de la báscula.");
        if (command.BaudRate is not (1200 or 2400 or 4800 or 9600 or 19200 or 38400 or 57600 or 115200)) throw new ArgumentException("La velocidad de la báscula no es válida.");
        if (command.Parity is not ("None" or "Even" or "Odd")) throw new ArgumentException("La paridad no es válida.");
        if (command.DataBits is not (7 or 8)) throw new ArgumentException("Los bits de datos deben ser 7 u 8.");
        if (command.StopBits is not ("One" or "Two")) throw new ArgumentException("Los bits de parada no son válidos.");
        if (command.Terminator is not ("CRLF" or "CR" or "LF")) throw new ArgumentException("El terminador no es válido.");
        if (command.Unit is not ("Kilogramo" or "Gramo" or "Libra")) throw new ArgumentException("La unidad de la báscula no es válida.");
        if (command.ReadTimeoutMs is < 200 or > 5000) throw new ArgumentException("El tiempo de espera debe estar entre 200 y 5000 milisegundos.");
    }

    private static ScaleSettingsResult ToResult(StoreRecord store) => new(store.ScaleEnabled, store.ScalePort, store.ScaleBaudRate, store.ScaleParity, store.ScaleDataBits, store.ScaleStopBits, store.ScaleTerminator, store.ScaleUnit, store.ScaleReadTimeoutMs);
}
