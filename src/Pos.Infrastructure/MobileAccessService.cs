using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record MobilePairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);
public sealed record MobilePairCommand(string Code, string DeviceName);
public sealed record MobilePairResult(Guid DeviceId, string DeviceToken);
public sealed record MobileLoginCommand(string UserName, string Password);
public sealed record MobileLoginResult(string AccessToken, string DisplayName, DateTimeOffset ExpiresAtUtc, bool CanViewInventory, bool CanAdjustInventory);
public sealed record MobileCapabilitiesResult(bool CanViewInventory, bool CanAdjustInventory);
public sealed record MobileProductResult(Guid Id, string Code, string Description, decimal Price, decimal Stock, string UnitOfMeasure, decimal MinimumStock, decimal MaximumStock);

/// <summary>Autorización de consulta para teléfonos; nunca crea turnos ni sesiones de caja.</summary>
public sealed class MobileAccessService(PosDbContext database, PasswordHasher<UserRecord> passwordHasher)
{
    public async Task<MobilePairingCodeResult?> CreatePairingCodeAsync(string actorToken, CancellationToken cancellationToken)
    {
        var actor = await ActiveDesktopUserAsync(actorToken, cancellationToken);
        if (actor is null || !actor.IsAdministrator) return null;
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (store is null) return null;

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        database.MobilePairingCodes.Add(new MobilePairingCodeRecord
        {
            Id = Guid.NewGuid(), StoreId = store.Id, CreatedByUserId = actor.Id,
            CodeHash = Hash(code), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = expires
        });
        await database.SaveChangesAsync(cancellationToken);
        return new MobilePairingCodeResult(code, expires);
    }

