using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record MobileCountSessionResult(Guid SessionId, DateTimeOffset StartedAtUtc);
public sealed record MobileCountCommand(Guid SessionId, Guid OperationId, Guid ProductId, decimal ExpectedStock, decimal CountedStock);
public sealed record MobileCountResult(Guid OperationId, Guid ProductId, string Code, string Description, decimal SystemStock, decimal CountedStock, decimal Difference, decimal CurrentStock, DateTimeOffset CreatedAtUtc);
public sealed record MobileCountHistoryRow(Guid SessionId, Guid ProductId, string Code, string Description, decimal SystemStock, decimal CountedStock, decimal Difference, string UserName, string DeviceName, DateTimeOffset CreatedAtUtc);
public sealed class MobileCountStockChangedException(decimal currentStock) : InvalidOperationException("La existencia cambió desde que consultaste el producto. Actualiza el artículo y vuelve a contar.")
{
    public decimal CurrentStock { get; } = currentStock;
}

/// <summary>Registra conteos desde teléfonos sin crear sesiones de caja y mantiene la bitácora del conteo.</summary>
public sealed class MobileInventoryCountService(PosDbContext database)
{
    public async Task<MobileCountSessionResult?> StartSessionAsync(string accessToken, string deviceToken, CancellationToken cancellationToken)
    {
        var actor = await AuthorizedActorAsync(accessToken, deviceToken, Permission.AdjustInventory, cancellationToken);
        if (actor is null) return null;

        var session = new MobileInventoryCountSessionRecord
        {
            Id = Guid.NewGuid(), StoreId = actor.Device.StoreId, UserId = actor.User.Id,
            MobileDeviceId = actor.Device.Id, StartedAtUtc = DateTimeOffset.UtcNow
        };
        database.MobileInventoryCountSessions.Add(session);
        await database.SaveChangesAsync(cancellationToken);
        return new MobileCountSessionResult(session.Id, session.StartedAtUtc);
    }

    public async Task<bool?> CloseSessionAsync(string accessToken, string deviceToken, Guid sessionId, CancellationToken cancellationToken)
    {
        var actor = await AuthorizedActorAsync(accessToken, deviceToken, Permission.AdjustInventory, cancellationToken);
        if (actor is null) return null;
        var session = await database.MobileInventoryCountSessions.SingleOrDefaultAsync(item => item.Id == sessionId && item.StoreId == actor.Device.StoreId && item.UserId == actor.User.Id && item.MobileDeviceId == actor.Device.Id, cancellationToken);
        if (session is null) return false;
        session.ClosedAtUtc ??= DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MobileCountResult?> RegisterCountAsync(string accessToken, string deviceToken, MobileCountCommand command, CancellationToken cancellationToken)
    {
        if (command.SessionId == Guid.Empty || command.OperationId == Guid.Empty || command.ProductId == Guid.Empty || command.ExpectedStock < 0m || command.CountedStock < 0m)
            throw new ArgumentException("Indica el producto y una cantidad contada igual o mayor que cero.");

        var expectedStock = decimal.Round(command.ExpectedStock, 3, MidpointRounding.AwayFromZero);
        var countedStock = decimal.Round(command.CountedStock, 3, MidpointRounding.AwayFromZero);
        if (expectedStock != command.ExpectedStock || countedStock != command.CountedStock)
            throw new ArgumentException("La existencia sólo puede capturarse hasta con tres decimales.");

        var actor = await AuthorizedActorAsync(accessToken, deviceToken, Permission.AdjustInventory, cancellationToken);
        if (actor is null) return null;

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var previous = await database.MobileInventoryCounts.AsNoTracking()
            .Include(item => item.Product)
            .SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (previous is not null)
        {
            var previousSession = await database.MobileInventoryCountSessions.AsNoTracking().SingleAsync(item => item.Id == previous.SessionId, cancellationToken);
            if (previous.SessionId != command.SessionId || previousSession.UserId != actor.User.Id || previousSession.MobileDeviceId != actor.Device.Id)
                throw new InvalidOperationException("La operación de conteo no pertenece a este usuario o teléfono.");
            await transaction.CommitAsync(cancellationToken);
            return ToResult(previous, previous.Product.Stock);
        }

        var session = await database.MobileInventoryCountSessions.SingleOrDefaultAsync(item => item.Id == command.SessionId && item.StoreId == actor.Device.StoreId && item.UserId == actor.User.Id && item.MobileDeviceId == actor.Device.Id, cancellationToken)
            ?? throw new InvalidOperationException("La sesión de conteo ya no está disponible. Inicia un nuevo conteo.");
        if (session.ClosedAtUtc is not null) throw new InvalidOperationException("Este conteo ya fue cerrado. Inicia uno nuevo para continuar.");

        await InventoryConcurrency.LockProductsAsync(database, [command.ProductId], cancellationToken);
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == command.ProductId && item.IsActive && !item.IsTemporary, cancellationToken)
            ?? throw new KeyNotFoundException("El artículo no existe o está inactivo.");
        if (product.Stock != expectedStock) throw new MobileCountStockChangedException(product.Stock);

        var before = product.Stock;
        var difference = decimal.Round(countedStock - before, 3, MidpointRounding.AwayFromZero);
        var now = DateTimeOffset.UtcNow;
        product.Stock = countedStock;
        if (difference != 0m)
        {
            database.InventoryMovements.Add(new InventoryMovementRecord
            {
                Id = Guid.NewGuid(), ProductId = product.Id, UserId = actor.User.Id, OperationId = command.OperationId,
                Quantity = difference, StockBefore = before, StockAfter = countedStock, Reason = "Conteo móvil", CreatedAtUtc = now
            });
        }

        var count = new MobileInventoryCountRecord
        {
            Id = Guid.NewGuid(), SessionId = session.Id, ProductId = product.Id, UserId = actor.User.Id,
            OperationId = command.OperationId, SystemStock = before, CountedStock = countedStock,
            Difference = difference, CreatedAtUtc = now
        };
        database.MobileInventoryCounts.Add(count);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResult(count, countedStock, product.Code, product.Description);
    }

