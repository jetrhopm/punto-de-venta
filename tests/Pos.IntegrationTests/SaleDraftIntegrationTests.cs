using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class SaleDraftIntegrationTests
{
    [Fact]
    public void TrialClockDoesNotAllowReturningToDemoAfterItExpired()
    {
        var started = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var expired = TrialClockPolicy.Evaluate(started, started, started.AddMinutes(31), TimeSpan.FromMinutes(30));
        var rolledBack = TrialClockPolicy.Evaluate(started, expired.LastSeenAtUtc, started.AddMinutes(10), TimeSpan.FromMinutes(30));

        Assert.False(expired.IsActive);
        Assert.Equal("trial_expired", expired.State);
        Assert.False(rolledBack.IsActive);
        Assert.Equal("trial_clock_changed", rolledBack.State);
    }

    [Fact]
    public async Task SavesAndRecoversTicketWithoutAffectingInventoryOrCashUntilItIsConfirmed()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda tickets " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "TICKETS_" + suffix, DisplayName = "Prueba de tickets", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "TICKET-" + suffix, NormalizedCode = "TICKET-" + suffix.ToUpperInvariant(), Description = "Producto de ticket", Price = 18m, Stock = 4m, IsActive = true };
        database.AddRange(store, user, register, session, product);
        await database.SaveChangesAsync();

        try
        {
            Assert.NotNull(await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(register.Id, 50m), CancellationToken.None));
            var drafts = new SaleDraftService(database);
            var draft = await drafts.CreateAsync(token, CancellationToken.None);
            Assert.NotNull(draft);

            await drafts.SaveLinesAsync(token, draft!.Id, new SaveSaleDraftLinesCommand([new SaleDraftLineCommand(product.Id, 2m)]), CancellationToken.None);
            var recovered = await drafts.ListOpenAsync(token, CancellationToken.None);
            var shiftId = await database.Shifts.Where(item => item.RegisterId == register.Id && item.Status == "Open").Select(item => item.Id).SingleAsync();
            var recoveredTicket = Assert.Single(recovered!);
            Assert.Equal(2m, recoveredTicket.Lines.Single().Quantity);
            Assert.Equal(4m, await database.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());
            Assert.Empty(await database.Sales.Where(item => item.ShiftId == shiftId).ToListAsync());
            Assert.Empty(await database.CashMovements.Where(item => item.ShiftId == shiftId).ToListAsync());

            Assert.NotNull(await new CashRegisterService(database).CloseAsync(token, new CloseShiftCommand(50m), CancellationToken.None));
            Assert.NotNull(await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(register.Id, 30m), CancellationToken.None));
            var resumedDrafts = await drafts.ListOpenAsync(token, CancellationToken.None);
            Assert.NotNull(resumedDrafts);
            var resumed = Assert.Single(resumedDrafts);
            var resumedShiftId = await database.Shifts.Where(item => item.RegisterId == register.Id && item.Status == "Open").Select(item => item.Id).SingleAsync();
            Assert.Equal(draft.Id, resumed.Id);
            Assert.Equal(resumedShiftId, await database.SaleDrafts.Where(item => item.Id == draft.Id).Select(item => item.ShiftId).SingleAsync());

            var sales = new SaleService(database, new PromotionService(database), new KitService(database));
            var command = new CompleteSaleCommand(draft.OperationId, [new SaleLineCommand(product.Id, 2m)], 36m, DraftId: draft.Id);
            var first = await sales.CompleteAsync(token, command, CancellationToken.None);
            var retry = await sales.CompleteAsync(token, command, CancellationToken.None);

            Assert.NotNull(first);
            Assert.NotNull(retry);
            Assert.True(retry.Existing);
            Assert.Single(await database.Sales.Where(item => item.OperationId == draft.OperationId).ToListAsync());
            Assert.Equal(2m, await database.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());
            Assert.Equal("Completed", await database.SaleDrafts.Where(item => item.Id == draft.Id).Select(item => item.Status).SingleAsync());
        }
        finally
        {
            var shiftIds = await database.Shifts.Where(item => item.RegisterId == register.Id).Select(item => item.Id).ToListAsync();
            var saleIds = await database.Sales.Where(item => shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            var draftIds = await database.SaleDrafts.Where(item => item.ShiftId != Guid.Empty && shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            database.PrintJobs.RemoveRange(database.PrintJobs.Where(item => saleIds.Contains(item.SaleId)));
            database.Payments.RemoveRange(database.Payments.Where(item => saleIds.Contains(item.SaleId)));
            database.CashMovements.RemoveRange(database.CashMovements.Where(item => shiftIds.Contains(item.ShiftId)));
            database.InventoryMovements.RemoveRange(database.InventoryMovements.Where(item => saleIds.Contains(item.SaleId ?? Guid.Empty)));
            database.SaleLines.RemoveRange(database.SaleLines.Where(item => saleIds.Contains(item.SaleId)));
            database.SaleDraftLines.RemoveRange(database.SaleDraftLines.Where(item => draftIds.Contains(item.DraftId)));
            database.SaleDrafts.RemoveRange(database.SaleDrafts.Where(item => draftIds.Contains(item.Id)));
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