    public async Task<MobilePairResult?> PairAsync(MobilePairCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Code) || string.IsNullOrWhiteSpace(command.DeviceName)) return null;
        if (command.Code.Trim().Length != 6 || command.DeviceName.Trim().Length > 120) throw new ArgumentException("Revisa el código temporal y el nombre del teléfono.");

        var pairing = await database.MobilePairingCodes.SingleOrDefaultAsync(item => item.CodeHash == Hash(command.Code.Trim()) && item.UsedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (pairing is null) return null;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var device = await database.MobileDevices.SingleOrDefaultAsync(item => item.StoreId == pairing.StoreId && item.Name == command.DeviceName.Trim(), cancellationToken);
        if (device is null)
        {
            device = new MobileDeviceRecord { Id = Guid.NewGuid(), StoreId = pairing.StoreId, Name = command.DeviceName.Trim(), DeviceTokenHash = Hash(token), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
            database.MobileDevices.Add(device);
        }
        else
        {
            device.DeviceTokenHash = Hash(token); device.IsActive = true; device.LastSeenAtUtc = null;
            var oldSessions = await database.MobileSessions.Where(item => item.MobileDeviceId == device.Id && item.RevokedAtUtc == null).ToListAsync(cancellationToken);
            foreach (var oldSession in oldSessions) oldSession.RevokedAtUtc = DateTimeOffset.UtcNow;
        }
        pairing.UsedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return new MobilePairResult(device.Id, token);
    }

    public async Task<MobileLoginResult?> LoginAsync(string deviceToken, MobileLoginCommand command, CancellationToken cancellationToken)
    {
        var device = await FindDeviceAsync(deviceToken, cancellationToken);
        if (device is null || string.IsNullOrWhiteSpace(command.UserName) || string.IsNullOrEmpty(command.Password)) return null;
        var normalized = InitialSetupService.NormalizeUserName(command.UserName);
        var user = await database.Users.SingleOrDefaultAsync(item => item.NormalizedUserName == normalized && item.IsActive, cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password) == PasswordVerificationResult.Failed) return null;
        var canView = await HasPermissionAsync(user, Permission.ViewInventory, cancellationToken);
        var canAdjust = await HasPermissionAsync(user, Permission.AdjustInventory, cancellationToken);
        if (!canView && !canAdjust) throw new InvalidOperationException("Este usuario no tiene permiso para consultar o ajustar inventario desde JetVenta Móvil.");

        var previous = await database.MobileSessions.Where(item => item.MobileDeviceId == device.Id && item.UserId == user.Id && item.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var item in previous) item.RevokedAtUtc = DateTimeOffset.UtcNow;
        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddHours(8);
        database.MobileSessions.Add(new MobileSessionRecord { Id = Guid.NewGuid(), MobileDeviceId = device.Id, UserId = user.Id, TokenHash = Hash(accessToken), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = expires });
        device.LastSeenAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return new MobileLoginResult(accessToken, user.DisplayName, expires, canView, canAdjust);
    }

    public async Task<bool> LogoutAsync(string accessToken, string deviceToken, CancellationToken cancellationToken)
    {
        var session = await AuthorizedSessionAsync(accessToken, deviceToken, cancellationToken);
        if (session is null) return false;
        session.RevokedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MobileProductResult?> ByCodeAsync(string accessToken, string deviceToken, string code, CancellationToken cancellationToken)
    {
        var session = await AuthorizedSessionAsync(accessToken, deviceToken, cancellationToken);
        if (session is null) throw new UnauthorizedAccessException();
        if (!await CanReadInventoryAsync(session.UserId, cancellationToken)) throw new InvalidOperationException("Tu usuario ya no tiene permiso para consultar inventario.");
        var normalized = ProductCatalogService.NormalizeCode(code ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return await database.Products.AsNoTracking().Where(item => item.IsActive && !item.IsTemporary && item.NormalizedCode == normalized).Select(ToMobileResult()).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MobileProductResult>?> SearchAsync(string accessToken, string deviceToken, string search, CancellationToken cancellationToken)
    {
        var session = await AuthorizedSessionAsync(accessToken, deviceToken, cancellationToken);
        if (session is null) return null;
        if (!await CanReadInventoryAsync(session.UserId, cancellationToken)) throw new InvalidOperationException("Tu usuario ya no tiene permiso para consultar inventario.");
        var value = (search ?? string.Empty).Trim();
        if (value.Length == 0) return [];
        var normalized = ProductCatalogService.NormalizeCode(value);
        return await database.Products.AsNoTracking()
            .Where(item => item.IsActive && !item.IsTemporary && (item.NormalizedCode == normalized || item.Code.Contains(value) || EF.Functions.ILike(item.Description, $"%{value}%")))
            .OrderBy(item => item.NormalizedCode == normalized ? 0 : 1).ThenBy(item => item.Description).Take(25).Select(ToMobileResult()).ToListAsync(cancellationToken);
    }

    public async Task<MobileCapabilitiesResult?> GetCapabilitiesAsync(string accessToken, string deviceToken, CancellationToken cancellationToken)
    {
        var session = await AuthorizedSessionAsync(accessToken, deviceToken, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        if (user is null) return null;
        return new MobileCapabilitiesResult(
            await HasPermissionAsync(user, Permission.ViewInventory, cancellationToken),
            await HasPermissionAsync(user, Permission.AdjustInventory, cancellationToken));
    }

    public async Task<IReadOnlyList<MobileProductResult>?> BrowseInventoryAsync(string accessToken, string deviceToken, string? query, string? status, CancellationToken cancellationToken)
    {
        var session = await AuthorizedSessionAsync(accessToken, deviceToken, cancellationToken);
        if (session is null) return null;
        if (!await CanReadInventoryAsync(session.UserId, cancellationToken)) throw new InvalidOperationException("Tu usuario ya no tiene permiso para consultar inventario.");

        var products = database.Products.AsNoTracking().Where(item => item.IsActive && !item.IsTemporary);
        var search = query?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = ProductCatalogService.NormalizeCode(search);
            products = products.Where(item => item.NormalizedCode == normalized || item.Code.Contains(search) || EF.Functions.ILike(item.Description, $"%{search}%"));
        }

        products = (status ?? "all").ToLowerInvariant() switch
        {
            "out" => products.Where(item => item.Stock <= 0m),
            "low" => products.Where(item => item.Stock > 0m && item.MinimumStock > 0m && item.Stock <= item.MinimumStock),
            _ => products
        };

        return await products.OrderBy(item => item.Description).Take(100).Select(ToMobileResult()).ToListAsync(cancellationToken);
    }

    private async Task<MobileSessionRecord?> AuthorizedSessionAsync(string accessToken, string deviceToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;
        var device = await FindDeviceAsync(deviceToken, cancellationToken);
        if (device is null) return null;
        var session = await database.MobileSessions.SingleOrDefaultAsync(item => item.TokenHash == Hash(accessToken) && item.MobileDeviceId == device.Id && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is not null && (device.LastSeenAtUtc is null || device.LastSeenAtUtc < DateTimeOffset.UtcNow.AddMinutes(-1)))
        {
            device.LastSeenAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
        }
        return session;
    }

    private async Task<bool> CanReadInventoryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        return user is not null && (await HasPermissionAsync(user, Permission.ViewInventory, cancellationToken) || await HasPermissionAsync(user, Permission.AdjustInventory, cancellationToken));
    }

    private async Task<bool> HasPermissionAsync(UserRecord user, Permission permission, CancellationToken cancellationToken) =>
        user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission.ToString() && item.ExpiresAtUtc == null, cancellationToken);

    private async Task<MobileDeviceRecord?> FindDeviceAsync(string token, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(token) ? null : await database.MobileDevices.SingleOrDefaultAsync(item => item.DeviceTokenHash == Hash(token) && item.IsActive, cancellationToken);

    private async Task<UserRecord?> ActiveDesktopUserAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == Hash(token) && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        return session is null ? null : await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
    }

    private static Expression<Func<ProductRecord, MobileProductResult>> ToMobileResult() => item => new MobileProductResult(item.Id, item.Code, item.Description, item.Price, item.Stock, item.UnitOfMeasure, item.MinimumStock, item.MaximumStock);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
