using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record OpenShiftCommand(Guid RegisterId, decimal InitialCash);
public sealed record OpenShiftResult(Guid ShiftId, Guid RegisterId, Guid UserId, decimal InitialCash, DateTimeOffset OpenedAtUtc);
public sealed record CurrentShiftResult(Guid ShiftId, Guid RegisterId, Guid UserId, decimal InitialCash, DateTimeOffset OpenedAtUtc);
public sealed record RegisterAvailabilityResult(Guid RegisterId, string RegisterName, string? OpenShiftUserName, DateTimeOffset? OpenedAtUtc);

public sealed class ShiftService(PosDbContext database)
{
    public async Task<CurrentShiftResult?> CurrentAsync(string accessToken, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(accessToken, cancellationToken);
        if (session is null) return null;
        var shift = await database.Shifts.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == session.UserId && item.Status == "Open", cancellationToken);
        return shift is null ? null : new CurrentShiftResult(shift.Id, shift.RegisterId, shift.UserId, shift.InitialCash, shift.OpenedAtUtc);
    }

    public async Task<RegisterAvailabilityResult?> GetActiveRegisterAsync(CancellationToken cancellationToken)
    {
        var register = await database.Registers.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new { item.Id, item.Name })
            .FirstOrDefaultAsync(cancellationToken);
        return register is null ? null : await GetRegisterAvailabilityAsync(register.Id, cancellationToken);
    }

    public async Task<RegisterAvailabilityResult?> GetRegisterAvailabilityAsync(Guid registerId, CancellationToken cancellationToken)
    {
        if (registerId == Guid.Empty) return null;
        var register = await database.Registers.AsNoTracking()
            .Where(item => item.Id == registerId)
            .Select(item => new { item.Id, item.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (register is null) return null;
        var openShift = await database.Shifts.AsNoTracking()
            .Where(item => item.RegisterId == register.Id && item.Status == "Open")
            .Join(database.Users.AsNoTracking(), shift => shift.UserId, user => user.Id,
                (shift, user) => new { user.DisplayName, shift.OpenedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        return new RegisterAvailabilityResult(register.Id, register.Name, openShift?.DisplayName, openShift?.OpenedAtUtc);
    }

    public async Task<OpenShiftResult?> OpenAsync(string accessToken, OpenShiftCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || command.RegisterId == Guid.Empty || command.InitialCash < 0m) return null;
        var session = await GetSessionAsync(accessToken, cancellationToken);
        if (session is null) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var openShift = await database.Shifts.AsNoTracking()
            .Where(item => item.RegisterId == command.RegisterId && item.Status == "Open")
            .Join(database.Users.AsNoTracking(), shift => shift.UserId, user => user.Id,
                (shift, user) => new { user.DisplayName, shift.OpenedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (openShift is not null)
        {
            throw new InvalidOperationException($"La caja ya tiene un turno abierto por {openShift.DisplayName} desde {openShift.OpenedAtUtc.LocalDateTime:g}. Inicia sesión con ese usuario para continuar o realiza el corte antes de cambiar de cajero.");
        }
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
