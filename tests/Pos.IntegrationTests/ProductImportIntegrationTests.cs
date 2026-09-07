using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class ProductImportIntegrationTests
{
    [Fact]
    public async Task CreatesRealPostgreSqlBackupWithChecksum()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "BACKUP_" + Guid.NewGuid().ToString("N"), DisplayName = "Backup test", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        database.Users.Add(user);
        database.Sessions.Add(new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) });
        await database.SaveChangesAsync();
        BackupResult? result = null;
        try
        {
            result = await new DatabaseMaintenanceService(database).CreateAsync(token, CancellationToken.None);
            Assert.NotNull(result);
            Assert.True(result.SizeBytes > 0);
            Assert.Equal(64, result.Sha256.Length);
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "backups");
            Assert.True(File.Exists(Path.Combine(directory, result.FileName)));
            Assert.Equal(result.Sha256, File.ReadAllText(Path.Combine(directory, result.FileName + ".sha256")).Trim());
        }
        finally
        {
            if (result is not null)
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "backups", result.FileName);
                File.Delete(path); File.Delete(path + ".sha256"); File.Delete(path + ".json");
            }
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Users.Remove(user);
            await database.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RepeatedOperationReturnsExistingResultWithoutDuplicatingStock()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "IMPORT_" + Guid.NewGuid().ToString("N"), DisplayName = "Import test", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        database.Users.Add(user); database.Sessions.Add(session); await database.SaveChangesAsync();
        var operationId = Guid.NewGuid();
        var updateOperationId = Guid.NewGuid();
        var code = "IMPORT-" + Guid.NewGuid().ToString("N");
        var normalizedCode = ProductCatalogService.NormalizeCode(code);
        var supplierName = "Proveedor " + Guid.NewGuid().ToString("N");
        var updatedSupplierName = "Proveedor actualizado " + Guid.NewGuid().ToString("N");
        try
        {
            var service = new ProductImportService(database);
            var command = new ProductImportCommand(operationId, "productos.xlsx", "Skip", [new ProductImportRow(2, code, "Producto importado", 20m, 10m, 5m, 0m, 0m, "Abarrotes", 2m, 10m, "Pieza", supplierName)]);
            var first = await service.ImportAsync(token, command, CancellationToken.None);
            var repeated = await service.ImportAsync(token, command, CancellationToken.None);
            Assert.NotNull(first); Assert.NotNull(repeated); Assert.False(first.ExistingResult); Assert.True(repeated.ExistingResult); Assert.Equal(first.ImportId, repeated.ImportId);
            Assert.Equal(1, await database.Products.CountAsync(item => item.NormalizedCode == normalizedCode));
            var productId = await database.Products.Where(item => item.NormalizedCode == normalizedCode).Select(item => item.Id).SingleAsync();
            Assert.Equal(1, await database.InventoryMovements.CountAsync(item => item.ProductId == productId));
            Assert.Equal(5m, await database.Products.Where(item => item.Id == productId).Select(item => item.Stock).SingleAsync());
            Assert.Equal("Abarrotes", await database.Products.Where(item => item.Id == productId).Select(item => item.Category).SingleAsync());
            Assert.Equal(supplierName, await database.Products.Where(item => item.Id == productId).Select(item => item.PrimarySupplierId).Join(database.Suppliers, id => id, supplier => supplier.Id, (_, supplier) => supplier.Name).SingleAsync());

            var update = await service.ImportAsync(token, new ProductImportCommand(updateOperationId, "productos-actualizados.xlsx", "Update", [new ProductImportRow(2, code, "Producto actualizado", 33m, 17m, 9m, 28m, 2m, "Bebidas", 3m, 15m, "Kilogramo", updatedSupplierName)]), CancellationToken.None);
            Assert.NotNull(update); Assert.Equal(0, update.Created); Assert.Equal(1, update.Updated); Assert.Equal(0, update.Skipped);
            var updatedProduct = await database.Products.SingleAsync(item => item.Id == productId);
            Assert.Equal("Producto actualizado", updatedProduct.Description); Assert.Equal(33m, updatedProduct.Price); Assert.Equal(17m, updatedProduct.Cost); Assert.Equal(28m, updatedProduct.WholesalePrice); Assert.Equal(2m, updatedProduct.WholesaleMinimumQuantity); Assert.Equal(9m, updatedProduct.Stock); Assert.Equal("Bebidas", updatedProduct.Category); Assert.Equal("Kilogramo", updatedProduct.UnitOfMeasure);
            Assert.Equal(2, await database.InventoryMovements.CountAsync(item => item.ProductId == productId));
            Assert.Equal(updatedSupplierName, await database.Products.Where(item => item.Id == productId).Select(item => item.PrimarySupplierId).Join(database.Suppliers, id => id, supplier => supplier.Id, (_, supplier) => supplier.Name).SingleAsync());
        }
        finally
        {
            var product = await database.Products.SingleOrDefaultAsync(item => item.NormalizedCode == normalizedCode);
            if (product is not null) { database.InventoryMovements.RemoveRange(database.InventoryMovements.Where(item => item.ProductId == product.Id)); database.Products.Remove(product); }
            database.Suppliers.RemoveRange(database.Suppliers.Where(item => item.Name == supplierName || item.Name == updatedSupplierName));
            database.ImportBatches.RemoveRange(database.ImportBatches.Where(item => item.OperationId == operationId || item.OperationId == updateOperationId));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id)); database.Users.Remove(user); await database.SaveChangesAsync();
        }
    }
}
