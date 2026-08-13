using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record UserCommand(string UserName, string Password, string DisplayName, bool IsAdministrator);
public sealed record UserStatusCommand(bool IsActive);
public sealed record UserPermissionsCommand(IReadOnlyList<string> Permissions);
public sealed record UserPasswordCommand(string Password);
public sealed record UserResult(Guid Id, string UserName, string DisplayName, bool IsAdministrator, bool IsActive, IReadOnlyList<string> Permissions);

public sealed class UserAdministrationService(PosDbContext database, PasswordHasher<UserRecord> passwordHasher)
{
    public async Task<IReadOnlyList<UserResult>?> ListAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedUserAsync(token, cancellationToken) is null) return null;
        var users = await database.Users.AsNoTracking().OrderBy(item => item.NormalizedUserName).ToListAsync(cancellationToken);
        var permissions = await database.Permissions.AsNoTracking().ToListAsync(cancellationToken);
        return users.Select(user => ToResult(user, permissions.Where(item => item.UserId == user.Id).Select(item => item.Code).ToArray())).ToArray();
    }

    public async Task<UserResult?> CreateAsync(string token, UserCommand command, CancellationToken cancellationToken)
    {
        var caller = await AuthorizedUserAsync(token, cancellationToken);
        if (caller is null) return null;
        if (command.IsAdministrator && !caller.IsAdministrator) throw new InvalidOperationException("Solo un administrador puede crear otro administrador.");
        Validate(command);
        var normalized = InitialSetupService.NormalizeUserName(command.UserName);
        if (await database.Users.AnyAsync(item => item.NormalizedUserName == normalized, cancellationToken)) throw new InvalidOperationException("El usuario ya existe.");
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = normalized, DisplayName = command.DisplayName.Trim(), IsAdministrator = command.IsAdministrator, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        user.PasswordHash = passwordHasher.HashPassword(user, command.Password);
        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(user, []);
    }

    public async Task<UserResult?> SetStatusAsync(string token, Guid userId, UserStatusCommand command, CancellationToken cancellationToken)
    {
        var caller = await AuthorizedUserAsync(token, cancellationToken);
        if (caller is null) return null;
        var user = await database.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken) ?? throw new KeyNotFoundException("Usuario no encontrado.");
        EnsureCanModify(caller, user);
        if (!command.IsActive && user.IsAdministrator && user.IsActive && await database.Users.CountAsync(item => item.IsAdministrator && item.IsActive, cancellationToken) <= 1) throw new InvalidOperationException("No se puede desactivar al ultimo administrador activo.");
        user.IsActive = command.IsActive;
        await database.SaveChangesAsync(cancellationToken);
        return await FindResultAsync(user, cancellationToken);
    }

    public async Task<UserResult?> ResetPasswordAsync(string token, Guid userId, string password, CancellationToken cancellationToken)
    {
        var caller = await AuthorizedUserAsync(token, cancellationToken);
        if (caller is null) return null;
        if (string.IsNullOrEmpty(password) || password.Length > 256) throw new ArgumentException("La contrasena es obligatoria y valida.");
        var user = await database.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken) ?? throw new KeyNotFoundException("Usuario no encontrado.");
        EnsureCanModify(caller, user);
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        await database.SaveChangesAsync(cancellationToken);
        return await FindResultAsync(user, cancellationToken);
    }

    public async Task<UserResult?> SetPermissionsAsync(string token, Guid userId, UserPermissionsCommand command, CancellationToken cancellationToken)
    {
        var caller = await AuthorizedUserAsync(token, cancellationToken);
        if (caller is null) return null;
        var user = await database.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken) ?? throw new KeyNotFoundException("Usuario no encontrado.");
        EnsureCanModify(caller, user);
        var allowed = Enum.GetNames<Pos.Domain.Permission>().ToHashSet(StringComparer.Ordinal);
        var permissions = command.Permissions.Distinct(StringComparer.Ordinal).ToArray();
        if (permissions.Any(item => !allowed.Contains(item))) throw new ArgumentException("Se recibio un permiso no valido.");
        var current = await database.Permissions.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        database.Permissions.RemoveRange(current);
        database.Permissions.AddRange(permissions.Select(code => new PermissionRecord { Id = Guid.NewGuid(), UserId = userId, Code = code }));
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(user, permissions);
    }

    private async Task<UserResult> FindResultAsync(UserRecord user, CancellationToken cancellationToken) => ToResult(user, await database.Permissions.AsNoTracking().Where(item => item.UserId == user.Id).Select(item => item.Code).ToArrayAsync(cancellationToken));
    private static UserResult ToResult(UserRecord user, IReadOnlyList<string> permissions) => new(user.Id, user.NormalizedUserName, user.DisplayName, user.IsAdministrator, user.IsActive, permissions);
    private async Task<UserRecord?> AuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        if (user is null) return null;
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == nameof(Pos.Domain.Permission.ManageUsers), cancellationToken) ? user : null;
    }
    private static void EnsureCanModify(UserRecord caller, UserRecord target)
    {
        if (target.IsAdministrator && !caller.IsAdministrator) throw new InvalidOperationException("Solo un administrador puede modificar a otro administrador.");
    }
    private static void Validate(UserCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.UserName) || command.UserName.Trim().Length > 80 || string.IsNullOrEmpty(command.Password) || command.Password.Length > 256 || string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Trim().Length > 160) throw new ArgumentException("Usuario, contrasena y nombre son obligatorios y deben tener una longitud valida.");
    }
}
