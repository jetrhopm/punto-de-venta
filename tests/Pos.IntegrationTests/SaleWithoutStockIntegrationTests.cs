using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class SaleWithoutStockIntegrationTests
{
    [Fact]
    public async Task CompletesSaleAndRecordsNegativeStockWhenProductHasNoInventory()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda venta sin existencia " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "SIN_STOCK_" + suffix, DisplayName = "Venta sin existencia", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "SIN-" + suffix, NormalizedCode = "SIN-" + suffix.ToUpperInvariant(), Description = "Producto sin existencia", Price = 25m, Stock = 0m, IsActive = true };

        database.AddRange(store, user, register, session, product);
        await database.SaveChangesAsync();

        try
        {
            var shift = await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(register.Id, 0m), CancellationToken.None);
            Assert.NotNull(shift);

            var operationId = Guid.NewGuid();
            var sales = new SaleService(database, new PromotionService(database), new KitService(database));
            var result = await sales.CompleteAsync(token, new CompleteSaleCommand(operationId, [new SaleLineCommand(product.Id, 1m)], 25m), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(25m, result.Total);
            Assert.Equal(-1m, await database.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());

            var movement = await database.InventoryMovements.SingleAsync(item => item.OperationId == operationId);
            Assert.Equal(0m, movement.StockBefore);
            Assert.Equal(-1m, movement.StockAfter);
            Assert.Equal(-1m, movement.Quantity);
        }
        finally
        {
            var shiftIds = await database.Shifts.Where(item => item.RegisterId == register.Id).Select(item => item.Id).ToListAsync();
            var saleIds = await database.Sales.Where(item => shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            database.PrintJobs.RemoveRange(database.PrintJobs.Where(item => saleIds.Contains(item.SaleId)));
            database.Payments.RemoveRange(database.Payments.Where(item => saleIds.Contains(item.SaleId)));
            database.InventoryMovements.RemoveRange(database.InventoryMovements.Where(item => saleIds.Contains(item.SaleId ?? Guid.Empty)));
            database.SaleLines.RemoveRange(database.SaleLines.Where(item => saleIds.Contains(item.SaleId)));
            database.Sales.RemoveRange(database.Sales.Where(item => saleIds.Contains(item.Id)));
            database.Shifts.RemoveRange(database.Shifts.Where(item => shiftIds.Contains(item.Id)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Products.Remove(product);
            database.Registers.Remove(register);
            database.Users.Remove(user);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }
}
