using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class SaleWithoutStockIntegrationTests
{
    [Fact]
    public async Task CompletesSaleUsingTheStoreOwnedByTheSessionRegister()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var legacyStore = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda anterior " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1) };
        var currentStore = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda actual " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "CAJA_TIENDA_" + suffix, DisplayName = "Caja de tienda actual", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = currentStore.Id, Name = "Caja " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, RegisterId = register.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "CAJA-TIENDA-" + suffix, NormalizedCode = "CAJA-TIENDA-" + suffix.ToUpperInvariant(), Description = "Producto de caja actual", Price = 15m, Stock = 2m, IsActive = true };
        database.AddRange(legacyStore, currentStore, user, register, session, product);
        await database.SaveChangesAsync();

        try
        {
            Assert.NotNull(await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(0m), CancellationToken.None));

            var result = await new SaleService(database, new PromotionService(database), new KitService(database)).CompleteAsync(
                token,
                new CompleteSaleCommand(Guid.NewGuid(), [new SaleLineCommand(product.Id, 1m)], 15m),
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(15m, result!.Total);
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
            database.Stores.RemoveRange(legacyStore, currentStore);
            await database.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RejectsSaleWhenStoreBlocksSalesWithoutStock()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync();
        var originalBlockSalesWithoutStock = store.BlockSalesWithoutStock;
        store.BlockSalesWithoutStock = true;
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "BLOQUEO_" + suffix, DisplayName = "Venta bloqueada", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, RegisterId = register.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "BLOQUEO-" + suffix, NormalizedCode = "BLOQUEO-" + suffix.ToUpperInvariant(), Description = "Producto sin existencia", Price = 25m, Stock = 0m, IsActive = true };

        database.AddRange(user, register, session, product);
        await database.SaveChangesAsync();

        try
        {
            Assert.NotNull(await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(0m), CancellationToken.None));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new SaleService(database, new PromotionService(database), new KitService(database)).CompleteAsync(
                token,
                new CompleteSaleCommand(Guid.NewGuid(), [new SaleLineCommand(product.Id, 1m)], 25m),
                CancellationToken.None));

            Assert.Contains("No hay existencia suficiente", exception.Message);
            Assert.Equal(0m, await database.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());
            Assert.Empty(await database.InventoryMovements.Where(item => item.ProductId == product.Id).ToListAsync());
        }
        finally
        {
            var shiftIds = await database.Shifts.Where(item => item.RegisterId == register.Id).Select(item => item.Id).ToListAsync();
            database.Shifts.RemoveRange(database.Shifts.Where(item => shiftIds.Contains(item.Id)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Products.Remove(product);
            database.Registers.Remove(register);
            database.Users.Remove(user);
            store.BlockSalesWithoutStock = originalBlockSalesWithoutStock;
            await database.SaveChangesAsync();
        }
    }

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
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, RegisterId = register.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "SIN-" + suffix, NormalizedCode = "SIN-" + suffix.ToUpperInvariant(), Description = "Producto sin existencia", Price = 25m, Stock = 0m, IsActive = true };

        database.AddRange(store, user, register, session, product);
        await database.SaveChangesAsync();

        try
        {
            var shift = await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(0m), CancellationToken.None);
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

    [Fact]
    public async Task CompletesTemporaryCommonProductWithoutChangingInventory()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda producto temporal " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "TEMP_" + suffix, DisplayName = "Producto temporal", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, RegisterId = register.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "TEMP-" + suffix, NormalizedCode = "TEMP-" + suffix.ToUpperInvariant(), Description = "Articulo temporal", Price = 12m, Stock = 7m, IsCommonProduct = true, IsTemporary = true, IsActive = true };

        database.AddRange(store, user, register, session, product);
        await database.SaveChangesAsync();

        try
        {
            Assert.NotNull(await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(0m), CancellationToken.None));

            var operationId = Guid.NewGuid();
            var result = await new SaleService(database, new PromotionService(database), new KitService(database)).CompleteAsync(
                token,
                new CompleteSaleCommand(operationId, [new SaleLineCommand(product.Id, 1m)], 12m),
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(7m, await database.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());
            Assert.Equal(0, await database.InventoryMovements.CountAsync(item => item.OperationId == operationId));
            Assert.Equal(product.Id, await database.SaleLines.Where(item => item.SaleId == result!.SaleId).Select(item => item.ProductId).SingleAsync());
        }
        finally
        {
            var shiftIds = await database.Shifts.Where(item => item.RegisterId == register.Id).Select(item => item.Id).ToListAsync();
            var saleIds = await database.Sales.Where(item => shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            database.PrintJobs.RemoveRange(database.PrintJobs.Where(item => saleIds.Contains(item.SaleId)));
            database.Payments.RemoveRange(database.Payments.Where(item => saleIds.Contains(item.SaleId)));
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