    public async Task<IReadOnlyList<MobileCountHistoryRow>?> HistoryAsync(string accessToken, string deviceToken, int limit, bool differencesOnly, CancellationToken cancellationToken)
    {
        var actor = await AuthorizedActorAsync(accessToken, deviceToken, Permission.ViewInventory, cancellationToken, allowInventoryAdjustment: true);
        if (actor is null) return null;

        var rows = from count in database.MobileInventoryCounts.AsNoTracking()
                   join product in database.Products.AsNoTracking() on count.ProductId equals product.Id
                   join user in database.Users.AsNoTracking() on count.UserId equals user.Id
                   join session in database.MobileInventoryCountSessions.AsNoTracking() on count.SessionId equals session.Id
                   join device in database.MobileDevices.AsNoTracking() on session.MobileDeviceId equals device.Id
                   where session.StoreId == actor.Device.StoreId
                   select new { count, product, user, device };
        if (differencesOnly) rows = rows.Where(item => item.count.Difference != 0m);
        return await rows.OrderByDescending(item => item.count.CreatedAtUtc).Take(Math.Clamp(limit, 1, 250))
            .Select(item => new MobileCountHistoryRow(item.count.SessionId, item.product.Id, item.product.Code, item.product.Description, item.count.SystemStock, item.count.CountedStock, item.count.Difference, item.user.DisplayName, item.device.Name, item.count.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<MobileActor?> AuthorizedActorAsync(string accessToken, string deviceToken, Permission permission, CancellationToken cancellationToken, bool allowInventoryAdjustment = false)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(deviceToken)) return null;
        var tokenHash = Hash(accessToken);
        var deviceHash = Hash(deviceToken);
        var actor = await (from session in database.MobileSessions.AsNoTracking()
                           join device in database.MobileDevices.AsNoTracking() on session.MobileDeviceId equals device.Id
                           join user in database.Users.AsNoTracking() on session.UserId equals user.Id
                           where session.TokenHash == tokenHash && session.RevokedAtUtc == null && session.ExpiresAtUtc > DateTimeOffset.UtcNow
                              && device.DeviceTokenHash == deviceHash && device.IsActive && user.IsActive
                           select new MobileActor(user, device)).SingleOrDefaultAsync(cancellationToken);
        if (actor is null) return null;
        var hasPermission = actor.User.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == actor.User.Id && item.Code == permission.ToString() && item.ExpiresAtUtc == null, cancellationToken);
        if (!hasPermission && allowInventoryAdjustment)
            hasPermission = await database.Permissions.AnyAsync(item => item.UserId == actor.User.Id && item.Code == Permission.AdjustInventory.ToString() && item.ExpiresAtUtc == null, cancellationToken);
        if (!hasPermission) throw new UnauthorizedAccessException(permission == Permission.AdjustInventory
            ? "Tu usuario no tiene permiso para ajustar inventario desde JetVenta Móvil."
            : "Tu usuario no tiene permiso para consultar la bitácora de inventario.");
        return actor;
    }

    private static MobileCountResult ToResult(MobileInventoryCountRecord count, decimal currentStock, string? code = null, string? description = null) =>
        new(count.OperationId, count.ProductId, code ?? count.Product.Code, description ?? count.Product.Description, count.SystemStock, count.CountedStock, count.Difference, currentStock, count.CreatedAtUtc);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record MobileActor(UserRecord User, MobileDeviceRecord Device);
}
