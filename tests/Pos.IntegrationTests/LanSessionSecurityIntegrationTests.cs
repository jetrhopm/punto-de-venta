using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class LanSessionSecurityIntegrationTests
{
    [Fact]
    public async Task RequiresThePairedDeviceTokenForRemoteSessions()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var deviceToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda LAN " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja LAN " + suffix, IsActive = true };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "LAN_" + suffix, DisplayName = "Usuario LAN", PasswordHash = "test", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var device = new DeviceRecord { Id = Guid.NewGuid(), StoreId = store.Id, RegisterId = register.Id, Name = "Equipo LAN " + suffix, DeviceType = "Register", DeviceTokenHash = Hash(deviceToken), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var remoteSession = Session(user.Id, register.Id, device.Id, accessToken);
        var localSessionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var localSession = Session(user.Id, register.Id, null, localSessionToken);
        database.AddRange(store, register, user, device, remoteSession, localSession);
        await database.SaveChangesAsync();

        try
        {
            var service = new AuthenticationService(database, new PasswordHasher<UserRecord>());
            Assert.True((await service.ValidateSessionDeviceAsync(accessToken, deviceToken, false, CancellationToken.None)).IsValid);
            Assert.False((await service.ValidateSessionDeviceAsync(accessToken, null, false, CancellationToken.None)).IsValid);
            Assert.False((await service.ValidateSessionDeviceAsync(accessToken, "otro-token", false, CancellationToken.None)).IsValid);
            Assert.True((await service.ValidateSessionDeviceAsync(localSessionToken, null, true, CancellationToken.None)).IsValid);
            var remoteLocalSession = await service.ValidateSessionDeviceAsync(localSessionToken, null, false, CancellationToken.None);
            Assert.False(remoteLocalSession.IsValid);
            Assert.Equal("local_session_only", remoteLocalSession.FailureCode);
        }
        finally
        {
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.Id == remoteSession.Id || item.Id == localSession.Id));
            database.Devices.Remove(device);
            database.Registers.Remove(register);
            database.Users.Remove(user);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.8.10", true)]
    [InlineData("192.168.1.20", true)]
    [InlineData("8.8.8.8", false)]
    public void OnlyAcceptsLoopbackAndPrivateNetworkAddresses(string address, bool expected) =>
        Assert.Equal(expected, LanNetworkPolicy.IsLocalOrPrivate(IPAddress.Parse(address)));

    private static SessionRecord Session(Guid userId, Guid registerId, Guid? deviceId, string token) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        RegisterId = registerId,
        DeviceId = deviceId,
        TokenHash = Hash(token),
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
    };

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
