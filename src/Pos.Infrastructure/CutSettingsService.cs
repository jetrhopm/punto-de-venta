using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record CutSettingsResult(bool RequireCashCountOnClose, bool AutoAdjustCashDifference, bool CashLimitEnabled, decimal CashLimit, string CashLimitMessage);
public sealed record SetCutSettingsCommand(bool RequireCashCountOnClose, bool AutoAdjustCashDifference, bool CashLimitEnabled, decimal CashLimit, string CashLimitMessage);

public sealed class CutSettingsService(PosDbContext database)
{
    public async Task<CutSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;
        return ToResult(await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken));
    }

    public async Task<CutSettingsResult?> UpdateAsync(string token, SetCutSettingsCommand command, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;
        if (command.CashLimit < 0m || command.CashLimit > 9_999_999m) throw new ArgumentException("El límite de efectivo debe ser un importe válido.");
        if (command.CashLimitEnabled && command.CashLimit <= 0m) throw new ArgumentException("Indica un límite de efectivo mayor a cero.");
        if (command.CashLimitMessage?.Trim().Length is > 300) throw new ArgumentException("El mensaje admite hasta 300 caracteres.");
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        store.RequireCashCountOnClose = command.RequireCashCountOnClose;
        store.AutoAdjustCashDifference = command.RequireCashCountOnClose && command.AutoAdjustCashDifference;
        store.CashLimitEnabled = command.CashLimitEnabled;
        store.CashLimit = decimal.Round(command.CashLimit, 2);
        store.CashLimitMessage = string.IsNullOrWhiteSpace(command.CashLimitMessage) ? "Realiza un retiro de efectivo (F8); se superó el límite permitido en caja." : command.CashLimitMessage.Trim();
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(store);
    }

    private static CutSettingsResult ToResult(StoreRecord store) => new(store.RequireCashCountOnClose, store.AutoAdjustCashDifference, store.CashLimitEnabled, store.CashLimit, store.CashLimitMessage);
    private async Task<bool> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return false;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken);
    }
}
