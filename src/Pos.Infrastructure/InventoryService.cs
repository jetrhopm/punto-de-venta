using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record InventoryAdjustmentCommand(Guid OperationId, Guid ProductId, decimal Quantity, string Reason);
public sealed record InventoryAdjustmentResult(Guid MovementId, Guid ProductId, decimal Quantity, decimal StockBefore, decimal StockAfter, string Reason);
public sealed record InventoryMovementResult(Guid Id, Guid ProductId, Guid? SaleId, Guid UserId, Guid OperationId, decimal Quantity, decimal StockBefore, decimal StockAfter, string Reason, DateTimeOffset CreatedAtUtc);

public sealed class InventoryService(PosDbContext database)
{
    public async Task<InventoryAdjustmentResult?> AdjustAsync(string token, InventoryAdjustmentCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.ProductId == Guid.Empty || command.Quantity == 0m || string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("El ajuste requiere operacion, producto, cantidad distinta de cero y motivo.");
        var userId = await GetAuthorizedUserAsync(token, "AdjustInventory", cancellationToken);
        if (userId is null) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.InventoryMovements.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new InventoryAdjustmentResult(existing.Id, existing.ProductId, existing.Quantity, existing.StockBefore, existing.StockAfter, existing.Reason);
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == command.ProductId && item.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado o inactivo.");
        var before = product.Stock;
        var after = decimal.Round(before + command.Quantity, 3, MidpointRounding.AwayFromZero);
        if (after < 0m) throw new InvalidOperationException("El ajuste no puede dejar existencia negativa.");
        product.Stock = after;
        var movement = new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = product.Id, UserId = userId.Value, OperationId = command.OperationId, Quantity = decimal.Round(command.Quantity, 3, MidpointRounding.AwayFromZero), StockBefore = before, StockAfter = after, Reason = command.Reason.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow };
        database.InventoryMovements.Add(movement);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InventoryAdjustmentResult(movement.Id, movement.ProductId, movement.Quantity, before, after, movement.Reason);
    }

    public async Task<IReadOnlyList<InventoryMovementResult>?> KardexAsync(string token, Guid productId, CancellationToken cancellationToken)
    {
        var userId = await GetAuthorizedUserAsync(token, "ViewInventory", cancellationToken);
        if (userId is null) return null;
        return await database.InventoryMovements.AsNoTracking().Where(item => item.ProductId == productId).OrderByDescending(item => item.CreatedAtUtc).Select(item => new InventoryMovementResult(item.Id, item.ProductId, item.SaleId, item.UserId, item.OperationId, item.Quantity, item.StockBefore, item.StockAfter, item.Reason, item.CreatedAtUtc)).Take(200).ToListAsync(cancellationToken);
    }

    private async Task<Guid?> GetAuthorizedUserAsync(string token, string permission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken) ? user.Id : null;
    }
}
