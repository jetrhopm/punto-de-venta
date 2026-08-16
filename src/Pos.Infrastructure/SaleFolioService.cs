using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SaleFolioSettingsResult(long NextFolio, long LastIssuedFolio);
public sealed record SetNextSaleFolioCommand(long NextFolio);

public sealed class SaleFolioService(PosDbContext database)
{
    public async Task<SaleFolioSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;
        var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        var last = await database.Sales.AsNoTracking().Select(item => (long?)item.Folio).MaxAsync(cancellationToken) ?? 0L;
        return new SaleFolioSettingsResult(store.NextSaleFolio, last);
    }

    public async Task<SaleFolioSettingsResult?> SetNextAsync(string token, SetNextSaleFolioCommand command, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;
        if (command.NextFolio < 1) throw new ArgumentException("El siguiente folio debe ser un número mayor a cero.");

        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        if (command.NextFolio < store.NextSaleFolio)
            throw new ArgumentException($"No se puede retroceder el consecutivo. El siguiente folio disponible es {store.NextSaleFolio:N0}.");

        store.NextSaleFolio = command.NextFolio;
        await database.SaveChangesAsync(cancellationToken);
        var last = await database.Sales.AsNoTracking().Select(item => (long?)item.Folio).MaxAsync(cancellationToken) ?? 0L;
        return new SaleFolioSettingsResult(store.NextSaleFolio, last);
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
