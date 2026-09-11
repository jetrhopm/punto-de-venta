using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record SupplierCommand(string Name, string? Phone, string? Email);
public sealed record SupplierResult(Guid Id, string Name, string? Phone, string? Email);
public sealed record PurchaseLineCommand(Guid ProductId, decimal Quantity, decimal UnitCost, decimal? SalePrice = null);
public sealed record ReceivePurchaseCommand(Guid OperationId, Guid? SupplierId, IReadOnlyList<PurchaseLineCommand> Lines);
public sealed record ReceivePurchaseResult(Guid PurchaseId, Guid OperationId, decimal Total, bool Existing);
public sealed record PurchaseSuggestion(Guid ProductId, string Code, string Description, Guid? DepartmentId, string? Department, Guid? SupplierId, string? Supplier, decimal Stock, decimal MinimumStock, decimal MaximumStock, decimal SuggestedQuantity, decimal UnitCost, decimal EstimatedTotal, string UnitOfMeasure);
public sealed record PurchasePlanningProduct(Guid ProductId, string Code, string Description, Guid? DepartmentId, string? Department, Guid? SupplierId, string? Supplier, decimal Stock, decimal MinimumStock, decimal MaximumStock, decimal SuggestedQuantity, decimal UnitCost, decimal EstimatedTotal, decimal QuantitySold, string UnitOfMeasure);
public sealed record PurchaseOrderLineCommand(Guid ProductId, decimal Quantity, decimal UnitCost);
public sealed record PurchaseOrderCommand(Guid OperationId, Guid? SupplierId, string? Notes, IReadOnlyList<PurchaseOrderLineCommand> Lines);
public sealed record PurchaseOrderResult(Guid Id, Guid OperationId, Guid? SupplierId, string? Supplier, string Status, string? Notes, decimal Total, int LineCount, DateTimeOffset CreatedAtUtc, DateTimeOffset? ClosedAtUtc);
public sealed record PurchaseOrderPrintLine(string Code, string Description, decimal Quantity, decimal UnitCost, decimal Total);
public sealed record PurchaseOrderPrintResult(Guid Id, string? Supplier, string Status, string? Notes, decimal Total, DateTimeOffset CreatedAtUtc, IReadOnlyList<PurchaseOrderPrintLine> Lines);

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
        if (command.OperationId == Guid.Empty || command.Lines.Count == 0) throw new ArgumentException("La compra requiere operación y partidas.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.Purchases.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new ReceivePurchaseResult(existing.Id, existing.OperationId, existing.Total, true);
        var supplierId = command.SupplierId;
        if (supplierId is null || supplierId == Guid.Empty)
        {
            var directSupplier = await database.Suppliers.SingleOrDefaultAsync(item => item.Name == "Sin proveedor", cancellationToken);
            if (directSupplier is null)
            {
                directSupplier = new SupplierRecord { Id = Guid.NewGuid(), Name = "Sin proveedor", CreatedAtUtc = DateTimeOffset.UtcNow };
                database.Suppliers.Add(directSupplier);
            }
            supplierId = directSupplier.Id;
        }
        else if (!await database.Suppliers.AnyAsync(item => item.Id == supplierId, cancellationToken)) throw new KeyNotFoundException("Proveedor no encontrado.");
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        var ids = command.Lines.Select(item => item.ProductId).Distinct().ToArray();
        var products = await database.Products.Where(item => ids.Contains(item.Id) && item.IsActive).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (products.Count != ids.Length) throw new KeyNotFoundException("Una o mas partidas no existen o estan inactivas.");
        var purchase = new PurchaseRecord { Id = Guid.NewGuid(), OperationId = command.OperationId, SupplierId = supplierId.Value, UserId = user.Id, CreatedAtUtc = DateTimeOffset.UtcNow };
        var total = 0m;
        foreach (var line in command.Lines)
        {
            if (line.Quantity <= 0m || line.UnitCost < 0m || line.SalePrice is < 0m) throw new ArgumentException("La cantidad debe ser positiva y el costo y precio no pueden ser negativos.");
            var product = products[line.ProductId]; var before = product.Stock; var oldValue = before * product.Cost; var receivedValue = line.Quantity * line.UnitCost; var after = before + line.Quantity;
            product.Cost = after == 0m ? 0m : decimal.Round((oldValue + receivedValue) / after, 2, MidpointRounding.AwayFromZero);
            if (line.SalePrice is decimal salePrice)
            {
                product.Price = decimal.Round(salePrice, 2, MidpointRounding.AwayFromZero);
                if (product.Cost > 0m) product.ProfitPercent = decimal.Round(((product.Price - product.Cost) / product.Cost) * 100m, 2, MidpointRounding.AwayFromZero);
            }
            if (store.InventoryEnabled) product.Stock = decimal.Round(after, 3, MidpointRounding.AwayFromZero);
            if (store.AutoPriceWithProfit && product.Price <= 0m) product.Price = decimal.Round(product.Cost * (1m + (product.ProfitPercent > 0m ? product.ProfitPercent : store.DefaultProfitPercent) / 100m), 2, MidpointRounding.AwayFromZero);
            var lineTotal = decimal.Round(receivedValue, 2, MidpointRounding.AwayFromZero); total += lineTotal;
            database.PurchaseLines.Add(new PurchaseLineRecord { Id = Guid.NewGuid(), PurchaseId = purchase.Id, ProductId = product.Id, Quantity = line.Quantity, UnitCost = decimal.Round(line.UnitCost, 2), LineTotal = lineTotal });
            if (store.InventoryEnabled) database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = product.Id, UserId = user.Id, OperationId = command.OperationId, Quantity = line.Quantity, StockBefore = before, StockAfter = product.Stock, Reason = "Purchase", CreatedAtUtc = purchase.CreatedAtUtc });
        }
        purchase.Total = decimal.Round(total, 2); database.Purchases.Add(purchase); await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new ReceivePurchaseResult(purchase.Id, purchase.OperationId, purchase.Total, false);
    }

    public async Task<IReadOnlyList<PurchaseSuggestion>?> SuggestionsAsync(string token, Guid? supplierId, Guid? departmentId, CancellationToken cancellationToken)
    {
        if (await UserAsync(token, cancellationToken) is null) return null;
        if (supplierId is Guid supplied && supplied != Guid.Empty && !await database.Suppliers.AnyAsync(item => item.Id == supplied, cancellationToken)) throw new KeyNotFoundException("Proveedor no encontrado.");
        if (departmentId is Guid department && department != Guid.Empty && !await database.Departments.AnyAsync(item => item.Id == department, cancellationToken)) throw new KeyNotFoundException("Departamento no encontrado.");
        var products = await database.Products.AsNoTracking()
            .Where(item => item.IsActive && !item.IsTemporary && item.MinimumStock > 0m && item.Stock <= item.MinimumStock)
            .Where(item => !supplierId.HasValue || item.PrimarySupplierId == supplierId)
            .Where(item => !departmentId.HasValue || item.DepartmentId == departmentId)
            .OrderBy(item => item.Department == null ? string.Empty : item.Department.Name).ThenBy(item => item.Description)
            .Select(item => new { item.Id, item.Code, item.Description, item.DepartmentId, Department = item.Department == null ? null : item.Department.Name, item.PrimarySupplierId, Supplier = item.PrimarySupplierId == null ? null : database.Suppliers.Where(supplier => supplier.Id == item.PrimarySupplierId).Select(supplier => supplier.Name).FirstOrDefault(), item.Stock, item.MinimumStock, item.MaximumStock, item.Cost, item.UnitOfMeasure })
            .ToListAsync(cancellationToken);
        return products.Select(item =>
        {
            var replenishmentTarget = item.MaximumStock > item.Stock ? item.MaximumStock : item.MinimumStock;
            var suggestedQuantity = decimal.Round(Math.Max(replenishmentTarget - item.Stock, 1m), 3, MidpointRounding.AwayFromZero);
            var total = decimal.Round(suggestedQuantity * item.Cost, 2, MidpointRounding.AwayFromZero);
            return new PurchaseSuggestion(item.Id, item.Code, item.Description, item.DepartmentId, item.Department, item.PrimarySupplierId, item.Supplier, item.Stock, item.MinimumStock, item.MaximumStock, suggestedQuantity, item.Cost, total, item.UnitOfMeasure);
        }).ToList();
    }

    public async Task<IReadOnlyList<PurchasePlanningProduct>?> PlanningProductsAsync(string token, string? mode, Guid? supplierId, Guid? departmentId, int salesDays, CancellationToken cancellationToken)
    {
        if (await UserAsync(token, cancellationToken) is null) return null;
        if (supplierId is Guid supplied && supplied != Guid.Empty && !await database.Suppliers.AnyAsync(item => item.Id == supplied, cancellationToken)) throw new KeyNotFoundException("Proveedor no encontrado.");
        if (departmentId is Guid department && department != Guid.Empty && !await database.Departments.AnyAsync(item => item.Id == department, cancellationToken)) throw new KeyNotFoundException("Departamento no encontrado.");

        var normalizedMode = (mode ?? "All").Trim().ToUpperInvariant();
        var validModes = new[] { "ALL", "RECOMMENDED", "CRITICAL", "LOWSTOCK", "BESTSELLERS", "LOWSALES" };
        if (!validModes.Contains(normalizedMode)) throw new ArgumentException("El filtro de compras no es válido.");
        var days = salesDays is 7 or 30 or 90 or 365 ? salesDays : 30;
        var salesFromUtc = DateTimeOffset.UtcNow.AddDays(-days);

        var soldByProduct = await (from line in database.SaleLines.AsNoTracking()
                                   join sale in database.Sales.AsNoTracking() on line.SaleId equals sale.Id
                                   where sale.Status == "Completed" && sale.CreatedAtUtc >= salesFromUtc
                                   group line by line.ProductId into grouped
                                   select new { ProductId = grouped.Key, Quantity = grouped.Sum(item => item.Quantity) })
            .ToDictionaryAsync(item => item.ProductId, item => item.Quantity, cancellationToken);

        var products = await database.Products.AsNoTracking()
            .Where(item => item.IsActive && !item.IsTemporary)
            .Where(item => !supplierId.HasValue || item.PrimarySupplierId == supplierId)
            .Where(item => !departmentId.HasValue || item.DepartmentId == departmentId)
            .Select(item => new
            {
                item.Id,
                item.Code,
                item.Description,
                item.DepartmentId,
                Department = item.Department == null ? null : item.Department.Name,
                item.PrimarySupplierId,
                Supplier = item.PrimarySupplierId == null ? null : database.Suppliers.Where(supplier => supplier.Id == item.PrimarySupplierId).Select(supplier => supplier.Name).FirstOrDefault(),
                item.Stock,
                item.MinimumStock,
                item.MaximumStock,
                item.Cost,
                item.UnitOfMeasure
            })
            .ToListAsync(cancellationToken);

        var result = products.Select(item =>
        {
            var needsRestock = item.MinimumStock > 0m && item.Stock <= item.MinimumStock;
            var target = item.MaximumStock > item.Stock ? item.MaximumStock : item.MinimumStock;
            var suggestedQuantity = needsRestock
                ? decimal.Round(Math.Max(target - item.Stock, 1m), 3, MidpointRounding.AwayFromZero)
                : 1m;
            var quantitySold = soldByProduct.GetValueOrDefault(item.Id);
            return new PurchasePlanningProduct(item.Id, item.Code, item.Description, item.DepartmentId, item.Department, item.PrimarySupplierId, item.Supplier, item.Stock, item.MinimumStock, item.MaximumStock, suggestedQuantity, item.Cost, decimal.Round(suggestedQuantity * item.Cost, 2, MidpointRounding.AwayFromZero), quantitySold, item.UnitOfMeasure);
        });

        result = normalizedMode switch
        {
            "CRITICAL" => result.Where(item => item.Stock <= 0m).OrderBy(item => item.Stock).ThenBy(item => item.Description),
            "LOWSTOCK" => result.Where(item => item.Stock > 0m && item.MinimumStock > 0m && item.Stock <= item.MinimumStock).OrderBy(item => item.Stock - item.MinimumStock).ThenBy(item => item.Description),
            "RECOMMENDED" => result.Where(item => item.MinimumStock > 0m && item.Stock <= item.MinimumStock).OrderBy(item => item.Stock - item.MinimumStock).ThenByDescending(item => item.QuantitySold),
            "BESTSELLERS" => result.Where(item => item.QuantitySold > 0m).OrderByDescending(item => item.QuantitySold).ThenBy(item => item.Description),
            "LOWSALES" => result.OrderBy(item => item.QuantitySold).ThenBy(item => item.Description),
            _ => result.OrderBy(item => item.Description)
        };

        return result.ToList();
    }

    public async Task<IReadOnlyList<PurchaseOrderResult>?> ListOrdersAsync(string token, string? status, Guid? supplierId, CancellationToken cancellationToken)
    {
        if (await UserAsync(token, cancellationToken) is null) return null;
        var normalizedStatus = string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase) ? "Closed" : string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase) ? "Open" : null;
        var orders = await database.PurchaseOrders.AsNoTracking()
            .Where(item => normalizedStatus == null || item.Status == normalizedStatus)
            .Where(item => !supplierId.HasValue || item.SupplierId == supplierId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(200)
            .Select(item => new { item.Id, item.OperationId, item.SupplierId, Supplier = item.SupplierId == null ? null : database.Suppliers.Where(supplier => supplier.Id == item.SupplierId).Select(supplier => supplier.Name).FirstOrDefault(), item.Status, item.Notes, item.Total, item.CreatedAtUtc, item.ClosedAtUtc, LineCount = database.PurchaseOrderLines.Count(line => line.PurchaseOrderId == item.Id) })
            .ToListAsync(cancellationToken);
        return orders.Select(item => new PurchaseOrderResult(item.Id, item.OperationId, item.SupplierId, item.Supplier, item.Status, item.Notes, item.Total, item.LineCount, item.CreatedAtUtc, item.ClosedAtUtc)).ToList();
    }

    public async Task<PurchaseOrderResult?> CreateOrderAsync(string token, PurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var user = await UserAsync(token, cancellationToken);
        if (user is null) return null;
        if (command.OperationId == Guid.Empty || command.Lines.Count == 0) throw new ArgumentException("La orden requiere al menos una partida.");
        if (command.Notes?.Length > 500) throw new ArgumentException("Las notas no pueden exceder 500 caracteres.");
        if (command.Lines.Any(line => line.ProductId == Guid.Empty || line.Quantity <= 0m || line.UnitCost < 0m) || command.Lines.Select(line => line.ProductId).Distinct().Count() != command.Lines.Count) throw new ArgumentException("Revisa productos, cantidades y costos de la orden.");
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return await OrderResultAsync(existing, cancellationToken);
        if (command.SupplierId is Guid supplierId && supplierId != Guid.Empty && !await database.Suppliers.AnyAsync(item => item.Id == supplierId, cancellationToken)) throw new KeyNotFoundException("Proveedor no encontrado.");
        var productIds = command.Lines.Select(line => line.ProductId).ToArray();
        var products = await database.Products.Where(item => productIds.Contains(item.Id) && item.IsActive && !item.IsTemporary).ToDictionaryAsync(item => item.Id, cancellationToken);
        if (products.Count != productIds.Length) throw new KeyNotFoundException("Una o más partidas no existen o están inactivas.");
        var order = new PurchaseOrderRecord { Id = Guid.NewGuid(), OperationId = command.OperationId, SupplierId = command.SupplierId is Guid id && id != Guid.Empty ? id : null, UserId = user.Id, Status = "Open", Notes = Clean(command.Notes), CreatedAtUtc = DateTimeOffset.UtcNow };
        foreach (var line in command.Lines)
        {
            var lineTotal = decimal.Round(line.Quantity * line.UnitCost, 2, MidpointRounding.AwayFromZero);
            order.Total += lineTotal;
            database.PurchaseOrderLines.Add(new PurchaseOrderLineRecord { Id = Guid.NewGuid(), PurchaseOrderId = order.Id, ProductId = line.ProductId, Quantity = decimal.Round(line.Quantity, 3), UnitCost = decimal.Round(line.UnitCost, 2), LineTotal = lineTotal });
        }
        order.Total = decimal.Round(order.Total, 2); database.PurchaseOrders.Add(order); await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return await OrderResultAsync(order, cancellationToken);
    }

    public async Task<PurchaseOrderResult?> CloseOrderAsync(string token, Guid orderId, CancellationToken cancellationToken)
    {
        if (await UserAsync(token, cancellationToken) is null) return null;
        var order = await database.PurchaseOrders.SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken) ?? throw new KeyNotFoundException("Orden no encontrada.");
        if (order.Status == "Open") { order.Status = "Closed"; order.ClosedAtUtc = DateTimeOffset.UtcNow; await database.SaveChangesAsync(cancellationToken); }
        return await OrderResultAsync(order, cancellationToken);
    }

    public async Task<PurchaseOrderPrintResult?> GetOrderForPrintAsync(string token, Guid orderId, CancellationToken cancellationToken)
    {
        if (await UserAsync(token, cancellationToken) is null) return null;
        var order = await database.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new KeyNotFoundException("Orden no encontrada.");
        var lines = await (from line in database.PurchaseOrderLines.AsNoTracking()
                           join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                           where line.PurchaseOrderId == orderId
                           orderby product.Description
                           select new PurchaseOrderPrintLine(product.Code, product.Description, line.Quantity, line.UnitCost, line.LineTotal))
            .ToListAsync(cancellationToken);
        var supplier = order.SupplierId is Guid supplierId
            ? await database.Suppliers.AsNoTracking().Where(item => item.Id == supplierId).Select(item => item.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new PurchaseOrderPrintResult(order.Id, supplier, order.Status, order.Notes, order.Total, order.CreatedAtUtc, lines);
    }

    private async Task<PurchaseOrderResult> OrderResultAsync(PurchaseOrderRecord order, CancellationToken cancellationToken)
    {
        var supplier = order.SupplierId is Guid supplierId ? await database.Suppliers.AsNoTracking().Where(item => item.Id == supplierId).Select(item => item.Name).SingleOrDefaultAsync(cancellationToken) : null;
        var lineCount = await database.PurchaseOrderLines.CountAsync(item => item.PurchaseOrderId == order.Id, cancellationToken);
        return new PurchaseOrderResult(order.Id, order.OperationId, order.SupplierId, supplier, order.Status, order.Notes, order.Total, lineCount, order.CreatedAtUtc, order.ClosedAtUtc);
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
