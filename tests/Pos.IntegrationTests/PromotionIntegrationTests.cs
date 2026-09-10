using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class PromotionIntegrationTests
{
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
