using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record ProductImportRow(int RowNumber, string Code, string Description, decimal Price, decimal Cost, decimal Stock, decimal WholesalePrice, decimal WholesaleMinimumQuantity, string Category = "", decimal MinimumStock = 0m, decimal MaximumStock = 0m, string UnitOfMeasure = "Pieza", string SupplierName = "");
public sealed record ProductImportCommand(Guid OperationId, string SourceFileName, string DuplicateRule, IReadOnlyList<ProductImportRow> Rows);
public sealed record ProductImportResult(Guid ImportId, int Created, int Updated, int Skipped, bool ExistingResult);

public sealed class ProductImportService(PosDbContext database)
{
    public async Task<ProductImportResult?> ImportAsync(string token, ProductImportCommand command, CancellationToken cancellationToken)
    {
        var userId = await AuthorizedAsync(token, cancellationToken);
        if (userId is null) return null;
        var existing = await database.ImportBatches.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null) return new(existing.Id, existing.CreatedCount, existing.UpdatedCount, existing.SkippedCount, true);
        Validate(command);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var normalizedCodes = command.Rows.Select(item => ProductCatalogService.NormalizeCode(item.Code)).ToArray();
        var products = await database.Products.Where(item => normalizedCodes.Contains(item.NormalizedCode)).ToDictionaryAsync(item => item.NormalizedCode, cancellationToken);
        var suppliers = (await database.Suppliers.ToListAsync(cancellationToken)).GroupBy(item => NormalizeName(item.Name), StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var created = 0;
        var updated = 0;
        var skipped = 0;
        foreach (var row in command.Rows)
        {
            var normalized = ProductCatalogService.NormalizeCode(row.Code);
            if (products.TryGetValue(normalized, out var product))
            {
                if (command.DuplicateRule.Equals("Skip", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                product.Description = row.Description.Trim();
                product.Price = decimal.Round(row.Price, 2);
                product.Cost = decimal.Round(row.Cost, 2);
                product.WholesalePrice = decimal.Round(row.WholesalePrice, 2);
                product.WholesaleMinimumQuantity = decimal.Round(row.WholesaleMinimumQuantity, 3);
                ApplyCatalogFields(product, row, ResolveSupplier(row.SupplierName, suppliers));
                AddStockMovement(product, row.Stock, userId.Value, command.OperationId, row.RowNumber);
                updated++;
            }
            else
            {
                product = new ProductRecord { Id = Guid.NewGuid(), Code = row.Code.Trim(), NormalizedCode = normalized, Description = row.Description.Trim(), Price = decimal.Round(row.Price, 2), Cost = decimal.Round(row.Cost, 2), WholesalePrice = decimal.Round(row.WholesalePrice, 2), WholesaleMinimumQuantity = decimal.Round(row.WholesaleMinimumQuantity, 3), Stock = 0m, IsActive = true };
                ApplyCatalogFields(product, row, ResolveSupplier(row.SupplierName, suppliers));
                database.Products.Add(product);
                products.Add(normalized, product);
                AddStockMovement(product, row.Stock, userId.Value, command.OperationId, row.RowNumber);
                created++;
            }
        }

        var batch = new ImportBatchRecord { Id = Guid.NewGuid(), OperationId = command.OperationId, UserId = userId.Value, SourceFileName = Path.GetFileName(command.SourceFileName), DuplicateRule = command.DuplicateRule, CreatedCount = created, UpdatedCount = updated, SkippedCount = skipped, CreatedAtUtc = DateTimeOffset.UtcNow };
        database.ImportBatches.Add(batch);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(batch.Id, created, updated, skipped, false);
    }

    private void AddStockMovement(ProductRecord product, decimal requestedStock, Guid userId, Guid batchOperationId, int rowNumber)
    {
        var stock = decimal.Round(requestedStock, 3);
        if (product.Stock == stock) return;
        var before = product.Stock;
        product.Stock = stock;
        database.InventoryMovements.Add(new InventoryMovementRecord { Id = Guid.NewGuid(), ProductId = product.Id, UserId = userId, OperationId = RowOperationId(batchOperationId, rowNumber), Quantity = stock - before, StockBefore = before, StockAfter = stock, Reason = "Importacion de inventario", CreatedAtUtc = DateTimeOffset.UtcNow });
    }

    private SupplierRecord? ResolveSupplier(string supplierName, Dictionary<string, SupplierRecord> suppliers)
    {
        if (string.IsNullOrWhiteSpace(supplierName)) return null;
        var key = NormalizeName(supplierName);
        if (suppliers.TryGetValue(key, out var existing)) return existing;
        var supplier = new SupplierRecord { Id = Guid.NewGuid(), Name = supplierName.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow };
        database.Suppliers.Add(supplier);
        suppliers.Add(key, supplier);
        return supplier;
    }

    private static void ApplyCatalogFields(ProductRecord product, ProductImportRow row, SupplierRecord? supplier)
    {
        product.Category = row.Category.Trim();
        product.UnitOfMeasure = string.IsNullOrWhiteSpace(row.UnitOfMeasure) ? "Pieza" : row.UnitOfMeasure.Trim();
        product.MinimumStock = decimal.Round(row.MinimumStock, 3);
        product.MaximumStock = decimal.Round(row.MaximumStock, 3);
        product.PrimarySupplierId = supplier?.Id;
    }

    private static string NormalizeName(string value) => value.Trim().ToUpperInvariant();

    private static Guid RowOperationId(Guid operationId, int rowNumber)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{operationId:N}:{rowNumber}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static void Validate(ProductImportCommand command)
    {
        if (command.OperationId == Guid.Empty || command.Rows.Count == 0 || command.Rows.Count > 100000) throw new ArgumentException("La importación debe contener entre 1 y 100000 productos.");
        if (!command.DuplicateRule.Equals("Skip", StringComparison.OrdinalIgnoreCase) && !command.DuplicateRule.Equals("Update", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("La regla de duplicados debe ser Omitir o Actualizar.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in command.Rows)
        {
            var code = ProductCatalogService.NormalizeCode(row.Code);
            if (string.IsNullOrWhiteSpace(code) || code.Length > 80 || string.IsNullOrWhiteSpace(row.Description) || row.Description.Trim().Length > 200) throw new ArgumentException($"Fila {row.RowNumber}: código y descripción son obligatorios.");
            if (!seen.Add(code)) throw new ArgumentException($"Fila {row.RowNumber}: el código {row.Code} está repetido dentro del archivo.");
            if (row.Price < 0 || row.Cost < 0 || row.Stock < 0 || row.WholesalePrice < 0 || row.WholesaleMinimumQuantity < 0) throw new ArgumentException($"Fila {row.RowNumber}: precios, costo y existencia no pueden ser negativos.");
            if (row.MinimumStock < 0 || row.MaximumStock < 0 || (row.MaximumStock > 0 && row.MaximumStock < row.MinimumStock)) throw new ArgumentException($"Fila {row.RowNumber}: los mÃ­nimos y mÃ¡ximos de inventario no son vÃ¡lidos.");
            if (row.Category.Trim().Length > 100 || row.UnitOfMeasure.Trim().Length > 30 || row.SupplierName.Trim().Length > 160) throw new ArgumentException($"Fila {row.RowNumber}: departamento, tipo de venta o proveedor supera la longitud permitida.");
            if (row.WholesalePrice > 0 && row.WholesaleMinimumQuantity <= 0) throw new ArgumentException($"Fila {row.RowNumber}: el precio de mayoreo requiere cantidad mínima.");
        }
    }

    private async Task<Guid?> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ImportOrExportData", cancellationToken) ? user.Id : null;
    }
}
