using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record KitComponentCommand(Guid ComponentProductId, decimal Quantity);
public sealed record KitCommand(Guid KitProductId, IReadOnlyList<KitComponentCommand> Components);
public sealed record KitComponentResult(Guid ComponentProductId, string Code, string Description, decimal Quantity);

public sealed class KitService(PosDbContext database)
{
    public async Task<IReadOnlyList<KitComponentResult>?> SetAsync(string token, KitCommand command, CancellationToken cancellationToken)
    {
        if (command.KitProductId == Guid.Empty || command.Components.Count == 0) throw new ArgumentException("El kit requiere producto y componentes.");
        if (await AuthorizedAsync(token, "ManageProducts", cancellationToken) is null) return null;
        var kit = await database.Products.SingleOrDefaultAsync(item => item.Id == command.KitProductId && item.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Kit no encontrado.");
        if (command.Components.Any(item => item.ComponentProductId == command.KitProductId || item.Quantity <= 0m) || command.Components.GroupBy(item => item.ComponentProductId).Any(group => group.Count() > 1)) throw new ArgumentException("Los componentes del kit deben ser positivos, únicos y no pueden contener al propio kit.");
        var components = await database.Products.Where(item => command.Components.Select(component => component.ComponentProductId).Contains(item.Id) && item.IsActive).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (components.Count != command.Components.Count) throw new KeyNotFoundException("Uno o más componentes no existen.");
        kit.IsKit = true; database.KitComponents.RemoveRange(database.KitComponents.Where(item => item.KitProductId == kit.Id));
        database.KitComponents.AddRange(command.Components.Select(item => new KitComponentRecord { Id = Guid.NewGuid(), KitProductId = kit.Id, ComponentProductId = item.ComponentProductId, Quantity = decimal.Round(item.Quantity, 3) })); await database.SaveChangesAsync(cancellationToken);
        return command.Components.Select(item => new KitComponentResult(item.ComponentProductId, components[item.ComponentProductId].Code, components[item.ComponentProductId].Description, item.Quantity)).ToArray();
    }
    public async Task<IReadOnlyList<(Guid ProductId, decimal Quantity)>?> ExpandAsync(Guid productId, decimal quantity, CancellationToken cancellationToken)
    {
        var kit = await database.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == productId, cancellationToken); if (kit is null) return null;
        var components = await database.KitComponents.AsNoTracking().Where(item => item.KitProductId == productId).Select(item => new { item.ComponentProductId, item.Quantity }).ToListAsync(cancellationToken);
        return kit.IsKit && components.Count > 0 ? components.Select(item => (item.ComponentProductId, item.Quantity * quantity)).ToArray() : [(productId, quantity)];
    }
    private async Task<Guid?> AuthorizedAsync(string token, string permission, CancellationToken cancellationToken) { var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))); var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken); if (session is null) return null; var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken); return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user.Id : null; }
}
