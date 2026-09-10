using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class PromotionIntegrationTests
{
    [Fact]
    public async Task CalculatesPercentFixedAmountAndBuyPayPromotions()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var products = new[]
        {
            new ProductRecord { Id = Guid.NewGuid(), Code = "PERCENT-" + suffix, NormalizedCode = ("PERCENT-" + suffix).ToUpperInvariant(), Description = "Producto porcentaje", Price = 100m, IsActive = true },
            new ProductRecord { Id = Guid.NewGuid(), Code = "FIXED-" + suffix, NormalizedCode = ("FIXED-" + suffix).ToUpperInvariant(), Description = "Producto descuento fijo", Price = 100m, IsActive = true },
            new ProductRecord { Id = Guid.NewGuid(), Code = "BUYPAY-" + suffix, NormalizedCode = ("BUYPAY-" + suffix).ToUpperInvariant(), Description = "Producto compra y paga", Price = 100m, IsActive = true }
        };
        database.Products.AddRange(products);
        database.Promotions.AddRange(
            new PromotionRecord { Id = Guid.NewGuid(), ProductId = products[0].Id, Name = "Porcentaje " + suffix, Percent = 15m, IsActive = true },
            new PromotionRecord { Id = Guid.NewGuid(), ProductId = products[1].Id, Name = "Fijo " + suffix, DiscountAmount = 12m, IsActive = true },
            new PromotionRecord { Id = Guid.NewGuid(), ProductId = products[2].Id, Name = "Compra paga " + suffix, BuyQuantity = 3m, PayQuantity = 2m, IsActive = true });
        await database.SaveChangesAsync();

        try
        {
            var service = new PromotionService(database);
            var percent = await service.CalculateAsync(products[0].Id, 100m, DateTimeOffset.UtcNow, CancellationToken.None, 2m);
            var fixedAmount = await service.CalculateAsync(products[1].Id, 100m, DateTimeOffset.UtcNow, CancellationToken.None, 2m);
            var buyPay = await service.CalculateAsync(products[2].Id, 100m, DateTimeOffset.UtcNow, CancellationToken.None, 3m);

            Assert.Equal(170m, percent.Total);
            Assert.Equal(85m, percent.UnitPrice);
            Assert.Equal("Porcentaje " + suffix, percent.PromotionName);
            Assert.Equal(176m, fixedAmount.Total);
            Assert.Equal(88m, fixedAmount.UnitPrice);
            Assert.Equal("Fijo " + suffix, fixedAmount.PromotionName);
            Assert.Equal(200m, buyPay.Total);
            Assert.Equal("Compra paga " + suffix, buyPay.PromotionName);
        }
        finally
        {
            var ids = products.Select(item => item.Id).ToArray();
            database.Promotions.RemoveRange(database.Promotions.Where(item => ids.Contains(item.ProductId)));
            database.Products.RemoveRange(products);
            await database.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task UpdatingAnInactivePromotionKeepsItsStatusAndReturnsProductInformation()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var store = new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda promociones " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow };
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "PROMO_" + suffix, DisplayName = "Prueba promociones", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = "PROMO-" + suffix, NormalizedCode = ("PROMO-" + suffix).ToUpperInvariant(), Description = "Producto de promoción", Price = 25m, IsActive = true };
        database.AddRange(store, user, session, product);
        await database.SaveChangesAsync();

        try
        {
            var service = new PromotionService(database);
            var created = await service.CreateAsync(token, new PromotionCommand(product.Id, "Promoción " + suffix, Percent: 10m), CancellationToken.None);
            Assert.NotNull(created);

            Assert.True(await service.SetStatusAsync(token, created.Id, new PromotionStatusCommand(false), CancellationToken.None));
            var updated = await service.UpdateAsync(token, created.Id, new PromotionCommand(product.Id, "Promoción editada " + suffix, DiscountAmount: 5m), CancellationToken.None);

            Assert.NotNull(updated);
            Assert.False(updated.IsActive);
            Assert.Equal(product.Code, updated.ProductCode);
            Assert.Equal(product.Description, updated.ProductDescription);
            Assert.Equal(5m, updated.DiscountAmount);
            Assert.Equal(0m, updated.Percent);

            var byName = await service.ListAsync(token, null, true, "editada", CancellationToken.None);
            var byProduct = await service.ListAsync(token, null, true, product.Code, CancellationToken.None);
            Assert.Contains(byName!, item => item.Id == created.Id);
            Assert.Contains(byProduct!, item => item.Id == created.Id);
        }
        finally
        {
            database.Promotions.RemoveRange(database.Promotions.Where(item => item.ProductId == product.Id));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Products.Remove(product);
            database.Users.Remove(user);
            database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }
}
