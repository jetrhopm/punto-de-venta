using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class CashRegisterIntegrationTests
{
    [Fact]
    public async Task DoesNotOpenTheSameRegisterForAnotherCashierAndIdentifiesTheOpenShiftOwner()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var firstToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secondToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda relevo " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var firstCashier = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "PRIMERO_" + suffix, DisplayName = "Cajero que dejó la caja", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var secondCashier = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "SEGUNDO_" + suffix, DisplayName = "Cajero que intenta entrar", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja relevo " + suffix, IsActive = true };
        database.AddRange(
            store,
            firstCashier,
            secondCashier,
            register,
            new SessionRecord { Id = Guid.NewGuid(), UserId = firstCashier.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(firstToken))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) },
            new SessionRecord { Id = Guid.NewGuid(), UserId = secondCashier.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secondToken))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) });
        await database.SaveChangesAsync();

        try
        {
            var shifts = new ShiftService(database);
            Assert.NotNull(await shifts.OpenAsync(firstToken, new OpenShiftCommand(register.Id, 0m), CancellationToken.None));

            var availability = await shifts.GetRegisterAvailabilityAsync(register.Id, CancellationToken.None);
            Assert.NotNull(availability);
            Assert.Equal(firstCashier.DisplayName, availability!.OpenShiftUserName);
            Assert.NotNull(availability.OpenedAtUtc);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => shifts.OpenAsync(secondToken, new OpenShiftCommand(register.Id, 0m), CancellationToken.None));
            Assert.Contains(firstCashier.DisplayName, exception.Message);
        }
        finally
        {
            database.Shifts.RemoveRange(database.Shifts.Where(item => item.RegisterId == register.Id));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == firstCashier.Id || item.UserId == secondCashier.Id));
            database.Registers.Remove(register);
            database.Users.RemoveRange(firstCashier, secondCashier);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CashierCanReadCutSettingsAndCloseWithTemporaryCloseShiftPermission()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var hasher = new PasswordHasher<UserRecord>();
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda autorización " + suffix, BusinessType = "Pruebas", RequireCashCountOnClose = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var administrator = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("ADMIN_CUT_" + suffix).ToUpperInvariant(), DisplayName = "Administrador de corte", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        administrator.PasswordHash = hasher.HashPassword(administrator, "clave-admin");
        var cashier = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = ("CASHIER_CUT_" + suffix).ToUpperInvariant(), DisplayName = "Cajero sin corte", IsAdministrator = false, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        cashier.PasswordHash = hasher.HashPassword(cashier, "clave-cajero");
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja autorización " + suffix, IsActive = true };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = cashier.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        database.AddRange(store, administrator, cashier, register, session);
        await database.SaveChangesAsync();

        try
        {
            Assert.NotNull(await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(register.Id, 250m), CancellationToken.None));
            Assert.Null(await new CutSettingsService(database).GetAsync(token, CancellationToken.None));
            Assert.Null(await new CashRegisterService(database).CloseAsync(token, new CloseShiftCommand(250m), CancellationToken.None));

            var grant = await new AuthenticationService(database, hasher).GrantTemporaryPermissionAsync(
                token,
                new TemporaryPermissionAuthorizationCommand(administrator.NormalizedUserName, "clave-admin", "CloseShift"),
                CancellationToken.None);

            Assert.True(grant.Succeeded);
            Assert.NotNull(grant.Authorization);
            Assert.DoesNotContain("ConfigureStore", await database.Permissions.Where(item => item.UserId == cashier.Id).Select(item => item.Code).ToListAsync());
            Assert.NotNull(await new CutSettingsService(database).GetAsync(token, CancellationToken.None));
            Assert.Null(await new CashRegisterService(database).CloseAsync(token, new CloseShiftCommand(250m), CancellationToken.None));
            Assert.True(await database.Shifts.AnyAsync(item => item.RegisterId == register.Id && item.Status == "Open"));
            Assert.NotNull(await new CashRegisterService(database).CloseAsync(token, new CloseShiftCommand(250m, grant.Authorization!.GrantId), CancellationToken.None));
            Assert.False(await database.Shifts.AnyAsync(item => item.RegisterId == register.Id && item.Status == "Open"));
            Assert.False(await database.Permissions.IgnoreQueryFilters().AnyAsync(item => item.Id == grant.Authorization.GrantId));

            Assert.NotNull(await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(register.Id, 100m), CancellationToken.None));
            var expiredGrant = new PermissionRecord
            {
                Id = Guid.NewGuid(),
                UserId = cashier.Id,
                Code = "CloseShift",
                GrantedByUserId = administrator.Id,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            };
            database.Permissions.Add(expiredGrant);
            await database.SaveChangesAsync();

            Assert.Null(await new CashRegisterService(database).CloseAsync(token, new CloseShiftCommand(100m, expiredGrant.Id), CancellationToken.None));
            Assert.True(await database.Shifts.AnyAsync(item => item.RegisterId == register.Id && item.Status == "Open"));

            database.Permissions.Remove(expiredGrant);
            var unrelatedGrant = new PermissionRecord
            {
                Id = Guid.NewGuid(),
                UserId = cashier.Id,
                Code = "ConfigureStore",
                GrantedByUserId = administrator.Id,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            };
            database.Permissions.Add(unrelatedGrant);
            await database.SaveChangesAsync();

            Assert.Null(await new CashRegisterService(database).CloseAsync(token, new CloseShiftCommand(100m, unrelatedGrant.Id), CancellationToken.None));
            Assert.True(await database.Shifts.AnyAsync(item => item.RegisterId == register.Id && item.Status == "Open"));

            database.Permissions.Remove(unrelatedGrant);
            database.Permissions.Add(new PermissionRecord { Id = Guid.NewGuid(), UserId = cashier.Id, Code = "CloseShift" });
            await database.SaveChangesAsync();

            Assert.NotNull(await new CashRegisterService(database).CloseAsync(token, new CloseShiftCommand(100m), CancellationToken.None));
            Assert.False(await database.Shifts.AnyAsync(item => item.RegisterId == register.Id && item.Status == "Open"));
        }
        finally
        {
            database.Permissions.RemoveRange(database.Permissions.IgnoreQueryFilters().Where(item => item.UserId == cashier.Id || item.UserId == administrator.Id));
            database.Shifts.RemoveRange(database.Shifts.Where(item => item.RegisterId == register.Id));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == cashier.Id || item.UserId == administrator.Id));
            database.Registers.Remove(register);
            database.Users.RemoveRange(administrator, cashier);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }

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

            var firstFolio = Random.Shared.NextInt64(1, long.MaxValue / 2);
            var cashSale = new SaleRecord { Id = Guid.NewGuid(), OperationId = Guid.NewGuid(), ShiftId = first.ShiftId, Folio = firstFolio, Total = 100m, Status = "Completed", CreatedAtUtc = DateTimeOffset.UtcNow };
            var cardSale = new SaleRecord { Id = Guid.NewGuid(), OperationId = Guid.NewGuid(), ShiftId = first.ShiftId, Folio = firstFolio + 1, Total = 80m, Status = "Completed", CreatedAtUtc = DateTimeOffset.UtcNow };
            database.Sales.AddRange(cashSale, cardSale);
            database.Payments.AddRange(
                new PaymentRecord { Id = Guid.NewGuid(), SaleId = cashSale.Id, Method = "Cash", Amount = 100m, Received = 100m },
                new PaymentRecord { Id = Guid.NewGuid(), SaleId = cardSale.Id, Method = "Card", Amount = 80m, Received = 80m });
            database.CashMovements.AddRange(
                new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = first.ShiftId, Type = "In", Amount = 20m, Reason = "Cambio adicional", CreatedAtUtc = DateTimeOffset.UtcNow },
                new CashMovementRecord { Id = Guid.NewGuid(), ShiftId = first.ShiftId, Type = "Out", Amount = 5m, Reason = "Gasto menor", CreatedAtUtc = DateTimeOffset.UtcNow });
            await database.SaveChangesAsync();

            var preview = await cash.CurrentSummaryAsync(token, CancellationToken.None);
            Assert.NotNull(preview);
            Assert.Equal(615m, preview.ExpectedCash);

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
            await database.SaveChangesAsync();

            database.Shifts.RemoveRange(database.Shifts.Where(item => shiftIds.Contains(item.Id)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Registers.Remove(register);
            database.Users.Remove(user);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }
}
