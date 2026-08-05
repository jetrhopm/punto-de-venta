using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record LoginCommand(string UserName, string Password);
public sealed record LoginResult(Guid SessionId, string AccessToken, Guid UserId, string DisplayName, bool IsAdministrator, DateTimeOffset ExpiresAtUtc, IReadOnlyList<string> Permissions);

public sealed class AuthenticationService(PosDbContext database, PasswordHasher<UserRecord> passwordHasher)
{
    public async Task<LoginResult?> LoginAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.UserName) || string.IsNullOrEmpty(command.Password)) return null;
        var normalized = InitialSetupService.NormalizeUserName(command.UserName);
        var user = await database.Users.SingleOrDefaultAsync(item => item.NormalizedUserName == normalized && item.IsActive, cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password) == PasswordVerificationResult.Failed) return null;

        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Hash(accessToken), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(8) };
        database.Sessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);
        IReadOnlyList<string> permissions = user.IsAdministrator
            ? Enum.GetNames<Pos.Domain.Permission>()
            : await database.Permissions.Where(item => item.UserId == user.Id).Select(item => item.Code).ToListAsync(cancellationToken);
        return new LoginResult(session.Id, accessToken, user.Id, user.DisplayName, user.IsAdministrator, session.ExpiresAtUtc, permissions);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
