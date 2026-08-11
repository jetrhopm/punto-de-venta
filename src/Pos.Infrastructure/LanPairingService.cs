using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);
public sealed record PairDeviceCommand(string Code, string DeviceName, string RegisterName);
public sealed record PairDeviceResult(Guid DeviceId, Guid StoreId, Guid RegisterId, string DeviceToken, string RegisterName);

public sealed class LanPairingService(PosDbContext database)
{
    public async Task<PairingCodeResult?> CreateCodeAsync(string token, CancellationToken cancellationToken)
    {
        var user = await AuthorizedUserAsync(token, cancellationToken);
        if (user is null || !user.IsAdministrator) return null;
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (store is null) return null;
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        database.PairingCodes.Add(new PairingCodeRecord { Id = Guid.NewGuid(), StoreId = store.Id, CreatedByUserId = user.Id, CodeHash = Hash(code), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = expires });
        await database.SaveChangesAsync(cancellationToken);
        return new PairingCodeResult(code, expires);
    }

    public async Task<PairDeviceResult?> PairAsync(PairDeviceCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Code) || string.IsNullOrWhiteSpace(command.DeviceName) || string.IsNullOrWhiteSpace(command.RegisterName)) return null;
        var pairing = await database.PairingCodes.SingleOrDefaultAsync(item => item.CodeHash == Hash(command.Code.Trim()) && item.UsedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (pairing is null) return null;
        var registerName = command.RegisterName.Trim();
        if (await database.Registers.AnyAsync(item => item.StoreId == pairing.StoreId && item.Name == registerName, cancellationToken)) throw new InvalidOperationException("El nombre de caja ya existe.");
        var deviceToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = pairing.StoreId, Name = registerName, IsActive = true };
        var device = new DeviceRecord { Id = Guid.NewGuid(), StoreId = pairing.StoreId, RegisterId = register.Id, Name = command.DeviceName.Trim(), DeviceType = "Register", DeviceTokenHash = Hash(deviceToken), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        pairing.UsedAtUtc = DateTimeOffset.UtcNow;
        database.Registers.Add(register); database.Devices.Add(device);
        await database.SaveChangesAsync(cancellationToken);
        return new PairDeviceResult(device.Id, device.StoreId, device.RegisterId, deviceToken, register.Name);
    }

    private async Task<UserRecord?> AuthorizedUserAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Hash(token);
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        return session is null ? null : await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
