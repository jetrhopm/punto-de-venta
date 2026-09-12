using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);
public sealed record PairDeviceCommand(string Code, string DeviceName, string RegisterName);
public sealed record PairDeviceResult(Guid DeviceId, Guid StoreId, Guid RegisterId, string DeviceToken, string RegisterName);

public sealed class LanPairingService(PosDbContext database)
{
    public async Task<PairingCodeResult?> CreateCodeAsync(string token, Guid? registerId, CancellationToken cancellationToken)
    {
        var user = await AuthorizedUserAsync(token, cancellationToken);
        if (user is null || !user.IsAdministrator) return null;
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (store is null) return null;
        if (registerId is not null && !await database.Registers.AnyAsync(item => item.Id == registerId && item.StoreId == store.Id && item.IsActive, cancellationToken))
            throw new InvalidOperationException("La caja ya no existe o está desactivada. Reactívala antes de volver a emparejarla.");
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        database.PairingCodes.Add(new PairingCodeRecord { Id = Guid.NewGuid(), StoreId = store.Id, CreatedByUserId = user.Id, RegisterId = registerId, CodeHash = Hash(code), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = expires });
        await database.SaveChangesAsync(cancellationToken);
        return new PairingCodeResult(code, expires);
    }

    public async Task<PairDeviceResult?> PairAsync(PairDeviceCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Code) || string.IsNullOrWhiteSpace(command.DeviceName) || string.IsNullOrWhiteSpace(command.RegisterName)) return null;
        if (command.Code.Trim().Length != 6 || command.DeviceName.Trim().Length > 80 || command.RegisterName.Trim().Length > 80) throw new ArgumentException("Revisa el código y los nombres de equipo y caja.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var pairing = await database.PairingCodes.SingleOrDefaultAsync(item => item.CodeHash == Hash(command.Code.Trim()) && item.UsedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (pairing is null) return null;
        var registerName = command.RegisterName.Trim();
        var deviceToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        RegisterRecord register;
        DeviceRecord device;
        if (pairing.RegisterId is Guid existingRegisterId)
        {
            register = await database.Registers.SingleOrDefaultAsync(item => item.Id == existingRegisterId && item.StoreId == pairing.StoreId && item.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("La caja ya no está disponible para volver a emparejarse.");
            var existingDevice = await database.Devices.Where(item => item.RegisterId == register.Id).OrderByDescending(item => item.LastSeenAtUtc ?? item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
            if (existingDevice is null)
            {
                device = new DeviceRecord { Id = Guid.NewGuid(), StoreId = pairing.StoreId, RegisterId = register.Id, Name = command.DeviceName.Trim(), DeviceType = "Register", DeviceTokenHash = Hash(deviceToken), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
                database.Devices.Add(device);
            }
            else
            {
                device = existingDevice;
                device.Name = command.DeviceName.Trim();
                device.DeviceTokenHash = Hash(deviceToken);
                device.IsActive = true;
                device.LastSeenAtUtc = null;
            }
            var sessions = await database.Sessions.Where(item => item.RegisterId == register.Id && item.RevokedAtUtc == null).ToListAsync(cancellationToken);
            foreach (var session in sessions) session.RevokedAtUtc = DateTimeOffset.UtcNow;
            foreach (var other in await database.Devices.Where(item => item.RegisterId == register.Id && item.Id != device.Id).ToListAsync(cancellationToken)) other.IsActive = false;
        }
        else
        {
            if (await database.Registers.AnyAsync(item => item.StoreId == pairing.StoreId && item.Name == registerName, cancellationToken)) throw new InvalidOperationException("El nombre de caja ya existe.");
            register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = pairing.StoreId, Name = registerName, IsActive = true };
            device = new DeviceRecord { Id = Guid.NewGuid(), StoreId = pairing.StoreId, RegisterId = register.Id, Name = command.DeviceName.Trim(), DeviceType = "Register", DeviceTokenHash = Hash(deviceToken), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
            database.Registers.Add(register); database.Devices.Add(device);
        }
        pairing.UsedAtUtc = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
