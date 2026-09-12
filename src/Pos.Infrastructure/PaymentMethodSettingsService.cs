using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record PaymentMethodSettingsResult(bool CashEnabled, bool CardEnabled, bool TransferEnabled, bool CreditEnabled, string RegisterName);
public sealed record SetPaymentMethodSettingsCommand(bool CashEnabled, bool CardEnabled, bool TransferEnabled, bool CreditEnabled);

public sealed class PaymentMethodSettingsService(PosDbContext database)
{
    public async Task<PaymentMethodSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(token, cancellationToken);
        if (session is null) return null;
        return ToResult(session.Register);
    }

    public async Task<PaymentMethodSettingsResult?> UpdateAsync(string token, SetPaymentMethodSettingsCommand command, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(token, cancellationToken);
        if (session is null || !await CanConfigureAsync(session.UserId, session.IsAdministrator, cancellationToken)) return null;
        if (!command.CashEnabled && !command.CardEnabled && !command.TransferEnabled && !command.CreditEnabled)
            throw new ArgumentException("Activa al menos una forma de pago.");
        var register = await database.Registers.SingleAsync(item => item.Id == session.Register.Id && item.IsActive, cancellationToken);
        register.CashPaymentEnabled = command.CashEnabled;
        register.CardPaymentEnabled = command.CardEnabled;
        register.TransferPaymentEnabled = command.TransferEnabled;
        register.CreditPaymentEnabled = command.CreditEnabled;
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(register);
    }

    private static PaymentMethodSettingsResult ToResult(RegisterRecord register) => new(register.CashPaymentEnabled, register.CardPaymentEnabled, register.TransferPaymentEnabled, register.CreditPaymentEnabled, register.Name);

    private async Task<bool> CanConfigureAsync(Guid userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        return isAdministrator || await database.Permissions.AnyAsync(item => item.UserId == userId && item.Code == "ConfigureStore", cancellationToken);
    }

    private async Task<PaymentSettingsSession?> GetSessionAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        return await database.Sessions.AsNoTracking()
            .Where(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .Join(database.Users.AsNoTracking(), session => session.UserId, user => user.Id, (session, user) => new { session, user })
            .Where(item => item.session.RegisterId != null && item.user.IsActive)
            .Join(database.Registers.AsNoTracking(), item => item.session.RegisterId, register => register.Id, (item, register) => new PaymentSettingsSession(item.user.Id, item.user.IsAdministrator, register))
            .Where(item => item.Register.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private sealed record PaymentSettingsSession(Guid UserId, bool IsAdministrator, RegisterRecord Register);
}
