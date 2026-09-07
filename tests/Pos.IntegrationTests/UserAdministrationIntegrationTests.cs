using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class UserAdministrationIntegrationTests
{
    [Fact]
    public async Task CreatesCashierWithSafeDefaultsAndAdministratorWithAllPermissions()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var administrator = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "ADMIN_" + suffix, DisplayName = "Administrador de prueba", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = administrator.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        database.AddRange(administrator, session);
        await database.SaveChangesAsync();

        try
        {
            var service = new UserAdministrationService(database, new PasswordHasher<UserRecord>());
            var cashier = await service.CreateAsync(token, new UserCommand("CAJERO_" + suffix, "12345", "Cajero de prueba", false), CancellationToken.None);

            Assert.NotNull(cashier);
            Assert.False(cashier.IsAdministrator);
            Assert.Equal(
                ["CancelSaleLines", "OpenCashDrawer", "OpenShift", "RecordCashMovements", "ReprintTickets", "Sell", "UseCommonProduct", "ViewProducts", "ViewSalesHistory"],
                cashier.Permissions.OrderBy(item => item));

            var promoted = await service.UpdateAsync(token, cashier.Id, new UpdateUserCommand(cashier.UserName, cashier.DisplayName, true, null), CancellationToken.None);
            Assert.NotNull(promoted);
            Assert.True(promoted.IsAdministrator);
            Assert.Equal(Enum.GetNames<Pos.Domain.Permission>().OrderBy(item => item), promoted.Permissions.OrderBy(item => item));
        }
        finally
        {
            var userIds = await database.Users.Where(item => item.Id == administrator.Id || item.NormalizedUserName.EndsWith(suffix)).Select(item => item.Id).ToListAsync();
            database.Permissions.RemoveRange(database.Permissions.Where(item => userIds.Contains(item.UserId)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => userIds.Contains(item.UserId)));
            database.Users.RemoveRange(database.Users.Where(item => userIds.Contains(item.Id)));
            await database.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task TemporaryPermissionIsHiddenFromAdministrationAndRemovedOnLogout()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var actorToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var administratorToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var hasher = new PasswordHasher<UserRecord>();
        var administrator = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("ADMIN_AUTH_" + suffix).ToUpperInvariant(), DisplayName = "Administrador autorizador", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        administrator.PasswordHash = hasher.HashPassword(administrator, "clave-admin");
        var cashier = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("CAJERO_AUTH_" + suffix).ToUpperInvariant(), DisplayName = "Cajero autorizado", IsAdministrator = false, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        cashier.PasswordHash = hasher.HashPassword(cashier, "clave-cajero");
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = cashier.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actorToken))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var administratorSession = new SessionRecord { Id = Guid.NewGuid(), UserId = administrator.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(administratorToken))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        database.AddRange(administrator, cashier, session, administratorSession);
        await database.SaveChangesAsync();

        try
        {
            var authentication = new AuthenticationService(database, hasher);
            var invalidCredentials = await authentication.GrantTemporaryPermissionDetailedAsync(
                actorToken,
                new TemporaryPermissionAuthorizationCommand(administrator.NormalizedUserName, "clave-incorrecta", "CloseShift"),
                CancellationToken.None);
            Assert.Null(invalidCredentials.Authorization);
            Assert.Equal("invalid_credentials", invalidCredentials.FailureCode);

            var missingPermission = await authentication.GrantTemporaryPermissionDetailedAsync(
                actorToken,
                new TemporaryPermissionAuthorizationCommand(cashier.NormalizedUserName, "clave-cajero", "CloseShift"),
                CancellationToken.None);
            Assert.Null(missingPermission.Authorization);
            Assert.Equal("permission_missing", missingPermission.FailureCode);

            var grant = await authentication.GrantTemporaryPermissionAsync(actorToken, new TemporaryPermissionAuthorizationCommand(administrator.NormalizedUserName, "clave-admin", "CloseShift"), CancellationToken.None);

            Assert.NotNull(grant);
            Assert.NotNull(grant.GrantId);
            Assert.True(grant.ExpiresAtUtc > DateTimeOffset.UtcNow);
            Assert.True(await database.Permissions.AnyAsync(item => item.UserId == cashier.Id && item.Code == "CloseShift"));

            var users = await new UserAdministrationService(database, hasher).ListAsync(administratorToken, CancellationToken.None);
            var listedCashier = Assert.Single(users!, item => item.Id == cashier.Id);
            Assert.DoesNotContain("CloseShift", listedCashier.Permissions);

            Assert.True(await authentication.LogoutAsync(actorToken, CancellationToken.None));
            Assert.False(await database.Permissions.IgnoreQueryFilters().AnyAsync(item => item.UserId == cashier.Id && item.Code == "CloseShift"));
            Assert.NotNull((await database.Sessions.SingleAsync(item => item.Id == session.Id)).RevokedAtUtc);
        }
        finally
        {
            var userIds = new[] { administrator.Id, cashier.Id };
            database.Permissions.RemoveRange(database.Permissions.IgnoreQueryFilters().Where(item => userIds.Contains(item.UserId)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => userIds.Contains(item.UserId)));
            database.Users.RemoveRange(database.Users.Where(item => userIds.Contains(item.Id)));
            await database.SaveChangesAsync();
        }
    }
}
