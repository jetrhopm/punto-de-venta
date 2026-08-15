using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record PromotionCommand(Guid ProductId, string Name, decimal Percent = 0m, decimal DiscountAmount = 0m, decimal BuyQuantity = 0m, decimal PayQuantity = 0m, DateTimeOffset? StartsAtUtc = null, DateTimeOffset? EndsAtUtc = null);
public sealed record PromotionResult(Guid Id, Guid ProductId, string Name, decimal Percent, decimal DiscountAmount, decimal BuyQuantity, decimal PayQuantity, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc, bool IsActive);
public sealed record PromotionPriceQuote(Guid ProductId, decimal BaseUnitPrice, decimal UnitPrice, decimal Quantity, decimal Total, decimal DiscountTotal, bool PromotionApplied);
public sealed record PromotionPriceCalculation(decimal UnitPrice, decimal Total);

public sealed class PromotionService(PosDbContext database)
{
    public async Task<PromotionResult?> CreateAsync(string token, PromotionCommand command, CancellationToken cancellationToken)
    {
        var user = await AuthorizedAsync(token, "ManageProducts", cancellationToken); if (user is null) return null;
        if (command.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 120 || command.Percent < 0m || command.Percent >= 100m || command.DiscountAmount < 0m || command.BuyQuantity < 0m || command.PayQuantity < 0m || (command.BuyQuantity > 0m && (command.PayQuantity <= 0m || command.PayQuantity >= command.BuyQuantity)) || (command.Percent == 0m && command.DiscountAmount == 0m && command.BuyQuantity == 0m) || (command.StartsAtUtc is not null && command.EndsAtUtc is not null && command.EndsAtUtc <= command.StartsAtUtc)) throw new ArgumentException("La promocion requiere un descuento valido y una vigencia coherente.");
        if (!await database.Products.AnyAsync(item => item.Id == command.ProductId && item.IsActive, cancellationToken)) throw new KeyNotFoundException("Producto no encontrado.");
        if (await database.Promotions.AnyAsync(item => item.Name == command.Name.Trim(), cancellationToken)) throw new InvalidOperationException("El nombre de la promocion ya existe.");
        var promotion = new PromotionRecord { Id = Guid.NewGuid(), ProductId = command.ProductId, Name = command.Name.Trim(), Percent = decimal.Round(command.Percent, 2), DiscountAmount = decimal.Round(command.DiscountAmount, 2), BuyQuantity = decimal.Round(command.BuyQuantity, 3), PayQuantity = decimal.Round(command.PayQuantity, 3), StartsAtUtc = command.StartsAtUtc?.ToUniversalTime(), EndsAtUtc = command.EndsAtUtc?.ToUniversalTime(), IsActive = true }; database.Promotions.Add(promotion); await database.SaveChangesAsync(cancellationToken); return ToResult(promotion);
    }

    public async Task<PromotionPriceCalculation> CalculateAsync(Guid productId, decimal price, DateTimeOffset now, CancellationToken cancellationToken, decimal quantity = 1m)
    {
        var promotions = await database.Promotions.AsNoTracking().Where(item => item.ProductId == productId && item.IsActive && (item.StartsAtUtc == null || item.StartsAtUtc <= now) && (item.EndsAtUtc == null || item.EndsAtUtc > now)).ToListAsync(cancellationToken);
        var result = price * quantity;
        foreach (var promotion in promotions)
        {
            var candidate = promotion.BuyQuantity > 0m && quantity >= promotion.BuyQuantity
                ? price * ((Math.Floor(quantity / promotion.BuyQuantity) * promotion.PayQuantity) + quantity % promotion.BuyQuantity)
                : promotion.Percent > 0m
                    ? price * quantity * (1m - promotion.Percent / 100m)
                    : Math.Max(0m, price - promotion.DiscountAmount) * quantity;
            result = Math.Min(result, candidate);
        }
        var total = decimal.Round(Math.Max(0m, result), 2, MidpointRounding.AwayFromZero);
        return new PromotionPriceCalculation(decimal.Round(total / quantity, 2, MidpointRounding.AwayFromZero), total);
    }

    public async Task<decimal> DiscountedPriceAsync(Guid productId, decimal price, DateTimeOffset now, CancellationToken cancellationToken, decimal quantity = 1m) =>
        (await CalculateAsync(productId, price, now, cancellationToken, quantity)).UnitPrice;

    public async Task<PromotionPriceQuote?> QuoteAsync(string token, Guid productId, decimal price, decimal quantity, CancellationToken cancellationToken)
    {
        // F1 necesita cotizar durante una venta; no debe exigir permisos administrativos de catálogo.
        if (await AuthorizedAsync(token, "Sell", cancellationToken) is null) return null;
        if (productId == Guid.Empty || price < 0m || quantity <= 0m) throw new ArgumentException("Los datos de precio y cantidad no son validos.");
        var calculation = await CalculateAsync(productId, price, DateTimeOffset.UtcNow, cancellationToken, quantity);
        var discountTotal = decimal.Round(Math.Max(0m, (price * quantity) - calculation.Total), 2, MidpointRounding.AwayFromZero);
        return new PromotionPriceQuote(productId, price, calculation.UnitPrice, quantity, calculation.Total, discountTotal, discountTotal > 0m);
    }

    public async Task<IReadOnlyList<PromotionResult>?> ListAsync(string token, Guid? productId, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewProducts", cancellationToken) is null) return null;
        var query = database.Promotions.AsNoTracking().Where(item => item.IsActive); if (productId is not null) query = query.Where(item => item.ProductId == productId);
        return await query.OrderByDescending(item => item.StartsAtUtc).Select(item => ToResult(item)).ToListAsync(cancellationToken);
    }

    public async Task<bool?> DeactivateAsync(string token, Guid id, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ManageProducts", cancellationToken) is null) return null;
        var promotion = await database.Promotions.SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Promocion no encontrada."); promotion.IsActive = false; await database.SaveChangesAsync(cancellationToken); return true;
    }

    private async Task<Guid?> AuthorizedAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user.Id : null;
    }
    private static PromotionResult ToResult(PromotionRecord item) => new(item.Id, item.ProductId, item.Name, item.Percent, item.DiscountAmount, item.BuyQuantity, item.PayQuantity, item.StartsAtUtc, item.EndsAtUtc, item.IsActive);
}
