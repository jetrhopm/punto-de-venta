using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class RegisterOperationIsolationIntegrationTests
{
    [Fact]
    public async Task DraftsCashAndTicketsStayInTheRegisterFromTheAuthenticatedSession()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var tokenA = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenB = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda aislamiento " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var registerA = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja A " + suffix, IsActive = true };
        var registerB = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja B " + suffix, IsActive = true };
        var userA = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "CAJA_A_" + suffix, DisplayName = "Cajero A", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var userB = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "CAJA_B_" + suffix, DisplayName = "Cajero B", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var sessionA = Session(userA.Id, registerA.Id, tokenA);
        var sessionB = Session(userB.Id, registerB.Id, tokenB);
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "AISLADO-" + suffix, NormalizedCode = "AISLADO-" + suffix.ToUpperInvariant(), Description = "Producto aislado", Price = 20m, Stock = 20m, IsActive = true };
        database.AddRange(store, registerA, registerB, userA, userB, sessionA, sessionB, product);
        await database.SaveChangesAsync();

        try
        {
            var shifts = new ShiftService(database);
            var shiftA = await shifts.OpenAsync(tokenA, new OpenShiftCommand(100m), CancellationToken.None);
            var shiftB = await shifts.OpenAsync(tokenB, new OpenShiftCommand(200m), CancellationToken.None);
            Assert.NotNull(shiftA);
            Assert.NotNull(shiftB);
            Assert.Equal(registerA.Id, shiftA.RegisterId);
            Assert.Equal(registerB.Id, shiftB.RegisterId);

            var drafts = new SaleDraftService(database);
            var draftA = await drafts.CreateAsync(tokenA, CancellationToken.None);
            var draftB = await drafts.CreateAsync(tokenB, CancellationToken.None);
            Assert.NotNull(draftA);
            Assert.NotNull(draftB);
            await drafts.SaveLinesAsync(tokenA, draftA!.Id, new SaveSaleDraftLinesCommand([new SaleDraftLineCommand(product.Id, 1m)]), CancellationToken.None);
            await drafts.SaveLinesAsync(tokenB, draftB!.Id, new SaveSaleDraftLinesCommand([new SaleDraftLineCommand(product.Id, 2m)]), CancellationToken.None);

            Assert.Equal(draftA.Id, Assert.Single((await drafts.ListOpenAsync(tokenA, CancellationToken.None))!).Id);
            Assert.Equal(draftB.Id, Assert.Single((await drafts.ListOpenAsync(tokenB, CancellationToken.None))!).Id);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => drafts.SaveLinesAsync(tokenB, draftA.Id, new SaveSaleDraftLinesCommand([new SaleDraftLineCommand(product.Id, 1m)]), CancellationToken.None));

            var cash = new CashRegisterService(database);
            Assert.NotNull(await cash.AddMovementAsync(tokenA, new CashMovementCommand("In", 10m, "Cambio"), CancellationToken.None));
            Assert.NotNull(await cash.AddMovementAsync(tokenB, new CashMovementCommand("In", 5m, "Cambio"), CancellationToken.None));
            Assert.Equal(110m, (await cash.CurrentSummaryAsync(tokenA, CancellationToken.None))!.ExpectedCash);
            Assert.Equal(205m, (await cash.CurrentSummaryAsync(tokenB, CancellationToken.None))!.ExpectedCash);

            var sale = new SaleRecord { Id = Guid.NewGuid(), OperationId = Guid.NewGuid(), ShiftId = shiftA.ShiftId, Folio = Random.Shared.NextInt64(1, long.MaxValue / 2), Total = 20m, CreatedAtUtc = DateTimeOffset.UtcNow };
            database.Sales.Add(sale);
            database.SaleLines.Add(new SaleLineRecord { Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id, Quantity = 1m, UnitPrice = 20m, LineTotal = 20m });
            await database.SaveChangesAsync();
            var tickets = new TicketService(database);
            Assert.Equal(sale.Id, await tickets.LatestSaleIdAsync(tokenA, CancellationToken.None));
            Assert.Null(await tickets.LatestSaleIdAsync(tokenB, CancellationToken.None));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => tickets.GetDataAsync(tokenB, sale.Id, CancellationToken.None));
        }
        finally
        {
            var shiftIds = await database.Shifts.Where(item => item.RegisterId == registerA.Id || item.RegisterId == registerB.Id).Select(item => item.Id).ToListAsync();
            var saleIds = await database.Sales.Where(item => shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            var draftIds = await database.SaleDrafts.Where(item => shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            database.SaleDraftLines.RemoveRange(database.SaleDraftLines.Where(item => draftIds.Contains(item.DraftId)));
            database.SaleDrafts.RemoveRange(database.SaleDrafts.Where(item => draftIds.Contains(item.Id)));
            database.SaleLines.RemoveRange(database.SaleLines.Where(item => saleIds.Contains(item.SaleId)));
            database.Payments.RemoveRange(database.Payments.Where(item => saleIds.Contains(item.SaleId)));
            database.CashMovements.RemoveRange(database.CashMovements.Where(item => shiftIds.Contains(item.ShiftId)));
            database.Sales.RemoveRange(database.Sales.Where(item => saleIds.Contains(item.Id)));
            database.Shifts.RemoveRange(database.Shifts.Where(item => shiftIds.Contains(item.Id)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.Id == sessionA.Id || item.Id == sessionB.Id));
            database.Products.Remove(product);
            database.Registers.RemoveRange(registerA, registerB);
            database.Users.RemoveRange(userA, userB);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }

    private static SessionRecord Session(Guid userId, Guid registerId, string token) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        RegisterId = registerId,
        TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
    };
}
