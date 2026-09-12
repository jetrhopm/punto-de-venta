using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record RegisterStatusResult(
    Guid Id,
    string Name,
    bool IsActive,
    string? DeviceName,
    DateTimeOffset? LastConnectionAtUtc,
    string? ActiveUser,
    string? OpenShiftUser,
    DateTimeOffset? ShiftOpenedAtUtc,
    bool IsPaired);

public sealed record RenameRegisterCommand(string Name);
public sealed record SetRegisterActiveCommand(bool IsActive);

public sealed class RegisterAdministrationService(PosDbContext database)
{
    public async Task<IReadOnlyList<RegisterStatusResult>?> ListAsync(string token, CancellationToken cancellationToken)
    {
        var administrator = await AdministratorAsync(token, cancellationToken);
        if (administrator is null) return null;
        var storeId = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).Select(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        if (storeId == Guid.Empty) return [];

        var registers = await database.Registers.AsNoTracking().Where(item => item.StoreId == storeId).OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var devices = await database.Devices.AsNoTracking().Where(item => item.StoreId == storeId).ToListAsync(cancellationToken);
        var activeSessions = await database.Sessions.AsNoTracking()
            .Where(item => item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow && item.RegisterId != null)
            .Join(database.Users.AsNoTracking(), session => session.UserId, user => user.Id, (session, user) => new { session.RegisterId, user.DisplayName, session.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var openShifts = await database.Shifts.AsNoTracking().Where(item => item.Status == "Open")
            .Join(database.Users.AsNoTracking(), shift => shift.UserId, user => user.Id, (shift, user) => new { shift.RegisterId, user.DisplayName, shift.OpenedAtUtc })
            .ToListAsync(cancellationToken);

        return registers.Select(register =>
        {
            var device = devices.Where(item => item.RegisterId == register.Id).OrderByDescending(item => item.LastSeenAtUtc ?? item.CreatedAtUtc).FirstOrDefault();
            var session = activeSessions.Where(item => item.RegisterId == register.Id).OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault();
            var shift = openShifts.Where(item => item.RegisterId == register.Id).OrderByDescending(item => item.OpenedAtUtc).FirstOrDefault();
            return new RegisterStatusResult(register.Id, register.Name, register.IsActive, device?.Name, device?.LastSeenAtUtc, session?.DisplayName, shift?.DisplayName, shift?.OpenedAtUtc, device is not null);
        }).ToArray();
    }

    public async Task<RegisterStatusResult?> RenameAsync(string token, Guid registerId, RenameRegisterCommand command, CancellationToken cancellationToken)
    {
        var administrator = await AdministratorAsync(token, cancellationToken);
        if (administrator is null) return null;
        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 80) throw new ArgumentException("El nombre de caja debe tener entre 2 y 80 caracteres.");
        var register = await database.Registers.SingleOrDefaultAsync(item => item.Id == registerId, cancellationToken) ?? throw new KeyNotFoundException("La caja no existe.");
        var duplicate = await database.Registers.AnyAsync(item => item.StoreId == register.StoreId && item.Id != register.Id && item.Name.ToUpper() == name.ToUpper(), cancellationToken);
        if (duplicate) throw new ArgumentException("Ya existe otra caja con ese nombre.");
        register.Name = name;
        await database.SaveChangesAsync(cancellationToken);
        return await GetAsync(register.Id, cancellationToken);
    }

    public async Task<RegisterStatusResult?> SetActiveAsync(string token, Guid registerId, SetRegisterActiveCommand command, CancellationToken cancellationToken)
    {
        var administrator = await AdministratorAsync(token, cancellationToken);
        if (administrator is null) return null;
        var register = await database.Registers.SingleOrDefaultAsync(item => item.Id == registerId, cancellationToken) ?? throw new KeyNotFoundException("La caja no existe.");
        if (!command.IsActive && await database.Shifts.AnyAsync(item => item.RegisterId == registerId && item.Status == "Open", cancellationToken))
            throw new InvalidOperationException("No se puede desactivar una caja con turno abierto. Cierra el turno y realiza el corte antes de desactivarla.");

        register.IsActive = command.IsActive;
        var devices = await database.Devices.Where(item => item.RegisterId == registerId).ToListAsync(cancellationToken);
        foreach (var device in devices) device.IsActive = command.IsActive;
        if (!command.IsActive)
        {
            var sessions = await database.Sessions.Where(item => item.RegisterId == registerId && item.RevokedAtUtc == null).ToListAsync(cancellationToken);
            foreach (var session in sessions) session.RevokedAtUtc = DateTimeOffset.UtcNow;
        }
        await database.SaveChangesAsync(cancellationToken);
        return await GetAsync(register.Id, cancellationToken);
    }

    public async Task<bool> CanAdministerAsync(string token, CancellationToken cancellationToken) => await AdministratorAsync(token, cancellationToken) is not null;

    private async Task<RegisterStatusResult?> GetAsync(Guid registerId, CancellationToken cancellationToken)
    {
        var register = await database.Registers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == registerId, cancellationToken);
        if (register is null) return null;
        var device = await database.Devices.AsNoTracking().Where(item => item.RegisterId == registerId).OrderByDescending(item => item.LastSeenAtUtc ?? item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var session = await database.Sessions.AsNoTracking().Where(item => item.RegisterId == registerId && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .Join(database.Users.AsNoTracking(), item => item.UserId, user => user.Id, (item, user) => new { user.DisplayName, item.CreatedAtUtc }).OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var shift = await database.Shifts.AsNoTracking().Where(item => item.RegisterId == registerId && item.Status == "Open")
            .Join(database.Users.AsNoTracking(), item => item.UserId, user => user.Id, (item, user) => new { user.DisplayName, item.OpenedAtUtc }).OrderByDescending(item => item.OpenedAtUtc).FirstOrDefaultAsync(cancellationToken);
        return new RegisterStatusResult(register.Id, register.Name, register.IsActive, device?.Name, device?.LastSeenAtUtc, session?.DisplayName, shift?.DisplayName, shift?.OpenedAtUtc, device is not null);
    }

    private async Task<Guid?> AdministratorAsync(string token, CancellationToken cancellationToken)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == tokenHash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        return user?.IsAdministrator == true ? user.Id : null;
    }
}
