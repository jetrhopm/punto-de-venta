using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record OpenShiftCommand(decimal InitialCash);
public sealed record OpenShiftResult(Guid ShiftId, Guid RegisterId, Guid UserId, decimal InitialCash, DateTimeOffset OpenedAtUtc);
public sealed record CurrentShiftResult(Guid ShiftId, Guid RegisterId, Guid UserId, decimal InitialCash, DateTimeOffset OpenedAtUtc);
public sealed record PendingShiftHandoffResult(Guid HandoffId, string FromUser, decimal ExpectedCash, decimal DeliveredCash, decimal Difference, DateTimeOffset DeliveredAtUtc);
public sealed record ReceiveShiftHandoffCommand(Guid HandoffId, decimal ReceivedCash);
public sealed record OpenRegisterShiftConflict(string OpenedBy, DateTimeOffset OpenedAtUtc);

public sealed class RegisterShiftAlreadyOpenException(OpenRegisterShiftConflict conflict)
    : InvalidOperationException("La caja ya tiene un turno abierto.")
{
    public OpenRegisterShiftConflict Conflict { get; } = conflict;
}

public sealed class ShiftService(PosDbContext database)
{
    public async Task<CurrentShiftResult?> CurrentAsync(string accessToken, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(accessToken, cancellationToken);
        if (session is null) return null;
        if (session.RegisterId is not Guid registerId) return null;
        var shift = await database.Shifts.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == session.UserId && item.RegisterId == registerId && item.Status == "Open", cancellationToken);
        return shift is null ? null : new CurrentShiftResult(shift.Id, shift.RegisterId, shift.UserId, shift.InitialCash, shift.OpenedAtUtc);
    }

    public async Task<OpenShiftResult?> OpenAsync(string accessToken, OpenShiftCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || command.InitialCash < 0m) return null;
        var session = await GetSessionAsync(accessToken, cancellationToken);
        if (session?.RegisterId is not Guid registerId) return null;
        var registerIsActive = await database.Registers.AsNoTracking().AnyAsync(item => item.Id == registerId && item.IsActive, cancellationToken);
        if (!registerIsActive) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await database.ShiftHandoffs.AnyAsync(item => item.RegisterId == registerId && item.Status == "Pending", cancellationToken))
            throw new InvalidOperationException("Hay una entrega de turno pendiente. El siguiente cajero debe contar y confirmar el efectivo recibido.");
        var existingShift = await (
            from openShift in database.Shifts.AsNoTracking()
            join user in database.Users.AsNoTracking() on openShift.UserId equals user.Id
            where openShift.RegisterId == registerId && openShift.Status == "Open"
            select new OpenRegisterShiftConflict(user.DisplayName, openShift.OpenedAtUtc)
        ).SingleOrDefaultAsync(cancellationToken);
        if (existingShift is not null) throw new RegisterShiftAlreadyOpenException(existingShift);
        var shift = new ShiftRecord { Id = Guid.NewGuid(), RegisterId = registerId, UserId = session.UserId, InitialCash = decimal.Round(command.InitialCash, 2), OpenedAtUtc = DateTimeOffset.UtcNow };
        database.Shifts.Add(shift);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OpenShiftResult(shift.Id, shift.RegisterId, shift.UserId, shift.InitialCash, shift.OpenedAtUtc);
    }

    public async Task<PendingShiftHandoffResult?> PendingHandoffAsync(string accessToken, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(accessToken, cancellationToken);
        if (session?.RegisterId is not Guid registerId) return null;
        return await (
            from handoff in database.ShiftHandoffs.AsNoTracking()
            join shift in database.Shifts.AsNoTracking() on handoff.FromShiftId equals shift.Id
            join user in database.Users.AsNoTracking() on shift.UserId equals user.Id
            where handoff.RegisterId == registerId && handoff.Status == "Pending"
            select new PendingShiftHandoffResult(handoff.Id, user.DisplayName, handoff.ExpectedCash, handoff.DeliveredCash, decimal.Round(handoff.DeliveredCash - handoff.ExpectedCash, 2), handoff.CreatedAtUtc)
        ).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<OpenShiftResult?> ReceiveHandoffAsync(string accessToken, ReceiveShiftHandoffCommand command, CancellationToken cancellationToken)
    {
        if (command.HandoffId == Guid.Empty || command.ReceivedCash < 0m) throw new ArgumentException("Captura el efectivo recibido para abrir el turno.");
        var session = await GetSessionAsync(accessToken, cancellationToken);
        if (session?.RegisterId is not Guid registerId) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var handoff = await database.ShiftHandoffs.SingleOrDefaultAsync(item => item.Id == command.HandoffId && item.RegisterId == registerId && item.Status == "Pending", cancellationToken);
        if (handoff is null) throw new InvalidOperationException("La entrega de turno ya fue confirmada o no corresponde a esta caja.");
        if (await database.Shifts.AnyAsync(item => item.RegisterId == registerId && item.Status == "Open", cancellationToken))
            throw new InvalidOperationException("La caja ya tiene un turno abierto.");
        var shift = new ShiftRecord { Id = Guid.NewGuid(), RegisterId = registerId, UserId = session.UserId, InitialCash = decimal.Round(command.ReceivedCash, 2), OpenedAtUtc = DateTimeOffset.UtcNow };
        handoff.ToShiftId = shift.Id;
        handoff.ReceivedCash = shift.InitialCash;
        handoff.ReceivedAtUtc = shift.OpenedAtUtc;
        handoff.Status = "Received";
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
