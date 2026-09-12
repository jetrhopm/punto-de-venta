using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record LoginCommand(string UserName, string Password);
public sealed record LoginResult(Guid SessionId, string AccessToken, Guid UserId, Guid RegisterId, string DisplayName, bool IsAdministrator, DateTimeOffset ExpiresAtUtc, IReadOnlyList<string> Permissions);
public sealed record LoginAttempt(LoginResult? Session, string? FailureCode, string? FailureMessage);
public sealed record SessionDeviceValidationAttempt(bool IsValid, string? FailureCode, string? FailureMessage);
public sealed record TemporaryPermissionAuthorizationCommand(string UserName, string Password, string Permission);
public sealed record TemporaryPermissionAuthorizationResult(Guid? GrantId, DateTimeOffset ExpiresAtUtc, string AuthorizedBy);
public sealed record TemporaryPermissionAuthorizationAttempt(
    TemporaryPermissionAuthorizationResult? Authorization,
    string? FailureCode,
    string? FailureMessage);

public sealed class AuthenticationService(PosDbContext database, PasswordHasher<UserRecord> passwordHasher)
{
    public async Task<LoginResult?> LoginAsync(LoginCommand command, CancellationToken cancellationToken) =>
        (await LoginDetailedAsync(command, null, true, cancellationToken)).Session;

