using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record LoginCommand(string UserName, string Password);
public sealed record LoginResult(Guid SessionId, string AccessToken, Guid UserId, string DisplayName, bool IsAdministrator, DateTimeOffset ExpiresAtUtc, IReadOnlyList<string> Permissions);
public sealed record TemporaryPermissionAuthorizationCommand(string UserName, string Password, string Permission);
public sealed record TemporaryPermissionAuthorizationResult(Guid? GrantId, DateTimeOffset ExpiresAtUtc, string AuthorizedBy);
public sealed record TemporaryPermissionAuthorizationAttempt(TemporaryPermissionAuthorizationResult? Authorization, string? FailureCode)
{
    public bool Succeeded => Authorization is not null;
}

public sealed class AuthenticationService(PosDbContext database, PasswordHasher<UserRecord> passwordHasher)
{
    public async Task<LoginResult?> LoginAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.UserName) || string.IsNullOrEmpty(command.Password)) return null;
        var normalized = InitialSetupService.NormalizeUserName(command.UserName);
        var user = await database.Users.SingleOrDefaultAsync(item => item.NormalizedUserName == normalized && item.IsActive, cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password) == PasswordVerificationResult.Failed) return null;

        var staleTemporaryPermissions = await database.Permissions.IgnoreQueryFilters()
            .Where(item => item.UserId == user.Id && item.ExpiresAtUtc != null)
            .ToListAsync(cancellationToken);
        database.Permissions.RemoveRange(staleTemporaryPermissions);

        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Hash(accessToken), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(8) };
        database.Sessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);
        IReadOnlyList<string> permissions = user.IsAdministrator
            ? Enum.GetNames<Pos.Domain.Permission>()
            : await database.Permissions.Where(item => item.UserId == user.Id && item.ExpiresAtUtc == null).Select(item => item.Code).ToListAsync(cancellationToken);
        return new LoginResult(session.Id, accessToken, user.Id, user.DisplayName, user.IsAdministrator, session.ExpiresAtUtc, permissions);
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

    public async Task<TemporaryPermissionAuthorizationAttempt> GrantTemporaryPermissionAsync(
        string actorToken,
        TemporaryPermissionAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Pos.Domain.Permission>(command.Permission, ignoreCase: false, out _) ||
            string.IsNullOrWhiteSpace(command.UserName) || string.IsNullOrEmpty(command.Password)) return new(null, "invalid-request");

        var actor = await GetActiveSessionUserAsync(actorToken, cancellationToken);
        if (actor is null) return new(null, "session-expired");

        var normalized = InitialSetupService.NormalizeUserName(command.UserName);
        var approver = await database.Users.SingleOrDefaultAsync(item => item.NormalizedUserName == normalized && item.IsActive, cancellationToken);
        if (approver is null) return new(null, "approver-not-found");
        if (passwordHasher.VerifyHashedPassword(approver, approver.PasswordHash, command.Password) == PasswordVerificationResult.Failed) return new(null, "invalid-password");

        var approverCanAuthorize = approver.IsAdministrator || await database.Permissions
            .IgnoreQueryFilters()
            .AnyAsync(item => item.UserId == approver.Id && item.Code == command.Permission && item.ExpiresAtUtc == null, cancellationToken);
        if (!approverCanAuthorize) return new(null, "approver-missing-permission");

        var current = await database.Permissions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.UserId == actor.Id && item.Code == command.Permission, cancellationToken);
        if (current is { ExpiresAtUtc: null }) return new(new TemporaryPermissionAuthorizationResult(null, DateTimeOffset.UtcNow, approver.DisplayName), null);

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
        return new(new TemporaryPermissionAuthorizationResult(current.Id, expiresAtUtc, approver.DisplayName), null);
    }

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
