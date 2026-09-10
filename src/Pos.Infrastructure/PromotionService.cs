using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record PromotionCommand(Guid ProductId, string Name, decimal Percent = 0m, decimal DiscountAmount = 0m, decimal BuyQuantity = 0m, decimal PayQuantity = 0m, DateTimeOffset? StartsAtUtc = null, DateTimeOffset? EndsAtUtc = null);
public sealed record PromotionStatusCommand(bool IsActive);
public sealed record PromotionResult(Guid Id, Guid ProductId, string Name, decimal Percent, decimal DiscountAmount, decimal BuyQuantity, decimal PayQuantity, DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc, bool IsActive, string ProductCode = "", string ProductDescription = "", decimal ProductCost = 0m, decimal ProductPrice = 0m);
public sealed record PromotionPriceQuote(Guid ProductId, decimal BaseUnitPrice, decimal UnitPrice, decimal Quantity, decimal Total, decimal DiscountTotal, bool PromotionApplied, string PromotionName = "");
public sealed record PromotionPriceCalculation(decimal UnitPrice, decimal Total, string PromotionName = "");

public sealed class PromotionService(PosDbContext database)
{
    public async Task<PromotionResult?> CreateAsync(string token, PromotionCommand command, CancellationToken cancellationToken)
    {
        var user = await AuthorizedAsync(token, "ManageProducts", cancellationToken); if (user is null) return null;
        Validate(command);
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == command.ProductId && item.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado.");
        if (await database.Promotions.AnyAsync(item => item.Name == command.Name.Trim(), cancellationToken)) throw new InvalidOperationException("El nombre de la promocion ya existe.");
        var promotion = CreateRecord(command);
        database.Promotions.Add(promotion);
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(promotion, product);
    }

    public async Task<PromotionResult?> UpdateAsync(string token, Guid id, PromotionCommand command, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ManageProducts", cancellationToken) is null) return null;
        Validate(command);
        var promotion = await database.Promotions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Promocion no encontrada.");
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == command.ProductId && item.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado.");
        if (await database.Promotions.AnyAsync(item => item.Id != id && item.Name == command.Name.Trim(), cancellationToken)) throw new InvalidOperationException("El nombre de la promocion ya existe.");
        promotion.ProductId = command.ProductId;
        promotion.Name = command.Name.Trim();
        promotion.Percent = decimal.Round(command.Percent, 2);
        promotion.DiscountAmount = decimal.Round(command.DiscountAmount, 2);
        promotion.BuyQuantity = decimal.Round(command.BuyQuantity, 3);
        promotion.PayQuantity = decimal.Round(command.PayQuantity, 3);
        promotion.StartsAtUtc = command.StartsAtUtc?.ToUniversalTime();
        promotion.EndsAtUtc = command.EndsAtUtc?.ToUniversalTime();
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(promotion, product);
    }

    public async Task<PromotionPriceCalculation> CalculateAsync(Guid productId, decimal price, DateTimeOffset now, CancellationToken cancellationToken, decimal quantity = 1m)
    {
        var promotions = await database.Promotions.AsNoTracking().Where(item => item.ProductId == productId && item.IsActive && (item.StartsAtUtc == null || item.StartsAtUtc <= now) && (item.EndsAtUtc == null || item.EndsAtUtc > now)).ToListAsync(cancellationToken);
        var result = price * quantity;
        var promotionName = string.Empty;
        foreach (var promotion in promotions)
        {
            var candidate = promotion.BuyQuantity > 0m && quantity >= promotion.BuyQuantity
                ? price * ((Math.Floor(quantity / promotion.BuyQuantity) * promotion.PayQuantity) + quantity % promotion.BuyQuantity)
                : promotion.Percent > 0m
                    ? price * quantity * (1m - promotion.Percent / 100m)
                    : Math.Max(0m, price - promotion.DiscountAmount) * quantity;
            if (candidate < result)
            {
                result = candidate;
                promotionName = promotion.Name;
            }
        }
        var total = decimal.Round(Math.Max(0m, result), 2, MidpointRounding.AwayFromZero);
        return new PromotionPriceCalculation(decimal.Round(total / quantity, 2, MidpointRounding.AwayFromZero), total, promotionName);
    }

    public async Task<decimal> DiscountedPriceAsync(Guid productId, decimal price, DateTimeOffset now, CancellationToken cancellationToken, decimal quantity = 1m) =>
        (await CalculateAsync(productId, price, now, cancellationToken, quantity)).UnitPrice;

