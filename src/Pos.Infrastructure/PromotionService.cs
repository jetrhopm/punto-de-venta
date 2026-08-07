using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record PromotionCommand(Guid ProductId, string Name, decimal Percent, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);
public sealed record PromotionResult(Guid Id, Guid ProductId, string Name, decimal Percent, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, bool IsActive);

public sealed class PromotionService(PosDbContext database)
{
    public async Task<PromotionResult?> CreateAsync(string token, PromotionCommand command, CancellationToken cancellationToken)
    {
        var user = await AuthorizedAsync(token, "ManageProducts", cancellationToken); if (user is null) return null;
        if (command.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 120 || command.Percent <= 0m || command.Percent >= 100m || command.EndsAtUtc <= command.StartsAtUtc) throw new ArgumentException("La promocion debe tener producto, nombre, porcentaje entre 0 y 100 y vigencia valida.");
        if (!await database.Products.AnyAsync(item => item.Id == command.ProductId && item.IsActive, cancellationToken)) throw new KeyNotFoundException("Producto no encontrado.");
        var promotion = new PromotionRecord { Id = Guid.NewGuid(), ProductId = command.ProductId, Name = command.Name.Trim(), Percent = decimal.Round(command.Percent, 2), StartsAtUtc = command.StartsAtUtc.ToUniversalTime(), EndsAtUtc = command.EndsAtUtc.ToUniversalTime(), IsActive = true }; database.Promotions.Add(promotion); await database.SaveChangesAsync(cancellationToken); return ToResult(promotion);
    }

    public async Task<decimal> DiscountedPriceAsync(Guid productId, decimal price, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var promotion = await database.Promotions.AsNoTracking().Where(item => item.ProductId == productId && item.IsActive && item.StartsAtUtc <= now && item.EndsAtUtc > now).OrderByDescending(item => item.Percent).FirstOrDefaultAsync(cancellationToken);
        return promotion is null ? price : decimal.Round(price * (1m - promotion.Percent / 100m), 2, MidpointRounding.AwayFromZero);
    }

    private async Task<Guid?> AuthorizedAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user.Id : null;
    }
    private static PromotionResult ToResult(PromotionRecord item) => new(item.Id, item.ProductId, item.Name, item.Percent, item.StartsAtUtc, item.EndsAtUtc, item.IsActive);
}
