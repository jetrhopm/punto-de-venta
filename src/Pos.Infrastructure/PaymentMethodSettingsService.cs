using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record PaymentMethodSettingsResult(bool CashEnabled, bool CardEnabled, bool TransferEnabled, bool CreditEnabled);
public sealed record SetPaymentMethodSettingsCommand(bool CashEnabled, bool CardEnabled, bool TransferEnabled, bool CreditEnabled);

public sealed class PaymentMethodSettingsService(PosDbContext database)
{
    public async Task<PaymentMethodSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        // Las formas activas se aplican a toda la tienda; cualquier cajero autenticado
        // debe poder leerlas al abrir la ventana de cobro.
        if (await AuthenticatedUserIdAsync(token, cancellationToken) is null) return null;
        var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        return ToResult(store);
    }

    public async Task<PaymentMethodSettingsResult?> UpdateAsync(string token, SetPaymentMethodSettingsCommand command, CancellationToken cancellationToken)
    {
        if (!await CanConfigureAsync(token, cancellationToken)) return null;
        if (!command.CashEnabled && !command.CardEnabled && !command.TransferEnabled && !command.CreditEnabled)
            throw new ArgumentException("Activa al menos una forma de pago.");
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        store.CashPaymentEnabled = command.CashEnabled;
        store.CardPaymentEnabled = command.CardEnabled;
        store.TransferPaymentEnabled = command.TransferEnabled;
        store.CreditPaymentEnabled = command.CreditEnabled;
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(store);
    }

    private static PaymentMethodSettingsResult ToResult(StoreRecord store) => new(store.CashPaymentEnabled, store.CardPaymentEnabled, store.TransferPaymentEnabled, store.CreditPaymentEnabled);

    private async Task<bool> CanConfigureAsync(string token, CancellationToken cancellationToken)
    {
        var userId = await AuthenticatedUserIdAsync(token, cancellationToken);
        if (userId is null) return false;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == userId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken);
    }

    private async Task<Guid?> AuthenticatedUserIdAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        return await database.Sessions.AsNoTracking()
            .Where(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .Select(item => (Guid?)item.UserId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
