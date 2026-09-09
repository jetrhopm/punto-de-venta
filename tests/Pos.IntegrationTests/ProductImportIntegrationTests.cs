using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace Pos.IntegrationTests;

public sealed class ProductImportIntegrationTests
{
    [Fact]
    public async Task PreventsDepartmentsDuplicatedOnlyByAccentsOrCapitalization()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var suffix = Guid.NewGuid().ToString("N");
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "DEPARTMENT_" + suffix, DisplayName = "Department test", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        database.Users.Add(user);
        database.Sessions.Add(new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) });
        await database.SaveChangesAsync();
        var firstName = "Lácteos " + suffix;
        var equivalentName = "LACTEOS " + suffix;
        try
        {
            var catalog = new ProductCatalogService(database);
            var first = await catalog.CreateDepartmentAsync(token, firstName, CancellationToken.None);
            Assert.NotNull(first);
            Assert.True(await catalog.DeactivateDepartmentAsync(token, first.Id, CancellationToken.None));
            Assert.DoesNotContain((await catalog.ListDepartmentsAsync(token, false, CancellationToken.None))!, item => item.Id == first.Id);
            var includingInactive = await catalog.ListDepartmentsAsync(token, true, CancellationToken.None);
            Assert.Contains(includingInactive!, item => item.Id == first.Id && !item.IsActive);
            Assert.True(await catalog.SetDepartmentStatusAsync(token, first.Id, true, CancellationToken.None));
            Assert.Contains((await catalog.ListDepartmentsAsync(token, false, CancellationToken.None))!, item => item.Id == first.Id && item.IsActive);
            await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.CreateDepartmentAsync(token, equivalentName, CancellationToken.None));

            var second = await catalog.CreateDepartmentAsync(token, "Bebidas " + suffix, CancellationToken.None);
            Assert.NotNull(second);
            await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.UpdateDepartmentAsync(token, second.Id, equivalentName, CancellationToken.None));
        }
        finally
        {
            database.Departments.RemoveRange(database.Departments.Where(item => item.Name.EndsWith(suffix)));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Users.Remove(user);
            await database.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RejectsWholeBatchWhenAnySelectedRowIsInvalid()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);
        await database.Database.MigrateAsync();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var suffix = Guid.NewGuid().ToString("N");
        var user = new UserRecord { Id = Guid.NewGuid(), NormalizedUserName = "ATOMIC_" + suffix, DisplayName = "Atomic import", PasswordHash = "test", IsAdministrator = true, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var session = new SessionRecord { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var createdStore = !await database.Stores.AnyAsync();
        var store = createdStore ? new StoreRecord { Id = Guid.NewGuid(), Name = "Tienda atomic " + suffix, BusinessType = "Pruebas", CreatedAtUtc = DateTimeOffset.UtcNow } : null;
        database.AddRange(user, session);
        if (store is not null) database.Stores.Add(store);
        await database.SaveChangesAsync();
        var operationId = Guid.NewGuid();
        var validCode = "ATOMIC-" + suffix;
        try
        {
            var command = new ProductImportCommand(operationId, "lote.xlsx", "Skip",
            [
                new ProductImportRow(2, validCode, "Fila válida", 10m, 5m, 4m, 0m, 0m),
                new ProductImportRow(3, "INVALID-" + suffix, "Fila inválida", 10m, 5m, 4m, 0m, 0m, MinimumStock: 8m, MaximumStock: 2m)
            ]);

            await Assert.ThrowsAsync<ArgumentException>(() => new ProductImportService(database).ImportAsync(token, command, CancellationToken.None));

            Assert.False(await database.Products.AnyAsync(item => item.NormalizedCode == ProductCatalogService.NormalizeCode(validCode)));
            Assert.False(await database.ImportBatches.AnyAsync(item => item.OperationId == operationId));
            Assert.False(await database.InventoryMovements.AnyAsync(item => item.OperationId == operationId));
        }
        finally
        {
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id));
            database.Users.Remove(user);
            if (store is not null) database.Stores.Remove(store);
            await database.SaveChangesAsync();
        }
    }

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
        var departmentName = "Lácteos " + Guid.NewGuid().ToString("N");
        var equivalentDepartmentName = "LACTEOS " + departmentName.Split(' ')[1];
        try
        {
            var service = new ProductImportService(database);
            var command = new ProductImportCommand(operationId, "productos.xlsx", "Skip", [new ProductImportRow(2, code, "Producto importado", 20m, 10m, 5m, 0m, 0m, departmentName, 2m, 10m, "Pieza", supplierName)]);
            var first = await service.ImportAsync(token, command, CancellationToken.None);
            var repeated = await service.ImportAsync(token, command, CancellationToken.None);
            Assert.NotNull(first); Assert.NotNull(repeated); Assert.False(first.ExistingResult); Assert.True(repeated.ExistingResult); Assert.Equal(first.ImportId, repeated.ImportId);
            Assert.Equal(1, await database.Products.CountAsync(item => item.NormalizedCode == normalizedCode));
            var productId = await database.Products.Where(item => item.NormalizedCode == normalizedCode).Select(item => item.Id).SingleAsync();
            Assert.Equal(1, await database.InventoryMovements.CountAsync(item => item.ProductId == productId));
            Assert.Equal(5m, await database.Products.Where(item => item.Id == productId).Select(item => item.Stock).SingleAsync());
            Assert.Equal(departmentName, await database.Products.Where(item => item.Id == productId).Select(item => item.Category).SingleAsync());
            Assert.Equal(supplierName, await database.Products.Where(item => item.Id == productId).Select(item => item.PrimarySupplierId).Join(database.Suppliers, id => id, supplier => supplier.Id, (_, supplier) => supplier.Name).SingleAsync());

            var update = await service.ImportAsync(token, new ProductImportCommand(updateOperationId, "productos-actualizados.xlsx", "Update", [new ProductImportRow(2, code, "Producto actualizado", 33m, 17m, 9m, 28m, 2m, equivalentDepartmentName, 3m, 15m, "Kilogramo", updatedSupplierName)]), CancellationToken.None);
            Assert.NotNull(update); Assert.Equal(0, update.Created); Assert.Equal(1, update.Updated); Assert.Equal(0, update.Skipped);
            var updatedProduct = await database.Products.SingleAsync(item => item.Id == productId);
            Assert.Equal("Producto actualizado", updatedProduct.Description); Assert.Equal(33m, updatedProduct.Price); Assert.Equal(17m, updatedProduct.Cost); Assert.Equal(28m, updatedProduct.WholesalePrice); Assert.Equal(2m, updatedProduct.WholesaleMinimumQuantity); Assert.Equal(9m, updatedProduct.Stock); Assert.Equal(departmentName, updatedProduct.Category); Assert.Equal("Kilogramo", updatedProduct.UnitOfMeasure);
            var matchingDepartments = await database.Departments.Where(item => item.Name.EndsWith(departmentName.Split(' ')[1])).ToListAsync();
            var importedDepartment = Assert.Single(matchingDepartments);
            Assert.Equal(importedDepartment.Id, updatedProduct.DepartmentId);
            Assert.Equal(2, await database.InventoryMovements.CountAsync(item => item.ProductId == productId));
            Assert.Equal(updatedSupplierName, await database.Products.Where(item => item.Id == productId).Select(item => item.PrimarySupplierId).Join(database.Suppliers, id => id, supplier => supplier.Id, (_, supplier) => supplier.Name).SingleAsync());

            var exported = await new InventoryService(database).ExportCsvAsync(token, CancellationToken.None);
            Assert.NotNull(exported);
            var csv = Encoding.UTF8.GetString(exported);
            Assert.Contains("PrecioMayoreo,MinimoMayoreo", csv, StringComparison.Ordinal);
            Assert.Contains($"\"{code}\"", csv, StringComparison.Ordinal);
            Assert.Contains("28,2,9,3,15", csv, StringComparison.Ordinal);
            Assert.Contains($"\"{updatedSupplierName}\"", csv, StringComparison.Ordinal);
        }
        finally
        {
            var product = await database.Products.SingleOrDefaultAsync(item => item.NormalizedCode == normalizedCode);
            if (product is not null) { database.InventoryMovements.RemoveRange(database.InventoryMovements.Where(item => item.ProductId == product.Id)); database.Products.Remove(product); }
            database.Suppliers.RemoveRange(database.Suppliers.Where(item => item.Name == supplierName || item.Name == updatedSupplierName));
            database.Departments.RemoveRange(database.Departments.Where(item => item.Name.EndsWith(departmentName.Split(' ')[1])));
            database.ImportBatches.RemoveRange(database.ImportBatches.Where(item => item.OperationId == operationId || item.OperationId == updateOperationId));
            database.Sessions.RemoveRange(database.Sessions.Where(item => item.UserId == user.Id)); database.Users.Remove(user); await database.SaveChangesAsync();
        }
    }
}
