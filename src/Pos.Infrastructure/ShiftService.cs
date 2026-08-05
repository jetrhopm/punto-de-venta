using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record OpenShiftCommand(Guid RegisterId, decimal InitialCash);
public sealed record OpenShiftResult(Guid ShiftId, Guid RegisterId, Guid UserId, decimal InitialCash, DateTimeOffset OpenedAtUtc);

public sealed class ShiftService(PosDbContext database)
{
    public async Task<OpenShiftResult?> OpenAsync(string accessToken, OpenShiftCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || command.RegisterId == Guid.Empty || command.InitialCash < 0m) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        var allowed = user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == session.UserId && item.Code == nameof(Pos.Domain.Permission.OpenShift), cancellationToken);
        if (!allowed) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await database.Shifts.AnyAsync(item => item.RegisterId == command.RegisterId && item.Status == "Open", cancellationToken)) throw new InvalidOperationException("La caja ya tiene un turno abierto.");
        var shift = new ShiftRecord { Id = Guid.NewGuid(), RegisterId = command.RegisterId, UserId = session.UserId, InitialCash = decimal.Round(command.InitialCash, 2), OpenedAtUtc = DateTimeOffset.UtcNow };
        database.Shifts.Add(shift);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OpenShiftResult(shift.Id, shift.RegisterId, shift.UserId, shift.InitialCash, shift.OpenedAtUtc);
    }
}
