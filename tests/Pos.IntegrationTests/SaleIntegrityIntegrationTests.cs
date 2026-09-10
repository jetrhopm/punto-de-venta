using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class SaleIntegrityIntegrationTests
{
    [Fact]
    public async Task AppliesWholesaleAutomaticallyAtConfiguredMinimum()
    {
        var context = await SaleContext.CreateAsync();
        try
        {
            var belowMinimum = await context.Sales.CompleteAsync(
                context.Token,
                new CompleteSaleCommand(Guid.NewGuid(), [new SaleLineCommand(context.Product.Id, 2m)], 20m),
                CancellationToken.None);
            var atMinimum = await context.Sales.CompleteAsync(
                context.Token,
                new CompleteSaleCommand(Guid.NewGuid(), [new SaleLineCommand(context.Product.Id, 3m)], 24m),
                CancellationToken.None);

            Assert.Equal(20m, belowMinimum!.Total);
            Assert.Equal(24m, atMinimum!.Total);
            Assert.Equal(10m, await context.Database.SaleLines.Where(item => item.SaleId == belowMinimum.SaleId).Select(item => item.UnitPrice).SingleAsync());
            Assert.Equal(8m, await context.Database.SaleLines.Where(item => item.SaleId == atMinimum.SaleId).Select(item => item.UnitPrice).SingleAsync());
        }
        finally { await context.DisposeAsync(); }
    }

    [Fact]
    public async Task AppliesManualWholesaleBelowConfiguredMinimumForAuthorizedUser()
    {
        var context = await SaleContext.CreateAsync();
        try
        {
            var result = await context.Sales.CompleteAsync(
                context.Token,
                new CompleteSaleCommand(Guid.NewGuid(), [new SaleLineCommand(context.Product.Id, 1m, UseWholesale: true)], 8m),
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(8m, result.Total);
            Assert.Equal(8m, await context.Database.SaleLines.Where(item => item.SaleId == result.SaleId).Select(item => item.UnitPrice).SingleAsync());
        }
        finally { await context.DisposeAsync(); }
    }

    [Fact]
    public async Task RejectsManualWholesaleWithoutTheRequiredPermission()
    {
        var context = await SaleContext.CreateAsync();
        try
        {
            var user = await context.Database.Users.SingleAsync(item => item.Id == context.UserId);
            user.IsAdministrator = false;
            context.Database.Permissions.Add(new PermissionRecord { Id = Guid.NewGuid(), UserId = context.UserId, Code = "Sell" });
            await context.Database.SaveChangesAsync();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Sales.CompleteAsync(
                context.Token,
                new CompleteSaleCommand(Guid.NewGuid(), [new SaleLineCommand(context.Product.Id, 1m, UseWholesale: true)], 8m),
                CancellationToken.None));
        }
        finally { await context.DisposeAsync(); }
    }

    [Fact]
    public async Task CancellingMixedSaleRestoresInventoryAndOnlyCashPayment()
    {
        var context = await SaleContext.CreateAsync();
        try
        {
            var sale = await context.Sales.CompleteAsync(
                context.Token,
                new CompleteSaleCommand(Guid.NewGuid(), [new SaleLineCommand(context.Product.Id, 2m)], 10m, PaymentMethod: "Mixed", CardAmount: 7m, TransferAmount: 3m),
                CancellationToken.None);

            var result = await context.Reversals.CancelAsync(context.Token, new CancelSaleCommand(Guid.NewGuid(), sale!.SaleId, "Prueba de pago mixto"), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(10m, await context.Database.Products.Where(item => item.Id == context.Product.Id).Select(item => item.Stock).SingleAsync());
            Assert.Equal("Cancelled", await context.Database.Sales.Where(item => item.Id == sale.SaleId).Select(item => item.Status).SingleAsync());
            Assert.Equal(10m, await context.Database.CashMovements.Where(item => item.ShiftId == context.ShiftId).Select(item => item.Amount).SingleAsync());
            Assert.Single(await context.Database.InventoryMovements.Where(item => item.OperationId == result!.OperationId).ToListAsync());
        }
        finally { await context.DisposeAsync(); }
    }

    private sealed class SaleContext : IAsyncDisposable
    {
        private SaleContext(PosDbContext database, string token, ProductRecord product, Guid shiftId, Guid userId, Guid registerId, Guid storeId)
        {
            Database = database;
            Token = token;
            Product = product;
            ShiftId = shiftId;
            UserId = userId;
            RegisterId = registerId;
            StoreId = storeId;
            Sales = new SaleService(database, new PromotionService(database), new KitService(database));
            Reversals = new SaleReversalService(database, new KitService(database));
        }

        public PosDbContext Database { get; }
        public string Token { get; }
        public ProductRecord Product { get; }
        public Guid ShiftId { get; }
        public Guid UserId { get; }
        public Guid RegisterId { get; }
        public Guid StoreId { get; }
        public SaleService Sales { get; }
        public SaleReversalService Reversals { get; }

        public static async Task<SaleContext> CreateAsync()
        {
            var database = new PosDbContextFactory().CreateDbContext([]);
            await database.Database.MigrateAsync();
            var suffix = Guid.NewGuid().ToString("N");
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda integridad " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
            var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "INT_" + suffix, DisplayName = "Prueba integridad", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
            var register = new RegisterRecord { Id = Guid.NewGuid(), StoreId = store.Id, Name = "Caja " + suffix, IsActive = true };
            var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
            var product = new ProductRecord { Id = Guid.NewGuid(), Code = "INT-" + suffix, NormalizedCode = ("INT-" + suffix).ToUpperInvariant(), Description = "Producto integridad", Price = 10m, WholesalePrice = 8m, WholesaleMinimumQuantity = 3m, Stock = 10m, IsActive = true };
            database.AddRange(store, user, register, session, product);
            await database.SaveChangesAsync();
            var shift = await new ShiftService(database).OpenAsync(token, new OpenShiftCommand(register.Id, 0m), CancellationToken.None);
            return new SaleContext(database, token, product, shift!.ShiftId, user.Id, register.Id, store.Id);
        }

        public async ValueTask DisposeAsync()
        {
            var saleIds = await Database.Sales.Where(item => item.ShiftId == ShiftId).Select(item => item.Id).ToListAsync();
            Database.PrintJobs.RemoveRange(Database.PrintJobs.Where(item => saleIds.Contains(item.SaleId)));
            Database.Payments.RemoveRange(Database.Payments.Where(item => saleIds.Contains(item.SaleId)));
            Database.InventoryMovements.RemoveRange(Database.InventoryMovements.Where(item => saleIds.Contains(item.SaleId ?? Guid.Empty)));
            Database.SaleReversals.RemoveRange(Database.SaleReversals.Where(item => saleIds.Contains(item.SaleId)));
            Database.CashMovements.RemoveRange(Database.CashMovements.Where(item => item.ShiftId == ShiftId));
            Database.SaleLines.RemoveRange(Database.SaleLines.Where(item => saleIds.Contains(item.SaleId)));
            Database.Sales.RemoveRange(Database.Sales.Where(item => saleIds.Contains(item.Id)));
            Database.Shifts.RemoveRange(Database.Shifts.Where(item => item.Id == ShiftId));
            Database.Sessions.RemoveRange(Database.Sessions.Where(item => item.UserId == UserId));
            Database.Products.Remove(Product);
            Database.Registers.RemoveRange(Database.Registers.Where(item => item.Id == RegisterId));
            Database.Users.RemoveRange(Database.Users.Where(item => item.Id == UserId));
            Database.Stores.RemoveRange(Database.Stores.Where(item => item.Id == StoreId));
            await Database.SaveChangesAsync();
            await Database.DisposeAsync();
        }
    }
}
