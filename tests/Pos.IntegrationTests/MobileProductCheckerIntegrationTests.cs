using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class MobileProductCheckerIntegrationTests
{
    [Fact]
    public async Task PairedMobileUserWithInventoryPermissionCanOnlyReadProductInformation()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync();
        var hasher = new PasswordHasher<UserRecord>();
        var administrator = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("ADMIN_MOVIL_" + suffix).ToUpperInvariant(), DisplayName = "Administrador móvil", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        administrator.PasswordHash = hasher.HashPassword(administrator, "admin-pass");
        var reader = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("LECTOR_MOVIL_" + suffix).ToUpperInvariant(), DisplayName = "Consulta móvil", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        reader.PasswordHash = hasher.HashPassword(reader, "lector-pass");
        var administratorToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var administratorSession = new SessionRecord { Id = Guid.NewGuid(), UserId = administrator.Id, TokenHash = Hash(administratorToken), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var permission = new PermissionRecord { Id = Guid.NewGuid(), UserId = reader.Id, Code = "ViewInventory" };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "MOVIL-" + suffix, NormalizedCode = "MOVIL-" + suffix.ToUpperInvariant(), Description = "Producto para móvil", Price = 28.5m, Cost = 11m, Stock = 7m, MinimumStock = 3m, MaximumStock = 20m, IsActive = true };
        database.AddRange(administrator, reader, administratorSession, permission, product);
        await database.SaveChangesAsync();

        try
        {
            var service = new MobileAccessService(database, hasher);
            var code = await service.CreatePairingCodeAsync(administratorToken, CancellationToken.None);
            Assert.NotNull(code);
            var paired = await service.PairAsync(new MobilePairCommand(code!.Code, "Android prueba " + suffix), CancellationToken.None);
            Assert.NotNull(paired);
            Assert.True(await database.MobileDevices.AnyAsync(item => item.Id == paired!.DeviceId));
            database.ChangeTracker.Clear();

            var login = await service.LoginAsync(paired!.DeviceToken, new MobileLoginCommand(reader.NormalizedUserName, "lector-pass"), CancellationToken.None);
            Assert.NotNull(login);

            var result = await service.ByCodeAsync(login!.AccessToken, paired.DeviceToken, product.Code, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(product.Description, result!.Description);
            Assert.Equal(product.Price, result.Price);
            Assert.Equal(product.Stock, result.Stock);
            Assert.Empty(await database.Shifts.Where(item => item.UserId == reader.Id).ToListAsync());
            Assert.Contains((await service.SearchAsync(login.AccessToken, paired.DeviceToken, "Producto para", CancellationToken.None) ?? []), item => item.Id == product.Id);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new MobileInventoryCountService(database).StartSessionAsync(login.AccessToken, paired.DeviceToken, CancellationToken.None));
        }
        finally
        {
            await database.MobileSessions.Where(item => item.UserId == reader.Id).ExecuteDeleteAsync();
            await database.MobilePairingCodes.Where(item => item.CreatedByUserId == administrator.Id).ExecuteDeleteAsync();
            await database.MobileDevices.Where(item => item.Name == "Android prueba " + suffix).ExecuteDeleteAsync();
            await database.Permissions.Where(item => item.Id == permission.Id).ExecuteDeleteAsync();
            await database.Sessions.Where(item => item.Id == administratorSession.Id).ExecuteDeleteAsync();
            await database.Products.Where(item => item.Id == product.Id).ExecuteDeleteAsync();
            await database.Users.Where(item => item.Id == administrator.Id || item.Id == reader.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task MobileInventoryCountAppliesImmediatelyAndKeepsAnIdempotentAudit()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var hasher = new PasswordHasher<UserRecord>();
        var administrator = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("ADMIN_CONTEO_" + suffix).ToUpperInvariant(), DisplayName = "Administrador conteo", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        administrator.PasswordHash = hasher.HashPassword(administrator, "admin-pass");
        var counter = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("CONTEO_" + suffix).ToUpperInvariant(), DisplayName = "Usuario conteo", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        counter.PasswordHash = hasher.HashPassword(counter, "conteo-pass");
        var administratorToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var administratorSession = new SessionRecord { Id = Guid.NewGuid(), UserId = administrator.Id, TokenHash = Hash(administratorToken), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var permission = new PermissionRecord { Id = Guid.NewGuid(), UserId = counter.Id, Code = Permission.AdjustInventory.ToString() };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "CONTEO-" + suffix, NormalizedCode = "CONTEO-" + suffix.ToUpperInvariant(), Description = "Producto conteo móvil", Price = 28.5m, Cost = 11m, Stock = 7m, MinimumStock = 3m, MaximumStock = 20m, IsActive = true };
        database.AddRange(administrator, counter, administratorSession, permission, product);
        await database.SaveChangesAsync();

        Guid deviceId = Guid.Empty;
        Guid countSessionId = Guid.Empty;
        try
        {
            var access = new MobileAccessService(database, hasher);
            var pairingCode = await access.CreatePairingCodeAsync(administratorToken, CancellationToken.None);
            Assert.NotNull(pairingCode);
            var paired = await access.PairAsync(new MobilePairCommand(pairingCode!.Code, "Android conteo " + suffix), CancellationToken.None);
            Assert.NotNull(paired);
            deviceId = paired!.DeviceId;
            var login = await access.LoginAsync(paired.DeviceToken, new MobileLoginCommand(counter.NormalizedUserName, "conteo-pass"), CancellationToken.None);
            Assert.NotNull(login);

            var service = new MobileInventoryCountService(database);
            var session = await service.StartSessionAsync(login!.AccessToken, paired.DeviceToken, CancellationToken.None);
            Assert.NotNull(session);
            countSessionId = session!.SessionId;
            var operationId = Guid.NewGuid();
            var command = new MobileCountCommand(session.SessionId, operationId, product.Id, 7m, 5m);

            var result = await service.RegisterCountAsync(login.AccessToken, paired.DeviceToken, command, CancellationToken.None);
            Assert.Equal(-2m, result!.Difference);
            Assert.Equal(5m, result.CurrentStock);
            Assert.Equal(5m, await database.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());
            Assert.Equal(1, await database.InventoryMovements.CountAsync(item => item.OperationId == operationId));

            var retry = await service.RegisterCountAsync(login.AccessToken, paired.DeviceToken, command, CancellationToken.None);
            Assert.Equal(result.OperationId, retry!.OperationId);
            Assert.Equal(result.Difference, retry.Difference);
            Assert.Equal(result.CurrentStock, retry.CurrentStock);
            Assert.Equal(1, await database.MobileInventoryCounts.CountAsync(item => item.OperationId == operationId));
            var history = await service.HistoryAsync(login.AccessToken, paired.DeviceToken, 50, differencesOnly: true, CancellationToken.None) ?? [];
            Assert.Single(history, item => item.ProductId == product.Id);

            Assert.True(await service.CloseSessionAsync(login.AccessToken, paired.DeviceToken, session.SessionId, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterCountAsync(login.AccessToken, paired.DeviceToken, command with { OperationId = Guid.NewGuid() }, CancellationToken.None));
        }
        finally
        {
            await database.MobileInventoryCounts.Where(item => item.ProductId == product.Id).ExecuteDeleteAsync();
            if (countSessionId != Guid.Empty) await database.MobileInventoryCountSessions.Where(item => item.Id == countSessionId).ExecuteDeleteAsync();
            await database.InventoryMovements.Where(item => item.ProductId == product.Id).ExecuteDeleteAsync();
            await database.MobileSessions.Where(item => item.UserId == counter.Id).ExecuteDeleteAsync();
            await database.MobilePairingCodes.Where(item => item.CreatedByUserId == administrator.Id).ExecuteDeleteAsync();
            if (deviceId != Guid.Empty) await database.MobileDevices.Where(item => item.Id == deviceId).ExecuteDeleteAsync();
            await database.Permissions.Where(item => item.Id == permission.Id).ExecuteDeleteAsync();
            await database.Sessions.Where(item => item.Id == administratorSession.Id).ExecuteDeleteAsync();
            await database.Products.Where(item => item.Id == product.Id).ExecuteDeleteAsync();
            await database.Users.Where(item => item.Id == administrator.Id || item.Id == counter.Id).ExecuteDeleteAsync();
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
