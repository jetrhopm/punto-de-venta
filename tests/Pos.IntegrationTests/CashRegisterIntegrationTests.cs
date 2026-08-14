using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class CashRegisterIntegrationTests
{
    [Fact]
    public async Task ClosesConsecutiveShiftsAndCountsOnlyCashPayments()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda corte " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "CORTE_" + suffix, DisplayName = "Prueba de corte", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };

        database.AddRange(store, user, register, session);
        await database.SaveChangesAsync();

        try
        {
            var shifts = new ShiftService(database);
            var cash = new CashRegisterService(database);
            var first = await shifts.OpenAsync(token, new OpenShiftCommand(register.Id, 500m), CancellationToken.None);
            Assert.NotNull(first);

            var cashSale = new SaleRecord { Id = Guid.NewGuid(), OperationId = Guid.NewGuid(), ShiftId = first.ShiftId, Total = 100m, Status = "Completed", CreatedAtUtc = DateTimeOffset.UtcNow };
            var cardSale = new SaleRecord { Id = Guid.NewGuid(), OperationId = Guid.NewGuid(), ShiftId = first.ShiftId, Total = 80m, Status = "Completed", CreatedAtUtc = DateTimeOffset.UtcNow };
            database.Sales.AddRange(cashSale, cardSale);
            database.Payments.AddRange(
                new PaymentRecord { Id = Guid.NewGuid(), SaleId = cashSale.Id, Method = "Cash", Amount = 100m, Received = 100m },
                new PaymentRecord { Id = Guid.NewGuid(), SaleId = cardSale.Id, Method = "Card", Amount = 80m, Received = 80m });
            database.CashMovements.AddRange(
                new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = first.ShiftId, Type = "In", Amount = 20m, Reason = "Cambio adicional", CreatedAtUtc = DateTimeOffset.UtcNow },
                new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = first.ShiftId, Type = "Out", Amount = 5m, Reason = "Gasto menor", CreatedAtUtc = DateTimeOffset.UtcNow });
            await database.SaveChangesAsync();

            var firstSummary = await cash.CloseAsync(token, new CloseShiftCommand(615m), CancellationToken.None);
            Assert.NotNull(firstSummary);
            Assert.Equal(615m, firstSummary.ExpectedCash);
            Assert.Equal(0m, firstSummary.Difference);

            var second = await shifts.OpenAsync(token, new OpenShiftCommand(register.Id, 300m), CancellationToken.None);
            Assert.NotNull(second);
            var secondSummary = await cash.CloseAsync(token, new CloseShiftCommand(300m), CancellationToken.None);

            Assert.NotNull(secondSummary);
            Assert.Equal(2, await database.Shifts.CountAsync(item => item.RegisterId == register.Id && item.Status == "Closed"));
            Assert.False(await database.Shifts.AnyAsync(item => item.RegisterId == register.Id && item.Status == "Open"));
        }
        finally
        {
            var shiftIds = await database.Shifts.Where(item => item.RegisterId == register.Id).Select(item => item.Id).ToListAsync();
            var saleIds = await database.Sales.Where(item => shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            database.Payments.RemoveRange(database.Payments.Where(item => saleIds.Contains(item.SaleId)));
            database.CashMovements.RemoveRange(database.CashMovements.Where(item => shiftIds.Contains(item.ShiftId)));
            database.Sales.RemoveRange(database.Sales.Where(item => saleIds.Contains(item.Id)));
            database.Shifts.RemoveRange(database.Shifts.Where(item => shiftIds.Contains(item.Id)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Registers.Remove(register);
            database.Users.Remove(user);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }
}
