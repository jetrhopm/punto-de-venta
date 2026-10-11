using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

/// <summary>Recepción multiproducto (plan 3.4.0): precios, efectivo retirado distinto al total e historial por cajero.</summary>
public sealed class PurchaseReceptionIntegrationTests
{
    [Fact]
    public async Task MultiProductReceptionAppliesPricingRecordsWithdrawnCashAndAppearsInHistory()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda recepción " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "RECEPCION_" + suffix, DisplayName = "Cajero recepción", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja recepción " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, RegisterId = register.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var supplier = new SupplierRecord { Id = Guid.NewGuid(), Name = "Sabritas prueba " + suffix, CreatedAtUtc = DateTimeOffset.UtcNow };
        // Con % propio: se actualizará el precio con el %.
        var chips = new ProductRecord { Id = Guid.NewGuid(), Code = "SAB" + suffix[..10], NormalizedCode = ("SAB" + suffix[..10]).ToUpperInvariant(), Description = "Papas prueba", Price = 25m, Cost = 10m, ProfitPercent = 20m, Stock = 4m, IsActive = true };
        // Sin %: conserva su precio de venta.
        var snack = new ProductRecord { Id = Guid.NewGuid(), Code = "750" + suffix[..10], NormalizedCode = ("750" + suffix[..10]).ToUpperInvariant(), Description = "Botana prueba", Price = 30m, Cost = 20m, ProfitPercent = 0m, Stock = 0m, IsActive = true };
        database.AddRange(store, user, register, session, supplier, chips, snack);
        await database.SaveChangesAsync();

        try
        {
            var service = new SupplierPurchaseService(database);
            var shift = await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(100m), CancellationToken.None);
            Assert.NotNull(shift);

            // Nunca se permite vender por debajo del precio del proveedor.
            await Assert.ThrowsAsync<ArgumentException>(() => service.ReceiveAsync(token, new ReceivePurchaseCommand(Guid.NewGuid(), supplier.Id, [new PurchaseLineCommand(snack.Id, 1m, 35m)]), CancellationToken.None));
            database.ChangeTracker.Clear();
            // Un producto repetido en la misma compra se rechaza.
            await Assert.ThrowsAsync<ArgumentException>(() => service.ReceiveAsync(token, new ReceivePurchaseCommand(Guid.NewGuid(), supplier.Id, [new PurchaseLineCommand(snack.Id, 1m, 22m), new PurchaseLineCommand(snack.Id, 1m, 22m)]), CancellationToken.None));
            database.ChangeTracker.Clear();

            var received = await service.ReceiveAsync(token, new ReceivePurchaseCommand(
                Guid.NewGuid(), supplier.Id,
                [new PurchaseLineCommand(chips.Id, 2m, 12m, ProfitPercentToApply: 50m), new PurchaseLineCommand(snack.Id, 3m, 22m)],
                PayFromCashRegister: true, CashWithdrawn: 50m, Notes: "El gerente pagó el resto"), CancellationToken.None);

            Assert.NotNull(received);
            Assert.Equal(90m, received!.Total);
            Assert.Equal(50m, received.CashWithdrawn);
            Assert.True(received.PaidFromCashRegister);

            database.ChangeTracker.Clear();
            var updatedChips = await database.Products.AsNoTracking().SingleAsync(item => item.Id == chips.Id);
            Assert.Equal(10.67m, updatedChips.Cost);   // (4×10 + 2×12) / 6
            Assert.Equal(16.01m, updatedChips.Price);  // 10.67 × 1.5
            Assert.Equal(50m, updatedChips.ProfitPercent);
            var updatedSnack = await database.Products.AsNoTracking().SingleAsync(item => item.Id == snack.Id);
            Assert.Equal(22m, updatedSnack.Cost);
            Assert.Equal(30m, updatedSnack.Price);

            var movement = await database.CashMovements.SingleAsync(item => item.ShiftId == shift!.ShiftId && item.Reason.StartsWith("Pago a proveedor:"));
            Assert.Equal(50m, movement.Amount);
            var cut = await new CashRegisterService(database).CurrentCutAsync(token, CancellationToken.None);
            Assert.Equal(50m, cut!.SupplierPayments);
            Assert.Equal(50m, cut.ExpectedCash);

            var history = await service.HistoryAsync(token, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1), user.Id, null, true, CancellationToken.None);
            var row = Assert.Single(history!);
            Assert.Equal("Cajero recepción", row.Cashier);
            Assert.Equal(2, row.LineCount);
            Assert.Equal(5m, row.Pieces);
            Assert.Equal(50m, row.CashWithdrawn);
            Assert.Equal("El gerente pagó el resto", row.Notes);
            var detail = await service.HistoryDetailAsync(token, row.Id, CancellationToken.None);
            Assert.Equal(2, detail!.Lines.Count);

            // Pagar con caja ya no exige proveedor: se registra como "Sin proveedor".
            database.ChangeTracker.Clear();
            var withoutSupplier = await service.ReceiveAsync(token, new ReceivePurchaseCommand(Guid.NewGuid(), null, [new PurchaseLineCommand(snack.Id, 1m, 22m)], PayFromCashRegister: true, CashWithdrawn: 10m), CancellationToken.None);
            Assert.True(withoutSupplier!.PaidFromCashRegister);
            Assert.True(await database.CashMovements.AnyAsync(item => item.ShiftId == shift!.ShiftId && item.Reason == "Pago a proveedor: Sin proveedor" && item.Amount == 10m));
        }
        finally
        {
            database.ChangeTracker.Clear();
            var purchaseIds = await database.Purchases.Where(item => item.UserId == user.Id).Select(item => item.Id).ToListAsync();
            var shiftIds = await database.Shifts.Where(item => item.RegisterId == register.Id).Select(item => item.Id).ToListAsync();
            database.PurchaseLines.RemoveRange(database.PurchaseLines.Where(item => purchaseIds.Contains(item.PurchaseId)));
            database.Purchases.RemoveRange(database.Purchases.Where(item => purchaseIds.Contains(item.Id)));
            database.InventoryMovements.RemoveRange(database.InventoryMovements.Where(item => item.ProductId == chips.Id || item.ProductId == snack.Id));
            database.CashMovements.RemoveRange(database.CashMovements.Where(item => shiftIds.Contains(item.ShiftId)));
            database.Shifts.RemoveRange(database.Shifts.Where(item => shiftIds.Contains(item.Id)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.Id == session.Id));
            database.Products.RemoveRange(database.Products.Where(item => item.Id == chips.Id || item.Id == snack.Id));
            database.Suppliers.RemoveRange(database.Suppliers.Where(item => item.Id == supplier.Id));
            database.Registers.RemoveRange(database.Registers.Where(item => item.Id == register.Id));
            database.Users.RemoveRange(database.Users.Where(item => item.Id == user.Id));
            database.Stores.RemoveRange(database.Stores.Where(item => item.Id == store.Id));
            await database.SaveChangesAsync();
        }
    }
}
