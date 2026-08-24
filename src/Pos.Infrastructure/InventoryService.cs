using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record InventoryAdjustmentCommand(Guid OperationId, Guid ProductId, decimal Quantity, string Reason);
public sealed record InventoryAdjustmentResult(Guid MovementId, Guid ProductId, decimal Quantity, decimal StockBefore, decimal StockAfter, string Reason);
public sealed record InventoryMovementResult(Guid Id, Guid ProductId, Guid? SaleId, Guid UserId, Guid OperationId, decimal Quantity, decimal StockBefore, decimal StockAfter, string Reason, DateTimeOffset CreatedAtUtc);
public sealed record InventoryLimitChangeCommand(Guid OperationId, Guid ProductId, decimal MinimumStock, decimal MaximumStock);
public sealed record InventoryLimitChangeResult(Guid ProductId, decimal PreviousMinimumStock, decimal PreviousMaximumStock, decimal MinimumStock, decimal MaximumStock);
public sealed record InventoryCatalogRow(Guid ProductId, string Code, string Description, string Department, string UnitOfMeasure, decimal Cost, decimal Price, decimal Stock, decimal MinimumStock, decimal MaximumStock, decimal CostValue, decimal SaleValue, decimal PotentialProfit, string Status);
public sealed record InventoryCatalogPageResult(IReadOnlyList<InventoryCatalogRow> Items, int Page, int PageSize, int TotalCount, int TotalPages, decimal TotalUnits, decimal TotalCostValue, decimal TotalSaleValue, decimal TotalPotentialProfit, int LowStockCount, int OutOfStockCount);
public sealed record InventoryMovementRow(Guid Id, string Code, string Description, decimal Quantity, decimal StockBefore, decimal StockAfter, string Reason, string UserName, DateTimeOffset CreatedAtUtc);
public sealed record InventoryMovementPageResult(IReadOnlyList<InventoryMovementRow> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed class InventoryService(PosDbContext database)
{
    public async Task<InventoryCatalogPageResult?> CatalogAsync(string token, string? query, string? status, string sort, bool descending, int page, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(token, "ViewInventory", cancellationToken) is null) return null;
        page = Math.Max(1, page);
        const int pageSize = 500;
        var products = database.Products.AsNoTracking().Where(item => item.IsActive && !item.IsTemporary);
        var text = query?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(text)) products = products.Where(item => item.NormalizedCode.Contains(text) || item.Description.ToUpper().Contains(text));
        status = status?.Trim().ToLowerInvariant();
        if (status == "agotado") products = products.Where(item => item.Stock <= 0m);
        else if (status == "bajo") products = products.Where(item => item.Stock > 0m && item.MinimumStock > 0m && item.Stock <= item.MinimumStock);
        else if (status == "sobre") products = products.Where(item => item.MaximumStock > 0m && item.Stock > item.MaximumStock);
        else if (status == "normal") products = products.Where(item => item.Stock > 0m && (item.MinimumStock <= 0m || item.Stock > item.MinimumStock) && (item.MaximumStock <= 0m || item.Stock <= item.MaximumStock));

        var allProducts = database.Products.AsNoTracking().Where(item => item.IsActive && !item.IsTemporary);
        var summary = await allProducts.GroupBy(_ => 1).Select(group => new
        {
            TotalUnits = group.Sum(item => item.Stock),
            TotalCostValue = group.Sum(item => item.Stock * item.Cost),
            TotalSaleValue = group.Sum(item => item.Stock * item.Price),
            TotalPotentialProfit = group.Sum(item => item.Stock * (item.Price - item.Cost)),
            LowStockCount = group.Count(item => item.Stock > 0m && item.MinimumStock > 0m && item.Stock <= item.MinimumStock),
            OutOfStockCount = group.Count(item => item.Stock <= 0m)
        }).SingleOrDefaultAsync(cancellationToken);

        products = sort.ToLowerInvariant() switch
        {
            "code" => descending ? products.OrderByDescending(item => item.Code) : products.OrderBy(item => item.Code),
            "department" => descending ? products.OrderByDescending(item => item.Category) : products.OrderBy(item => item.Category),
            "cost" => descending ? products.OrderByDescending(item => item.Cost) : products.OrderBy(item => item.Cost),
            "price" => descending ? products.OrderByDescending(item => item.Price) : products.OrderBy(item => item.Price),
            "stock" => descending ? products.OrderByDescending(item => item.Stock) : products.OrderBy(item => item.Stock),
            "minimum" => descending ? products.OrderByDescending(item => item.MinimumStock) : products.OrderBy(item => item.MinimumStock),
            "maximum" => descending ? products.OrderByDescending(item => item.MaximumStock) : products.OrderBy(item => item.MaximumStock),
            _ => descending ? products.OrderByDescending(item => item.Description) : products.OrderBy(item => item.Description)
        };
        var total = await products.CountAsync(cancellationToken);
        var rows = await products.Skip((page - 1) * pageSize).Take(pageSize).Select(item => new InventoryCatalogRow(
            item.Id, item.Code, item.Description, item.Category, item.UnitOfMeasure, item.Cost, item.Price, item.Stock, item.MinimumStock, item.MaximumStock,
            decimal.Round(item.Stock * item.Cost, 2), decimal.Round(item.Stock * item.Price, 2), decimal.Round(item.Stock * (item.Price - item.Cost), 2),
            item.Stock <= 0m ? "Agotado" : item.MinimumStock > 0m && item.Stock <= item.MinimumStock ? "Bajo mínimo" : item.MaximumStock > 0m && item.Stock > item.MaximumStock ? "Sobre máximo" : "Normal"))
            .ToListAsync(cancellationToken);
        return new InventoryCatalogPageResult(rows, page, pageSize, total, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)), summary?.TotalUnits ?? 0m, summary?.TotalCostValue ?? 0m, summary?.TotalSaleValue ?? 0m, summary?.TotalPotentialProfit ?? 0m, summary?.LowStockCount ?? 0, summary?.OutOfStockCount ?? 0);
    }

    public async Task<byte[]?> ExportCsvAsync(string token, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(token, "ViewInventory", cancellationToken) is null) return null;
        var rows = await database.Products.AsNoTracking().Where(item => item.IsActive && !item.IsTemporary).OrderBy(item => item.Description).Select(item => new { item.Code, item.Description, item.Category, item.UnitOfMeasure, item.Cost, item.Price, item.Stock, item.MinimumStock, item.MaximumStock }).ToListAsync(cancellationToken);
        var builder = new StringBuilder("Codigo,Descripcion,Departamento,TipoVenta,Costo,PrecioVenta,Existencia,InventarioMinimo,InventarioMaximo\r\n");
        foreach (var row in rows) builder.Append(string.Join(',', Csv(row.Code), Csv(row.Description), Csv(row.Category), Csv(row.UnitOfMeasure), row.Cost.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), row.Price.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), row.Stock.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), row.MinimumStock.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), row.MaximumStock.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    public async Task<InventoryMovementPageResult?> MovementsAsync(string token, string? query, int page, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(token, "ViewInventory", cancellationToken) is null) return null;
        page = Math.Max(1, page);
        const int pageSize = 500;
        var movements = from movement in database.InventoryMovements.AsNoTracking()
                        join product in database.Products.AsNoTracking().Where(item => !item.IsTemporary) on movement.ProductId equals product.Id
                        join user in database.Users.AsNoTracking() on movement.UserId equals user.Id
                        select new { movement, product, user };
        var text = query?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(text)) movements = movements.Where(item => item.product.NormalizedCode.Contains(text) || item.product.Description.ToUpper().Contains(text) || item.movement.Reason.ToUpper().Contains(text));
        var total = await movements.CountAsync(cancellationToken);
        var rows = await movements.OrderByDescending(item => item.movement.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).Select(item => new InventoryMovementRow(item.movement.Id, item.product.Code, item.product.Description, item.movement.Quantity, item.movement.StockBefore, item.movement.StockAfter, item.movement.Reason, item.user.DisplayName, item.movement.CreatedAtUtc)).ToListAsync(cancellationToken);
        return new InventoryMovementPageResult(rows, page, pageSize, total, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
    }

    public async Task<InventoryLimitChangeResult?> UpdateLimitsAsync(string token, InventoryLimitChangeCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.ProductId == Guid.Empty) throw new ArgumentException("La actualizacion requiere una operacion y un producto.");
        if (command.MinimumStock < 0m || command.MaximumStock < 0m || command.MaximumStock > 0m && command.MaximumStock < command.MinimumStock) throw new ArgumentException("Los limites de inventario no son validos.");
        var userId = await GetAuthorizedUserAsync(token, "AdjustInventory", cancellationToken);
        if (userId is null) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.InventoryLimitChanges.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new InventoryLimitChangeResult(existing.ProductId, existing.PreviousMinimumStock, existing.PreviousMaximumStock, existing.MinimumStock, existing.MaximumStock);
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == command.ProductId && item.IsActive && !item.IsTemporary, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado o inactivo.");
        var result = new InventoryLimitChangeResult(product.Id, product.MinimumStock, product.MaximumStock, decimal.Round(command.MinimumStock, 3), decimal.Round(command.MaximumStock, 3));
        product.MinimumStock = result.MinimumStock; product.MaximumStock = result.MaximumStock;
        database.InventoryLimitChanges.Add(new InventoryLimitChangeRecord { Id = Guid.NewGuid(), ProductId = product.Id, UserId = userId.Value, OperationId = command.OperationId, PreviousMinimumStock = result.PreviousMinimumStock, PreviousMaximumStock = result.PreviousMaximumStock, MinimumStock = result.MinimumStock, MaximumStock = result.MaximumStock, CreatedAtUtc = DateTimeOffset.UtcNow });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
    public async Task<InventoryAdjustmentResult?> AdjustAsync(string token, InventoryAdjustmentCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty || command.ProductId == Guid.Empty || command.Quantity == 0m || string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("El ajuste requiere operacion, producto, cantidad distinta de cero y motivo.");
        var userId = await GetAuthorizedUserAsync(token, "AdjustInventory", cancellationToken);
        if (userId is null) return null;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await database.InventoryMovements.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new InventoryAdjustmentResult(existing.Id, existing.ProductId, existing.Quantity, existing.StockBefore, existing.StockAfter, existing.Reason);
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == command.ProductId && item.IsActive && !item.IsTemporary, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado o inactivo.");
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

    private static string Csv(string value) => value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@') ? $"'\"{value.Replace("\"", "\"\"")}\"" : $"\"{value.Replace("\"", "\"\"")}\"";
}
