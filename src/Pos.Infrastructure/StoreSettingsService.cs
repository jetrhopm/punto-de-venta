using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record StoreSettingsCommand(string Name, string BusinessType, string LegalName, string TaxId, string Address, string Phone, string TimeZoneId);
public sealed record StoreSettingsResult(Guid Id, string Name, string BusinessType, string LegalName, string TaxId, string Address, string Phone, string TimeZoneId);

public sealed class StoreSettingsService(PosDbContext database)
{
    public async Task<StoreSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, cancellationToken) is null) return null;
        return ToResult(await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken));
    }

    public async Task<StoreSettingsResult?> UpdateAsync(string token, StoreSettingsCommand command, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, cancellationToken) is null) return null;
        Validate(command);
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        store.Name = command.Name.Trim();
        store.BusinessType = command.BusinessType.Trim();
        store.LegalName = command.LegalName.Trim();
        store.TaxId = command.TaxId.Trim().ToUpperInvariant();
        store.Address = command.Address.Trim();
        store.Phone = command.Phone.Trim();
        store.TimeZoneId = command.TimeZoneId.Trim();
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(store);
    }

    private static void Validate(StoreSettingsCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 160) throw new ArgumentException("El nombre comercial es obligatorio y admite hasta 160 caracteres.");
        if (string.IsNullOrWhiteSpace(command.BusinessType) || command.BusinessType.Trim().Length > 80) throw new ArgumentException("El giro es obligatorio y admite hasta 80 caracteres.");
        if (command.LegalName.Trim().Length > 200 || command.TaxId.Trim().Length > 20 || command.Address.Trim().Length > 300 || command.Phone.Trim().Length > 30) throw new ArgumentException("Uno de los datos de tienda supera la longitud permitida.");
        if (string.IsNullOrWhiteSpace(command.TimeZoneId) || command.TimeZoneId.Trim().Length > 100) throw new ArgumentException("La zona horaria es obligatoria.");
    }

    private async Task<Guid?> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken) ? user.Id : null;
    }

    private static StoreSettingsResult ToResult(StoreRecord store) => new(store.Id, store.Name, store.BusinessType, store.LegalName, store.TaxId, store.Address, store.Phone, store.TimeZoneId);
}