    public async Task<PromotionPriceQuote?> QuoteAsync(string token, Guid productId, decimal price, decimal quantity, CancellationToken cancellationToken)
    {
        // La cotización no modifica datos. Basta una sesión vigente para que la pantalla de venta
        // muestre el precio promocional antes de pedir la autorización de cobro.
        if (await AuthorizedSessionAsync(token, cancellationToken) is null) return null;
        if (productId == Guid.Empty || price < 0m || quantity <= 0m) throw new ArgumentException("Los datos de precio y cantidad no son validos.");
        var calculation = await CalculateAsync(productId, price, DateTimeOffset.UtcNow, cancellationToken, quantity);
        var discountTotal = decimal.Round(Math.Max(0m, (price * quantity) - calculation.Total), 2, MidpointRounding.AwayFromZero);
        return new PromotionPriceQuote(productId, price, calculation.UnitPrice, quantity, calculation.Total, discountTotal, discountTotal > 0m, calculation.PromotionName);
    }

    public async Task<IReadOnlyList<PromotionResult>?> ListAsync(string token, Guid? productId, bool includeInactive, string? query, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewProducts", cancellationToken) is null) return null;
        var promotionsQuery = database.Promotions.AsNoTracking().Where(item => includeInactive || item.IsActive);
        if (productId is not null) promotionsQuery = promotionsQuery.Where(item => item.ProductId == productId);
        var search = query?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(search))
            promotionsQuery = promotionsQuery.Where(item => item.Name.ToUpper().Contains(search) || database.Products.Any(product => product.Id == item.ProductId && (product.Code.ToUpper().Contains(search) || product.Description.ToUpper().Contains(search))));
        var promotions = await promotionsQuery.OrderByDescending(item => item.StartsAtUtc).ToListAsync(cancellationToken);
        var productIds = promotions.Select(item => item.ProductId).Distinct().ToArray();
        var products = await database.Products.AsNoTracking().Where(item => productIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        return promotions.Select(item => ToResult(item, products.GetValueOrDefault(item.ProductId))).ToArray();
    }

    public async Task<bool?> DeactivateAsync(string token, Guid id, CancellationToken cancellationToken)
    {
        return await SetStatusAsync(token, id, new PromotionStatusCommand(false), cancellationToken);
    }

    public async Task<bool?> SetStatusAsync(string token, Guid id, PromotionStatusCommand command, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ManageProducts", cancellationToken) is null) return null;
        var promotion = await database.Promotions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Promocion no encontrada."); promotion.IsActive = command.IsActive; await database.SaveChangesAsync(cancellationToken); return true;
    }

    private static PromotionRecord CreateRecord(PromotionCommand command) => new()
    {
        Id = Guid.NewGuid(), ProductId = command.ProductId, Name = command.Name.Trim(), Percent = decimal.Round(command.Percent, 2), DiscountAmount = decimal.Round(command.DiscountAmount, 2), BuyQuantity = decimal.Round(command.BuyQuantity, 3), PayQuantity = decimal.Round(command.PayQuantity, 3), StartsAtUtc = command.StartsAtUtc?.ToUniversalTime(), EndsAtUtc = command.EndsAtUtc?.ToUniversalTime(), IsActive = true
    };

    private static void Validate(PromotionCommand command)
    {
        if (command.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 120 || command.Percent < 0m || command.Percent >= 100m || command.DiscountAmount < 0m || command.BuyQuantity < 0m || command.PayQuantity < 0m || (command.BuyQuantity > 0m && (command.PayQuantity <= 0m || command.PayQuantity >= command.BuyQuantity)) || (command.Percent == 0m && command.DiscountAmount == 0m && command.BuyQuantity == 0m) || (command.StartsAtUtc is not null && command.EndsAtUtc is not null && command.EndsAtUtc <= command.StartsAtUtc)) throw new ArgumentException("La promocion requiere un descuento valido y una vigencia coherente.");
    }

    private async Task<Guid?> AuthorizedAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user.Id : null;
    }
    private async Task<Guid?> AuthorizedSessionAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        return await database.Sessions.AsNoTracking()
            .Where(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .Select(item => (Guid?)item.UserId)
            .SingleOrDefaultAsync(cancellationToken);
    }
    private static PromotionResult ToResult(PromotionRecord item, ProductRecord? product = null) => new(item.Id, item.ProductId, item.Name, item.Percent, item.DiscountAmount, item.BuyQuantity, item.PayQuantity, item.StartsAtUtc, item.EndsAtUtc, item.IsActive, product?.Code ?? string.Empty, product?.Description ?? string.Empty, product?.Cost ?? 0m, product?.Price ?? 0m);
}
