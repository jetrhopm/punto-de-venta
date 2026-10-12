using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class MobilePurchaseRequestIntegrationTests
{
    [Fact]
    public async Task MobileRequestDoesNotChangeInventoryUntilDesktopApproval()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync();
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "COMPRA_MOVIL_" + suffix, DisplayName = "Administrador compra móvil", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var desktopToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var mobileToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var deviceToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja compra móvil " + suffix, IsActive = true };
        var device = new MobileDeviceRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Compras móvil " + suffix, DeviceTokenHash = Hash(deviceToken), CreatedAtUtc = DateTimeOffset.UtcNow };
        var desktopSession = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, RegisterId = register.Id, TokenHash = Hash(desktopToken), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var mobileSession = new MobileSessionRecord { Id = Guid.NewGuid(), UserId = user.Id, MobileDeviceId = device.Id, TokenHash = Hash(mobileToken), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var shift = new ShiftRecord { Id = Guid.NewGuid(), RegisterId = register.Id, UserId = user.Id, InitialCash = 100m, Status = "Open", OpenedAtUtc = DateTimeOffset.UtcNow };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "MCOMPRA-" + suffix, NormalizedCode = "MCOMPRA-" + suffix.ToUpperInvariant(), Description = "Producto solicitud móvil", Price = 30m, Cost = 10m, Stock = 5m, IsActive = true };
        database.AddRange(user, register, device, desktopSession, mobileSession, shift, product);
        await database.SaveChangesAsync();

        var operationId = Guid.NewGuid();
        Guid requestId = Guid.Empty;
        Guid purchaseId = Guid.Empty;
        try
        {
            var requestService = new MobilePurchaseRequestService(database);
            var request = await requestService.CreateAsync(mobileToken, deviceToken,
                new CreateMobilePurchaseRequestCommand(operationId, null, true, 45m,
                    [new MobilePurchaseRequestLineCommand(product.Id, 3m, 15m)]), CancellationToken.None);

            Assert.NotNull(request);
            requestId = request!.Id;
            Assert.Equal("Pending", request.Status);
            Assert.Equal(5m, await database.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());
            Assert.False(await database.Purchases.AnyAsync(item => item.OperationId == operationId));
            Assert.Empty(await database.CashMovements.Where(item => item.ShiftId == shift.Id).ToListAsync());

            var approval = await new SupplierPurchaseService(database).ApproveMobileRequestAsync(desktopToken, requestId,
                new ApproveMobilePurchaseRequestCommand(null, true, 40m, "Entrega revisada",
                    [new MobilePurchaseApprovalLineCommand(product.Id, 2m, 15m, 30m, null)]), CancellationToken.None);

            Assert.NotNull(approval);
            purchaseId = approval!.Purchase.PurchaseId;
            Assert.Equal(30m, approval.Purchase.Total);
            Assert.True(approval.Purchase.PaidFromCashRegister);
            Assert.Equal(40m, approval.Purchase.CashWithdrawn);
            Assert.Equal(7m, await database.Products.Where(item => item.Id == product.Id).Select(item => item.Stock).SingleAsync());
            Assert.Equal("Approved", await database.MobilePurchaseRequests.Where(item => item.Id == requestId).Select(item => item.Status).SingleAsync());
            Assert.Equal(2m, await database.MobilePurchaseRequestLines.Where(item => item.RequestId == requestId).Select(item => item.Quantity).SingleAsync());
            var cashOut = await database.CashMovements.SingleAsync(item => item.ShiftId == shift.Id);
            Assert.Equal("Out", cashOut.Type);
            Assert.Equal(40m, cashOut.Amount);
        }
        finally
        {
            if (purchaseId != Guid.Empty)
            {
                await database.PurchaseLines.Where(item => item.PurchaseId == purchaseId).ExecuteDeleteAsync();
                await database.Purchases.Where(item => item.Id == purchaseId).ExecuteDeleteAsync();
            }
            await database.InventoryMovements.Where(item => item.OperationId == operationId).ExecuteDeleteAsync();
            if (requestId != Guid.Empty)
            {
                await database.MobilePurchaseRequestLines.Where(item => item.RequestId == requestId).ExecuteDeleteAsync();
                await database.MobilePurchaseRequests.Where(item => item.Id == requestId).ExecuteDeleteAsync();
            }
            await database.MobileSessions.Where(item => item.Id == mobileSession.Id).ExecuteDeleteAsync();
            await database.MobileDevices.Where(item => item.Id == device.Id).ExecuteDeleteAsync();
            await database.CashMovements.Where(item => item.ShiftId == shift.Id).ExecuteDeleteAsync();
            await database.Shifts.Where(item => item.Id == shift.Id).ExecuteDeleteAsync();
            await database.Sessions.Where(item => item.Id == desktopSession.Id).ExecuteDeleteAsync();
            await database.Products.Where(item => item.Id == product.Id).ExecuteDeleteAsync();
            await database.Registers.Where(item => item.Id == register.Id).ExecuteDeleteAsync();
            await database.Users.Where(item => item.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
