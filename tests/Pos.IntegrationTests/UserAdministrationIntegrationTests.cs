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
                ["CancelSaleLines", "CloseShift", "OpenCashDrawer", "OpenShift", "RecordCashMovements", "ReprintTickets", "Sell", "UseCommonProduct", "ViewProducts", "ViewSalesHistory"],
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
}
