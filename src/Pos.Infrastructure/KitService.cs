using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record KitComponentCommand(Guid ComponentProductId, decimal Quantity);
public sealed record KitCommand(Guid KitProductId, IReadOnlyList<KitComponentCommand> Components);
public sealed record KitComponentResult(Guid ComponentProductId, string Code, string Description, string UnitOfMeasure, decimal Quantity, decimal UnitCost, decimal UnitPrice, decimal TotalCost, decimal TotalPrice);
public sealed record KitResult(Guid Id, string Code, string Description, string Department, Guid? DepartmentId, decimal Cost, decimal Price, decimal ProfitPercent, decimal WholesalePrice, decimal WholesaleProfitPercent, decimal WholesaleMinimumQuantity, decimal MinimumStock, decimal MaximumStock, string UnitOfMeasure, bool IsActive, int ComponentCount);

public sealed class KitService(PosDbContext database)
{
    public async Task<IReadOnlyList<KitComponentResult>?> SetAsync(string token, KitCommand command, CancellationToken cancellationToken)
    {
        if (command.KitProductId == Guid.Empty || command.Components.Count == 0) throw new ArgumentException("El kit requiere producto y componentes.");
        if (await AuthorizedAsync(token, "ManageProducts", cancellationToken) is null) return null;
        var kit = await database.Products.SingleOrDefaultAsync(item => item.Id == command.KitProductId && item.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Kit no encontrado o está dado de baja.");
        if (command.Components.Any(item => item.ComponentProductId == command.KitProductId || item.Quantity <= 0m) || command.Components.GroupBy(item => item.ComponentProductId).Any(group => group.Count() > 1)) throw new ArgumentException("Los componentes del kit deben ser positivos, únicos y no pueden contener al propio kit.");
        var components = await database.Products.Where(item => command.Components.Select(component => component.ComponentProductId).Contains(item.Id) && item.IsActive).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (components.Count != command.Components.Count) throw new KeyNotFoundException("Uno o más componentes no existen.");
        if (components.Values.Any(item => item.IsKit)) throw new ArgumentException("Los componentes de un kit deben ser productos normales; los kits anidados no están disponibles.");
        kit.IsKit = true;
        kit.Cost = decimal.Round(command.Components.Sum(item => components[item.ComponentProductId].Cost * item.Quantity), 2, MidpointRounding.AwayFromZero);
        database.KitComponents.RemoveRange(database.KitComponents.Where(item => item.KitProductId == kit.Id));
        database.KitComponents.AddRange(command.Components.Select(item => new KitComponentRecord { Id = Guid.NewGuid(), KitProductId = kit.Id, ComponentProductId = item.ComponentProductId, Quantity = decimal.Round(item.Quantity, 3) }));
        await database.SaveChangesAsync(cancellationToken);
        return command.Components.Select(item => ToComponentResult(components[item.ComponentProductId], item.Quantity)).ToArray();
    }

    public async Task<IReadOnlyList<KitComponentResult>?> GetAsync(string token, Guid kitProductId, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewProducts", cancellationToken) is null) return null;
        var kit = await database.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == kitProductId && item.IsKit, cancellationToken)
            ?? throw new KeyNotFoundException("Kit no encontrado.");
        var components = await (from item in database.KitComponents.AsNoTracking()
                                join product in database.Products.AsNoTracking() on item.ComponentProductId equals product.Id
                                where item.KitProductId == kit.Id
                                orderby product.Description
                                select new { Product = product, item.Quantity }).ToListAsync(cancellationToken);
        return components.Select(item => ToComponentResult(item.Product, item.Quantity)).ToArray();
    }

    public async Task<IReadOnlyList<KitResult>?> ListAsync(string token, bool includeInactive, string? query, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, "ViewProducts", cancellationToken) is null) return null;
        var kits = database.Products.AsNoTracking().Include(item => item.Department).Where(item => item.IsKit && (includeInactive || item.IsActive));
        var search = query?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(search)) kits = kits.Where(item => item.NormalizedCode.Contains(search) || item.Description.ToUpper().Contains(search));
        var rows = await kits.OrderBy(item => item.Description).Select(item => new { item.Id, item.Code, item.Description, Department = item.Department == null ? string.Empty : item.Department.Name, item.DepartmentId, item.Cost, item.Price, item.ProfitPercent, item.WholesalePrice, item.WholesaleProfitPercent, item.WholesaleMinimumQuantity, item.MinimumStock, item.MaximumStock, item.UnitOfMeasure, item.IsActive }).ToListAsync(cancellationToken);
        var counts = await database.KitComponents.AsNoTracking().GroupBy(item => item.KitProductId).Select(group => new { Id = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);
        return rows.Select(item => new KitResult(item.Id, item.Code, item.Description, item.Department, item.DepartmentId, item.Cost, item.Price, item.ProfitPercent, item.WholesalePrice, item.WholesaleProfitPercent, item.WholesaleMinimumQuantity, item.MinimumStock, item.MaximumStock, item.UnitOfMeasure, item.IsActive, counts.GetValueOrDefault(item.Id))).ToArray();
    }
    public async Task<IReadOnlyList<(Guid ProductId, decimal Quantity)>?> ExpandAsync(Guid productId, decimal quantity, CancellationToken cancellationToken)
    {
        var kit = await database.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == productId, cancellationToken); if (kit is null) return null;
        var components = await database.KitComponents.AsNoTracking().Where(item => item.KitProductId == productId).Select(item => new { item.ComponentProductId, item.Quantity }).ToListAsync(cancellationToken);
        if (!kit.IsKit) return [(productId, quantity)];
        if (components.Count == 0) throw new InvalidOperationException($"El kit '{kit.Description}' no tiene componentes configurados.");
        return components.Select(item => (item.ComponentProductId, item.Quantity * quantity)).ToArray();
    }
    private static KitComponentResult ToComponentResult(ProductRecord product, decimal quantity) => new(product.Id, product.Code, product.Description, product.UnitOfMeasure, quantity, product.Cost, product.Price, decimal.Round(product.Cost * quantity, 2, MidpointRounding.AwayFromZero), decimal.Round(product.Price * quantity, 2, MidpointRounding.AwayFromZero));
    private async Task<Guid?> AuthorizedAsync(string token, string permission, CancellationToken cancellationToken) { var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user.Id : null; }
}
