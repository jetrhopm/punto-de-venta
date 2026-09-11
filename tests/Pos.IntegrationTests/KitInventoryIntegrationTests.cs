using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class KitInventoryIntegrationTests
{
    [Fact]
    public async Task IncompleteKitCannotBeExpandedForSale()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();
        var code = "KIT-INCOMPLETO-" + Guid.NewGuid().ToString("N");
        var kit = new ProductRecord { Id = Guid.NewGuid(), Code = code, NormalizedCode = code, Description = "Kit sin artículos", IsKit = true, IsActive = true };
        database.Products.Add(kit);
        await database.SaveChangesAsync();
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new KitService(database).ExpandAsync(kit.Id, 1m, CancellationToken.None));
            Assert.Contains("no tiene componentes", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { database.Products.Remove(kit); await database.SaveChangesAsync(); }
    }

    [Fact]
    public async Task SaleOfKitDiscountsComponentsAndCancellationRestoresThem()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda kits " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "KIT_" + suffix, DisplayName = "Prueba kits", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, RegisterId = register.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var component = new ProductRecord { Id = Guid.NewGuid(), Code = "COMP-" + suffix, NormalizedCode = ("COMP-" + suffix).ToUpperInvariant(), Description = "Componente del kit", Price = 8m, Stock = 10m, IsActive = true };
        var kit = new ProductRecord { Id = Guid.NewGuid(), Code = "KIT-" + suffix, NormalizedCode = ("KIT-" + suffix).ToUpperInvariant(), Description = "Kit de prueba", Price = 25m, Stock = 10m, IsKit = true, IsActive = true };
        database.AddRange(store, user, register, session, component, kit);
        await database.SaveChangesAsync();

        try
        {
            Assert.NotNull(await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(50m), CancellationToken.None));
            var kits = new KitService(database);
            var configured = await kits.SetAsync(token, new KitCommand(kit.Id, [new KitComponentCommand(component.Id, 2m)]), CancellationToken.None);
            Assert.NotNull(configured);

            var saleOperation = Guid.NewGuid();
            var sale = await new SaleService(database, new PromotionService(database), kits).CompleteAsync(
                token, new CompleteSaleCommand(saleOperation, [new SaleLineCommand(kit.Id, 1m)], 25m), CancellationToken.None);

            Assert.NotNull(sale);
            Assert.Equal(10m, await database.Products.Where(item => item.Id == kit.Id).Select(item => item.Stock).SingleAsync());
            Assert.Equal(8m, await database.Products.Where(item => item.Id == component.Id).Select(item => item.Stock).SingleAsync());
            var saleMovement = await database.InventoryMovements.SingleAsync(item => item.OperationId == saleOperation);
            Assert.Equal(component.Id, saleMovement.ProductId);
            Assert.Equal(-2m, saleMovement.Quantity);
            Assert.Equal("KitSale", saleMovement.Reason);

            var cancelOperation = Guid.NewGuid();
            var cancelled = await new SaleReversalService(database, kits).CancelAsync(token, new CancelSaleCommand(cancelOperation, sale!.SaleId, "Prueba de reversión"), CancellationToken.None);

            Assert.NotNull(cancelled);
            Assert.Equal(10m, await database.Products.Where(item => item.Id == component.Id).Select(item => item.Stock).SingleAsync());
            Assert.Equal("Cancelled", await database.Sales.Where(item => item.Id == sale.SaleId).Select(item => item.Status).SingleAsync());
            Assert.Equal(2m, await database.InventoryMovements.Where(item => item.OperationId == cancelOperation).Select(item => item.Quantity).SingleAsync());
        }
        finally
        {
            var shiftIds = await database.Shifts.Where(item => item.RegisterId == register.Id).Select(item => item.Id).ToListAsync();
            var saleIds = await database.Sales.Where(item => shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            database.KitComponents.RemoveRange(database.KitComponents.Where(item => item.KitProductId == kit.Id || item.ComponentProductId == component.Id));
            database.PrintJobs.RemoveRange(database.PrintJobs.Where(item => saleIds.Contains(item.SaleId)));
            database.Payments.RemoveRange(database.Payments.Where(item => saleIds.Contains(item.SaleId)));
            database.CashMovements.RemoveRange(database.CashMovements.Where(item => shiftIds.Contains(item.ShiftId)));
            database.InventoryMovements.RemoveRange(database.InventoryMovements.Where(item => saleIds.Contains(item.SaleId ?? Guid.Empty)));
            database.SaleReversals.RemoveRange(database.SaleReversals.Where(item => saleIds.Contains(item.SaleId)));
            database.SaleLines.RemoveRange(database.SaleLines.Where(item => saleIds.Contains(item.SaleId)));
            database.Sales.RemoveRange(database.Sales.Where(item => saleIds.Contains(item.Id)));
            database.Shifts.RemoveRange(database.Shifts.Where(item => shiftIds.Contains(item.Id)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Products.RemoveRange(database.Products.Where(item => item.Id == kit.Id || item.Id == component.Id));
            database.Registers.Remove(register);
            database.Users.Remove(user);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }
}
