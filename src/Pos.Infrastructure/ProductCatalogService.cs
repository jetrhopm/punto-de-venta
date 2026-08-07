using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record ProductCommand(string Code, string Description, decimal Price, decimal Cost = 0m, decimal WholesalePrice = 0m, decimal WholesaleMinimumQuantity = 0m, bool IsKit = false);
public sealed record ProductResult(Guid Id, string Code, string Description, decimal Price, decimal Cost, decimal WholesalePrice, decimal WholesaleMinimumQuantity, bool IsActive);

public sealed class ProductCatalogService(PosDbContext database)
{
    public async Task<ProductResult?> CreateAsync(string accessToken, ProductCommand command, CancellationToken cancellationToken)
    {
        Validate(command);
        var userId = await GetAuthorizedUserAsync(accessToken, "ManageProducts", cancellationToken);
        if (userId is null) return null;
        var normalizedCode = NormalizeCode(command.Code);
        if (await database.Products.AnyAsync(product => product.NormalizedCode == normalizedCode, cancellationToken)) throw new InvalidOperationException("El codigo del producto ya existe.");
        var product = new ProductRecord { Id = Guid.NewGuid(), Code = command.Code.Trim(), NormalizedCode = normalizedCode, Description = command.Description.Trim(), Price = decimal.Round(command.Price, 2), Cost = decimal.Round(command.Cost, 2), WholesalePrice = decimal.Round(command.WholesalePrice, 2), WholesaleMinimumQuantity = decimal.Round(command.WholesaleMinimumQuantity, 3), IsKit = command.IsKit, IsActive = true };
        database.Products.Add(product);
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(product);
    }

    public async Task<ProductResult?> UpdateAsync(string accessToken, Guid id, ProductCommand command, CancellationToken cancellationToken)
    {
        Validate(command);
        var userId = await GetAuthorizedUserAsync(accessToken, "ManageProducts", cancellationToken);
        if (userId is null) return null;
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado.");
        var normalizedCode = NormalizeCode(command.Code);
        if (await database.Products.AnyAsync(item => item.Id != id && item.NormalizedCode == normalizedCode, cancellationToken)) throw new InvalidOperationException("El codigo del producto ya existe.");
        product.Code = command.Code.Trim(); product.NormalizedCode = normalizedCode; product.Description = command.Description.Trim(); product.Price = decimal.Round(command.Price, 2); product.Cost = decimal.Round(command.Cost, 2); product.WholesalePrice = decimal.Round(command.WholesalePrice, 2); product.WholesaleMinimumQuantity = decimal.Round(command.WholesaleMinimumQuantity, 3); product.IsKit = command.IsKit;
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(product);
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static ProductResult ToResult(ProductRecord product) => new(product.Id, product.Code, product.Description, product.Price, product.Cost, product.WholesalePrice, product.WholesaleMinimumQuantity, product.IsActive);

    private async Task<Guid?> GetAuthorizedUserAsync(string accessToken, string permission, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        if (user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken)) return user.Id;
        return null;
    }

    private static void Validate(ProductCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Code) || command.Code.Trim().Length > 80) throw new ArgumentException("El codigo es obligatorio y debe tener maximo 80 caracteres.");
        if (string.IsNullOrWhiteSpace(command.Description) || command.Description.Trim().Length > 200) throw new ArgumentException("La descripcion es obligatoria y debe tener maximo 200 caracteres.");
        if (command.Price < 0m || command.Price > 9999999999999999.99m || command.Cost < 0m || command.Cost > 9999999999999999.99m || command.WholesalePrice < 0m || command.WholesaleMinimumQuantity < 0m || (command.WholesalePrice > 0m && command.WholesaleMinimumQuantity <= 0m)) throw new ArgumentException("Los precios deben ser positivos y el mayoreo requiere cantidad minima.");
    }
}
