using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class AuthenticationSessionIntegrationTests
{
    [Fact]
    public async Task BindsSessionsToRegisterAndBlocksAnotherRegisterUntilTheShiftCloses()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var hasher = new PasswordHasher<UserRecord>();
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda sesiones " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("CAJERO_" + suffix).ToUpperInvariant(), DisplayName = "Cajero multicaja", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        user.PasswordHash = hasher.HashPassword(user, "clave-prueba");
        var primaryRegister = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja principal " + suffix, IsActive = true };
        var firstRegister = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja A " + suffix, IsActive = true };
        var secondRegister = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja B " + suffix, IsActive = true };
        var firstToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secondToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var firstDevice = new DeviceRecord { Id = Guid.NewGuid(), StoreId = store.Id, RegisterId = firstRegister.Id, Name = "Equipo A " + suffix, DeviceType = "Register", DeviceTokenHash = Hash(firstToken), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var secondDevice = new DeviceRecord { Id = Guid.NewGuid(), StoreId = store.Id, RegisterId = secondRegister.Id, Name = "Equipo B " + suffix, DeviceType = "Register", DeviceTokenHash = Hash(secondToken), IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        database.AddRange(store, user, primaryRegister, firstRegister, secondRegister, firstDevice, secondDevice);
        await database.SaveChangesAsync();

        try
        {
            var service = new AuthenticationService(database, hasher);
            var command = new LoginCommand(user.NormalizedUserName, "clave-prueba");
            var firstLogin = await service.LoginDetailedAsync(command, firstToken, false, CancellationToken.None);
            Assert.True(firstLogin.Session is not null, $"{firstLogin.FailureCode}: {firstLogin.FailureMessage}");
            Assert.Equal(firstRegister.Id, firstLogin.Session!.RegisterId);
            Assert.Equal(firstDevice.Id, (await database.Sessions.SingleAsync(item => item.Id == firstLogin.Session.SessionId)).DeviceId);

            var blockedBySession = await service.LoginDetailedAsync(command, secondToken, false, CancellationToken.None);
            Assert.Null(blockedBySession.Session);
            Assert.Equal("session_active_elsewhere", blockedBySession.FailureCode);

            var resumedOnSameRegister = await service.LoginDetailedAsync(command, firstToken, false, CancellationToken.None);
            Assert.NotNull(resumedOnSameRegister.Session);
            Assert.NotNull((await database.Sessions.SingleAsync(item => item.Id == firstLogin.Session.SessionId)).RevokedAtUtc);

            var shift = new ShiftRecord { Id = Guid.NewGuid(), RegisterId = firstRegister.Id, UserId = user.Id, InitialCash = 0m, Status = "Open", OpenedAtUtc = DateTimeOffset.UtcNow };
            database.Shifts.Add(shift);
            (await database.Sessions.SingleAsync(item => item.Id == resumedOnSameRegister.Session!.SessionId)).RevokedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync();

            var blockedByShift = await service.LoginDetailedAsync(command, secondToken, false, CancellationToken.None);
            Assert.Null(blockedByShift.Session);
            Assert.Equal("shift_open_elsewhere", blockedByShift.FailureCode);

            var sameRegisterWithOpenShift = await service.LoginDetailedAsync(command, firstToken, false, CancellationToken.None);
            Assert.NotNull(sameRegisterWithOpenShift.Session);

            shift.Status = "Closed";
            (await database.Sessions.SingleAsync(item => item.Id == sameRegisterWithOpenShift.Session!.SessionId)).RevokedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync();
            var secondRegisterLogin = await service.LoginDetailedAsync(command, secondToken, false, CancellationToken.None);
            Assert.NotNull(secondRegisterLogin.Session);

            (await database.Sessions.SingleAsync(item => item.Id == secondRegisterLogin.Session!.SessionId)).RevokedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync();
            var localServerLogin = await service.LoginDetailedAsync(command, null, true, CancellationToken.None);
            Assert.NotNull(localServerLogin.Session);
            var expectedLocalRegister = await database.Registers.AsNoTracking()
                .Where(item => item.IsActive)
                .OrderBy(item => database.Devices.Any(device => device.RegisterId == item.Id) ? 1 : 0)
                .ThenBy(item => item.Name)
                .Select(item => item.Id)
                .FirstAsync();
            Assert.Equal(expectedLocalRegister, localServerLogin.Session!.RegisterId);

            var unpairedRemote = await service.LoginDetailedAsync(command, null, false, CancellationToken.None);
            Assert.Equal("device_required", unpairedRemote.FailureCode);
        }
        finally
        {
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Shifts.RemoveRange(database.Shifts.Where(item => item.UserId == user.Id));
            database.Devices.RemoveRange(database.Devices.Where(item => item.Id == firstDevice.Id || item.Id == secondDevice.Id));
            database.Registers.RemoveRange(database.Registers.Where(item => item.Id == primaryRegister.Id || item.Id == firstRegister.Id || item.Id == secondRegister.Id));
            database.Users.Remove(user);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
