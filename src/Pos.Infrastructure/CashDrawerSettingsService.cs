using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record CashDrawerSettingsResult(bool Enabled, string PrinterName, string Model, string Port);
public sealed record SetCashDrawerSettingsCommand(bool Enabled, string PrinterName, string Model, string Port);

public sealed class CashDrawerSettingsService(PosDbContext database)
{
    public async Task<CashDrawerSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (!await HasSessionAsync(token, cancellationToken)) return null;
        return ToResult(await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken));
    }

    public async Task<CashDrawerSettingsResult?> UpdateAsync(string token, SetCashDrawerSettingsCommand command, CancellationToken cancellationToken)
    {
        if (!await HasConfigurePermissionAsync(token, cancellationToken)) return null;
        Validate(command);
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        store.CashDrawerEnabled = command.Enabled;
        store.CashDrawerPrinterName = command.PrinterName.Trim();
        store.CashDrawerModel = command.Model.Trim();
        store.CashDrawerPort = command.Port.Trim().ToUpperInvariant();
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

    private static void Validate(SetCashDrawerSettingsCommand command)
    {
        if (command.PrinterName.Length > 260 || command.Model.Length > 80 || command.Port.Length > 20) throw new ArgumentException("La configuración del cajón contiene un valor demasiado largo.");
        if (command.Enabled && string.IsNullOrWhiteSpace(command.PrinterName)) throw new ArgumentException("Selecciona la impresora de Windows conectada al cajón.");
        if (!new[] { "PrinterPulse", "EpsonDrawer1", "EpsonDrawer2", "StarDrawer1", "StarDrawer2", "Generic" }.Contains(command.Model, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("El modelo de cajón seleccionado no es válido.");
        if (!new[] { "USB", "LPT1", "LPT2", "LPT3", "COM1", "COM2", "COM3", "COM4" }.Contains(command.Port, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("El puerto seleccionado no es válido.");
    }

    private static CashDrawerSettingsResult ToResult(StoreRecord store) => new(store.CashDrawerEnabled, store.CashDrawerPrinterName, store.CashDrawerModel, store.CashDrawerPort);
}
