using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class ConcurrentInventoryIntegrationTests
{
    [Fact]
    public async Task ConcurrentSalesFromDifferentRegistersPreserveEveryInventoryMovement()
    {
        await using var setup = new PosDbContextFactory().CreateDbContext([]);
        await setup.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var tokenA = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenB = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var operationA = Guid.NewGuid();
        var operationB = Guid.NewGuid();
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda concurrencia " + suffix, BusinessType = "Pruebas", InventoryEnabled = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var registerA = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja concurrencia A " + suffix, IsActive = true };
        var registerB = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja concurrencia B " + suffix, IsActive = true };
        var userA = User("CON_A_" + suffix, "Cajero concurrente A");
        var userB = User("CON_B_" + suffix, "Cajero concurrente B");
        var sessionA = Session(userA.Id, registerA.Id, tokenA);
        var sessionB = Session(userB.Id, registerB.Id, tokenB);
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "CON-" + suffix, NormalizedCode = ("CON-" + suffix).ToUpperInvariant(), Description = "Producto concurrente", Price = 10m, Stock = 1m, IsActive = true };
        setup.AddRange(store, registerA, registerB, userA, userB, sessionA, sessionB, product);
        await setup.SaveChangesAsync();

        try
        {
            Assert.NotNull(await new ShiftService(setup).OpenAsync(tokenA, new OpenShiftCommand(0m), CancellationToken.None));
            Assert.NotNull(await new ShiftService(setup).OpenAsync(tokenB, new OpenShiftCommand(0m), CancellationToken.None));

            var releaseSales = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var saleA = CompleteAsync(tokenA, operationA, product.Id, releaseSales.Task);
            var saleB = CompleteAsync(tokenB, operationB, product.Id, releaseSales.Task);
            releaseSales.SetResult(true);

            var results = await Task.WhenAll(saleA, saleB);
            Assert.All(results, result => Assert.NotNull(result));

            await using var verification = new PosDbContextFactory().CreateDbContext([]);
            Assert.Equal(-1m, await verification.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());
            var movements = await verification.InventoryMovements
                .Where(item => item.OperationId == operationA || item.OperationId == operationB)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync();
            Assert.Equal(2, movements.Count);
            Assert.Equal(1m, movements[0].StockBefore);
            Assert.Equal(0m, movements[0].StockAfter);
            Assert.Equal(0m, movements[1].StockBefore);
            Assert.Equal(-1m, movements[1].StockAfter);
        }
        finally
        {
            var shiftIds = await setup.Shifts.Where(item => item.RegisterId == registerA.Id || item.RegisterId == registerB.Id).Select(item => item.Id).ToListAsync();
            var saleIds = await setup.Sales.Where(item => shiftIds.Contains(item.ShiftId)).Select(item => item.Id).ToListAsync();
            setup.PrintJobs.RemoveRange(setup.PrintJobs.Where(item => saleIds.Contains(item.SaleId)));
            setup.Payments.RemoveRange(setup.Payments.Where(item => saleIds.Contains(item.SaleId)));
            setup.InventoryMovements.RemoveRange(setup.InventoryMovements.Where(item => item.OperationId == operationA || item.OperationId == operationB));
            setup.SaleLines.RemoveRange(setup.SaleLines.Where(item => saleIds.Contains(item.SaleId)));
            setup.Sales.RemoveRange(setup.Sales.Where(item => saleIds.Contains(item.Id)));
            setup.Shifts.RemoveRange(setup.Shifts.Where(item => shiftIds.Contains(item.Id)));
            setup.Sessions.RemoveRange(setup.Sessions.Where(item => item.Id == sessionA.Id || item.Id == sessionB.Id));
            setup.Products.Remove(product);
            setup.Registers.RemoveRange(registerA, registerB);
            setup.Users.RemoveRange(userA, userB);
            setup.Stores.Remove(store);
            await setup.SaveChangesAsync();
        }
    }

    private static async Task<CompleteSaleResult?> CompleteAsync(string token, Guid operationId, Guid productId, Task start)
    {
        await start;
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        var sales = new SaleService(database, new PromotionService(database), new KitService(database));
        return await sales.CompleteAsync(token, new CompleteSaleCommand(operationId, [new SaleLineCommand(productId, 1m)], 10m), CancellationToken.None);
    }

    private static UserRecord User(string userName, string displayName) => new()
    {
        Id = Guid.NewGuid(),
        NormalizedUserName = userName,
        DisplayName = displayName,
        PasswordHash = "test",
        IsAdministrator = true,
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

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
