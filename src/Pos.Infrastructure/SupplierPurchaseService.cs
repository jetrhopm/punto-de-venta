using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SupplierCommand(string Name, string? Phone, string? Email);
public sealed record SupplierResult(Guid Id, string Name, string? Phone, string? Email);
public sealed record PurchaseLineCommand(Guid ProductId, decimal Quantity, decimal UnitCost);
public sealed record ReceivePurchaseCommand(Guid OperationId, Guid SupplierId, IReadOnlyList<PurchaseLineCommand> Lines);
public sealed record ReceivePurchaseResult(Guid PurchaseId, Guid OperationId, decimal Total, bool Existing);

public sealed class SupplierPurchaseService(PosDbContext database)
{
    public async Task<IReadOnlyList<SupplierResult>?> ListSuppliersAsync(string token, string? query, CancellationToken cancellationToken)
    {
        if (await UserAsync(token, cancellationToken) is null) return null;
        var search = (query ?? string.Empty).Trim().ToUpperInvariant();
        return await database.Suppliers.AsNoTracking().Where(item => search.Length == 0 || item.Name.ToUpper().Contains(search)).OrderBy(item => item.Name).Take(100).Select(item => new SupplierResult(item.Id, item.Name, item.Phone, item.Email)).ToListAsync(cancellationToken);
    }

    public async Task<SupplierResult?> CreateSupplierAsync(string token, SupplierCommand command, CancellationToken cancellationToken)
    {
        if (await UserAsync(token, cancellationToken) is null) return null;
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 160) throw new ArgumentException("El proveedor requiere nombre valido.");
        if (command.Phone?.Length > 40 || command.Email?.Length > 160) throw new ArgumentException("Los datos del proveedor exceden su longitud.");
        var supplier = new SupplierRecord { Id = Guid.NewGuid(), Name = command.Name.Trim(), Phone = Clean(command.Phone), Email = Clean(command.Email), CreatedAtUtc = DateTimeOffset.UtcNow };
        database.Suppliers.Add(supplier); await database.SaveChangesAsync(cancellationToken); return new SupplierResult(supplier.Id, supplier.Name, supplier.Phone, supplier.Email);
    }

    public async Task<ReceivePurchaseResult?> ReceiveAsync(string token, ReceivePurchaseCommand command, CancellationToken cancellationToken)
    {
        var user = await UserAsync(token, cancellationToken);
        if (user is null) return null;
        if (command.OperationId == Guid.Empty || command.SupplierId == Guid.Empty || command.Lines.Count == 0) throw new ArgumentException("La compra requiere operacion, proveedor y partidas.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.Purchases.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new ReceivePurchaseResult(existing.Id, existing.OperationId, existing.Total, true);
        if (!await database.Suppliers.AnyAsync(item => item.Id == command.SupplierId, cancellationToken)) throw new KeyNotFoundException("Proveedor no encontrado.");
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        var ids = command.Lines.Select(item => item.ProductId).Distinct().ToArray();
        var products = await database.Products.Where(item => ids.Contains(item.Id) && item.IsActive).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (products.Count != ids.Length) throw new KeyNotFoundException("Una o mas partidas no existen o estan inactivas.");
        var purchase = new PurchaseRecord { Id = Guid.NewGuid(), OperationId = command.OperationId, SupplierId = command.SupplierId, UserId = user.Id, CreatedAtUtc = DateTimeOffset.UtcNow };
        var total = 0m;
        foreach (var line in command.Lines)
        {
            if (line.Quantity <= 0m || line.UnitCost < 0m) throw new ArgumentException("La cantidad debe ser positiva y el costo no puede ser negativo.");
            var product = products[line.ProductId]; var before = product.Stock; var oldValue = before * product.Cost; var receivedValue = line.Quantity * line.UnitCost; var after = before + line.Quantity;
            product.Cost = after == 0m ? 0m : decimal.Round((oldValue + receivedValue) / after, 2, MidpointRounding.AwayFromZero);
            if (store.InventoryEnabled) product.Stock = decimal.Round(after, 3, MidpointRounding.AwayFromZero);
            if (store.AutoPriceWithProfit && product.Price <= 0m) product.Price = decimal.Round(product.Cost * (1m + (product.ProfitPercent > 0m ? product.ProfitPercent : store.DefaultProfitPercent) / 100m), 2, MidpointRounding.AwayFromZero);
            var lineTotal = decimal.Round(receivedValue, 2, MidpointRounding.AwayFromZero); total += lineTotal;
            database.PurchaseLines.Add(new PurchaseLineRecord { Id = Guid.NewGuid(), PurchaseId = purchase.Id, ProductId = product.Id, Quantity = line.Quantity, UnitCost = decimal.Round(line.UnitCost, 2), LineTotal = lineTotal });
            if (store.InventoryEnabled) database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = product.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = line.Quantity, StockBefore = before, StockAfter = product.Stock, Reason = "Purchase", CreatedAtUtc = purchase.CreatedAtUtc });
        }
        purchase.Total = decimal.Round(total, 2); database.Purchases.Add(purchase); await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new ReceivePurchaseResult(purchase.Id, purchase.OperationId, purchase.Total, false);
    }

    private async Task<UserRecord?> UserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ManageSuppliersAndPurchases", cancellationToken) ? user : null;
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
