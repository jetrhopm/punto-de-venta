using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record OpenShiftCommand(Guid RegisterId, decimal InitialCash);
public sealed record OpenShiftResult(Guid ShiftId, Guid RegisterId, Guid UserId, decimal InitialCash, DateTimeOffset OpenedAtUtc);
public sealed record CurrentShiftResult(Guid ShiftId, Guid RegisterId, Guid UserId, decimal InitialCash, DateTimeOffset OpenedAtUtc);

public sealed class ShiftService(PosDbContext database)
{
    public async Task<CurrentShiftResult?> CurrentAsync(string accessToken, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(accessToken, cancellationToken);
        if (session is null) return null;
        var shift = await database.Shifts.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == session.UserId && item.Status == "Open", cancellationToken);
        return shift is null ? null : new CurrentShiftResult(shift.Id, shift.RegisterId, shift.UserId, shift.InitialCash, shift.OpenedAtUtc);
    }

    public async Task<OpenShiftResult?> OpenAsync(string accessToken, OpenShiftCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || command.RegisterId == Guid.Empty || command.InitialCash < 0m) return null;
        var session = await GetSessionAsync(accessToken, cancellationToken);
        if (session is null) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await database.Shifts.AnyAsync(item => item.RegisterId == command.RegisterId && item.Status == "Open", cancellationToken)) throw new InvalidOperationException("La caja ya tiene un turno abierto.");
        var shift = new ShiftRecord { Id = Guid.NewGuid(), RegisterId = command.RegisterId, UserId = session.UserId, InitialCash = decimal.Round(command.InitialCash, 2), OpenedAtUtc = DateTimeOffset.UtcNow };
        database.Shifts.Add(shift);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OpenShiftResult(shift.Id, shift.RegisterId, shift.UserId, shift.InitialCash, shift.OpenedAtUtc);
    }

    private async Task<SessionRecord?> GetSessionAsync(string accessToken, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken ?? string.Empty)));
        return await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
    }
}
