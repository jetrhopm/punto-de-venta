using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record CurrencySettingsResult(string CurrencySymbol);
public sealed record SetCurrencySettingsCommand(string CurrencySymbol);

public sealed class CurrencySettingsService(PosDbContext database)
{
    public async Task<CurrencySettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;
        var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        return new CurrencySettingsResult(Normalize(store.CurrencySymbol));
    }

    public async Task<CurrencySettingsResult?> UpdateAsync(string token, SetCurrencySettingsCommand command, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;
        var symbol = Normalize(command.CurrencySymbol);
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        store.CurrencySymbol = symbol;
        await database.SaveChangesAsync(cancellationToken);
        return new CurrencySettingsResult(symbol);
    }

    private static string Normalize(string? symbol)
    {
        var value = symbol?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 5 || value.Any(char.IsControl))
            throw new ArgumentException("Escribe un símbolo de moneda de uno a cinco caracteres.");
        return value;
    }

    private async Task<bool> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return false;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken);
    }
}