    public async Task<LoginAttempt> LoginDetailedAsync(LoginCommand command, string? deviceToken, bool isLocalConnection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.UserName) || string.IsNullOrEmpty(command.Password)) return FailedLogin("invalid_credentials", "El usuario o la contraseña son incorrectos.");
        var normalized = InitialSetupService.NormalizeUserName(command.UserName);
        var user = await database.Users.SingleOrDefaultAsync(item => item.NormalizedUserName == normalized && item.IsActive, cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password) == PasswordVerificationResult.Failed) return FailedLogin("invalid_credentials", "El usuario o la contraseña son incorrectos.");

        var station = await ResolveStationAsync(deviceToken, isLocalConnection, cancellationToken);
        if (station is null) return FailedLogin("device_required", "Esta computadora no está emparejada con una caja autorizada. Solicita al administrador un código de emparejamiento.");

        var activeElsewhere = await FindActiveSessionElsewhereAsync(user.Id, station.RegisterId, cancellationToken);
        if (activeElsewhere is not null)
        {
            return FailedLogin("session_active_elsewhere", $"{user.DisplayName} tiene una sesión activa en la caja {activeElsewhere}. Cierra sesión en esa caja antes de iniciar aquí.");
        }

        var openShiftElsewhere = await database.Shifts.AsNoTracking()
            .Where(item => item.UserId == user.Id && item.Status == "Open" && item.RegisterId != station.RegisterId)
            .Join(database.Registers.AsNoTracking(), shift => shift.RegisterId, register => register.Id, (_, register) => register.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(openShiftElsewhere))
        {
            return FailedLogin("shift_open_elsewhere", $"{user.DisplayName} dejó abierto el turno de la caja {openShiftElsewhere}. Debe cerrarlo antes de iniciar en otra caja.");
        }

        var staleTemporaryPermissions = await database.Permissions.IgnoreQueryFilters()
            .Where(item => item.UserId == user.Id && item.ExpiresAtUtc != null)
            .ToListAsync(cancellationToken);
        database.Permissions.RemoveRange(staleTemporaryPermissions);

        var sessionsForThisRegister = await database.Sessions
            .Where(item => item.UserId == user.Id && item.RegisterId == station.RegisterId && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var existingSession in sessionsForThisRegister) existingSession.RevokedAtUtc = DateTimeOffset.UtcNow;

        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, DeviceId = station.DeviceId, RegisterId = station.RegisterId, TokenHash = Hash(accessToken), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(8) };
        database.Sessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);
        IReadOnlyList<string> permissions = user.IsAdministrator
            ? Enum.GetNames<Pos.Domain.Permission>()
            : await database.Permissions.Where(item => item.UserId == user.Id && item.ExpiresAtUtc == null).Select(item => item.Code).ToListAsync(cancellationToken);
        return new LoginAttempt(new LoginResult(session.Id, accessToken, user.Id, station.RegisterId, user.DisplayName, user.IsAdministrator, session.ExpiresAtUtc, permissions), null, null);
    }

    public async Task<bool> LogoutAsync(string actorToken, CancellationToken cancellationToken)
    {
        var hash = Hash(actorToken ?? string.Empty);
        var session = await database.Sessions.SingleOrDefaultAsync(
            item => item.TokenHash == hash && item.RevokedAtUtc == null,
            cancellationToken);
        if (session is null) return false;

        var temporaryPermissions = await database.Permissions.IgnoreQueryFilters()
            .Where(item => item.UserId == session.UserId && item.ExpiresAtUtc != null)
            .ToListAsync(cancellationToken);
        database.Permissions.RemoveRange(temporaryPermissions);
        session.RevokedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SessionDeviceValidationAttempt> ValidateSessionDeviceAsync(string accessToken, string? deviceToken, bool isLocalConnection, CancellationToken cancellationToken)
    {
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(
            item => item.TokenHash == Hash(accessToken ?? string.Empty) && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow,
            cancellationToken);
        if (session is null) return new(false, "session_invalid", "La sesión ya no es válida. Inicia sesión nuevamente.");

        if (session.DeviceId is not Guid deviceId)
        {
            return isLocalConnection
                ? new(true, null, null)
                : new(false, "local_session_only", "La sesión de la caja principal sólo puede usarse desde la computadora servidor.");
        }

        if (string.IsNullOrWhiteSpace(deviceToken)) return new(false, "device_required", "Esta sesión requiere la identidad de la computadora emparejada.");
        var device = await database.Devices.SingleOrDefaultAsync(item =>
            item.Id == deviceId && item.RegisterId == session.RegisterId && item.IsActive && item.DeviceTokenHash == Hash(deviceToken),
            cancellationToken);
        if (device is not null && (device.LastSeenAtUtc is null || device.LastSeenAtUtc < DateTimeOffset.UtcNow.AddMinutes(-1)))
        {
            device.LastSeenAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
        }
        return device is null
            ? new(false, "device_mismatch", "La identidad de esta computadora no coincide con la sesión. Empareja nuevamente la caja o inicia sesión de nuevo.")
            : new(true, null, null);
    }

    public async Task<TemporaryPermissionAuthorizationResult?> GrantTemporaryPermissionAsync(
        string actorToken,
        TemporaryPermissionAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        var attempt = await GrantTemporaryPermissionDetailedAsync(actorToken, command, cancellationToken);
        return attempt.Authorization;
    }

    public async Task<TemporaryPermissionAuthorizationAttempt> GrantTemporaryPermissionDetailedAsync(
        string actorToken,
        TemporaryPermissionAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Pos.Domain.Permission>(command.Permission, ignoreCase: false, out _) ||
            string.IsNullOrWhiteSpace(command.UserName) || string.IsNullOrEmpty(command.Password))
        {
            return Failed("invalid_request", "La autorización solicitada no es válida.");
        }

        var actor = await GetActiveSessionUserAsync(actorToken, cancellationToken);
        if (actor is null)
        {
            return Failed("session_invalid", "La sesión actual ya no es válida. Inicia sesión de nuevo antes de solicitar autorización.");
        }

        var normalized = InitialSetupService.NormalizeUserName(command.UserName);
        var approver = await database.Users.SingleOrDefaultAsync(item => item.NormalizedUserName == normalized && item.IsActive, cancellationToken);
        if (approver is null || passwordHasher.VerifyHashedPassword(approver, approver.PasswordHash, command.Password) == PasswordVerificationResult.Failed)
        {
            return Failed("invalid_credentials", "El usuario que autoriza no existe, está desactivado o la contraseña es incorrecta.");
        }

        var approverCanAuthorize = approver.IsAdministrator || await database.Permissions
            .IgnoreQueryFilters()
            .AnyAsync(item => item.UserId == approver.Id && item.Code == command.Permission && item.ExpiresAtUtc == null, cancellationToken);
        if (!approverCanAuthorize)
        {
            return Failed("permission_missing", "El usuario elegido no tiene el permiso requerido para autorizar esta acción.");
        }

        var current = await database.Permissions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.UserId == actor.Id && item.Code == command.Permission, cancellationToken);
        if (current is { ExpiresAtUtc: null })
        {
            return Succeeded(new TemporaryPermissionAuthorizationResult(null, DateTimeOffset.UtcNow, approver.DisplayName));
        }

        // El permiso cubre el tiempo de contar efectivo y se revoca al terminar la acción.
        // La expiración es únicamente el respaldo de seguridad si el cliente pierde conexión.
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10);
        if (current is null)
        {
            current = new PermissionRecord
            {
                Id = Guid.NewGuid(),
                UserId = actor.Id,
                Code = command.Permission,
                GrantedByUserId = approver.Id,
                ExpiresAtUtc = expiresAtUtc
            };
            database.Permissions.Add(current);
        }
        else
        {
            current.GrantedByUserId = approver.Id;
            current.ExpiresAtUtc = expiresAtUtc;
        }

        await database.SaveChangesAsync(cancellationToken);
        return Succeeded(new TemporaryPermissionAuthorizationResult(current.Id, expiresAtUtc, approver.DisplayName));
    }

    private static TemporaryPermissionAuthorizationAttempt Succeeded(TemporaryPermissionAuthorizationResult authorization) =>
        new(authorization, null, null);

    private static TemporaryPermissionAuthorizationAttempt Failed(string code, string message) =>
        new(null, code, message);

    private static LoginAttempt FailedLogin(string code, string message) => new(null, code, message);

    private async Task<StationIdentity?> ResolveStationAsync(string? deviceToken, bool isLocalConnection, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(deviceToken))
        {
            var device = await database.Devices.AsNoTracking()
                .SingleOrDefaultAsync(item => item.DeviceTokenHash == Hash(deviceToken) && item.IsActive, cancellationToken);
            if (device is null) return null;
            var register = await database.Registers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == device.RegisterId && item.IsActive, cancellationToken);
            return register is null ? null : new StationIdentity(device.Id, register.Id);
        }

        if (!isLocalConnection) return null;
        var localRegister = await database.Registers.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => database.Devices.Any(device => device.RegisterId == item.Id) ? 1 : 0)
            .ThenBy(item => item.Name)
            .FirstOrDefaultAsync(cancellationToken);
        return localRegister is null ? null : new StationIdentity(null, localRegister.Id);
    }

    private async Task<string?> FindActiveSessionElsewhereAsync(Guid userId, Guid registerId, CancellationToken cancellationToken) =>
        await database.Sessions.AsNoTracking()
            .Where(item => item.UserId == userId && item.RegisterId != null && item.RegisterId != registerId && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .Join(database.Registers.AsNoTracking(), session => session.RegisterId, register => register.Id, (_, register) => register.Name)
            .FirstOrDefaultAsync(cancellationToken);

    private sealed record StationIdentity(Guid? DeviceId, Guid RegisterId);

    public async Task<bool> RevokeTemporaryPermissionAsync(string actorToken, Guid grantId, CancellationToken cancellationToken)
    {
        var actor = await GetActiveSessionUserAsync(actorToken, cancellationToken);
        if (actor is null) return false;
        var grant = await database.Permissions.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == grantId && item.UserId == actor.Id && item.ExpiresAtUtc != null, cancellationToken);
        if (grant is null) return false;
        database.Permissions.Remove(grant);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<UserRecord?> GetActiveSessionUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token ?? string.Empty);
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        return session is null ? null : await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
