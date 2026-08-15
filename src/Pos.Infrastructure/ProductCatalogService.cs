using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record ProductCommand(string Code, string Description, decimal Price, decimal Cost = 0m, decimal ProfitPercent = 20m, decimal WholesalePrice = 0m, decimal WholesaleProfitPercent = 0m, decimal WholesaleMinimumQuantity = 0m, bool IsKit = false, string UnitOfMeasure = "Pieza", Guid? DepartmentId = null, bool IsCommonProduct = false);
public sealed record DepartmentCommand(string Name);
public sealed record ProductResult(Guid Id, string Code, string Description, decimal Price, decimal Cost, decimal ProfitPercent, decimal WholesalePrice, decimal WholesaleProfitPercent, decimal WholesaleMinimumQuantity, Guid? DepartmentId, bool IsKit, string UnitOfMeasure, bool IsActive);
public sealed record CatalogProductResult(Guid Id, string Code, string Description, string Department, Guid? DepartmentId, decimal Cost, decimal Price, decimal ProfitPercent, decimal ProfitAmount, decimal WholesalePrice, decimal WholesaleProfitPercent, decimal WholesaleProfitAmount, decimal WholesaleMinimumQuantity, decimal Stock, decimal MinimumStock, decimal MaximumStock, string UnitOfMeasure, bool IsKit, bool IsActive);
public sealed record CatalogPageResult(IReadOnlyList<CatalogProductResult> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record DepartmentResult(Guid Id, string Name, bool IsActive);

public sealed class ProductCatalogService(PosDbContext database)
{
    public async Task<ProductResult?> CreateAsync(string accessToken, ProductCommand command, CancellationToken cancellationToken)
    {
        Validate(command);
        var userId = await GetAuthorizedUserAsync(accessToken, "ManageProducts", cancellationToken);
        if (userId is null) return null;
        var normalizedCode = NormalizeCode(command.Code);
        if (await database.Products.AnyAsync(product => product.NormalizedCode == normalizedCode, cancellationToken)) throw new InvalidOperationException("El codigo del producto ya existe.");
        await ValidateDepartmentAsync(command.DepartmentId, cancellationToken);
        var product = CreateRecord(command, normalizedCode);
        database.Products.Add(product);
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(product);
    }

    public async Task<ProductResult?> UpdateAsync(string accessToken, Guid id, ProductCommand command, CancellationToken cancellationToken)
    {
        Validate(command);
        var userId = await GetAuthorizedUserAsync(accessToken, "ManageProducts", cancellationToken);
        if (userId is null) return null;
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado.");
        var normalizedCode = NormalizeCode(command.Code);
        if (await database.Products.AnyAsync(item => item.Id != id && item.NormalizedCode == normalizedCode, cancellationToken)) throw new InvalidOperationException("El codigo del producto ya existe.");
        await ValidateDepartmentAsync(command.DepartmentId, cancellationToken);
        product.Code = command.Code.Trim(); product.NormalizedCode = normalizedCode; product.Description = command.Description.Trim(); product.Cost = decimal.Round(command.Cost, 2); product.ProfitPercent = decimal.Round(command.ProfitPercent, 2); product.Price = ResolvePrice(command.Price, product.Cost, product.ProfitPercent); product.WholesaleProfitPercent = decimal.Round(command.WholesaleProfitPercent, 2); product.WholesalePrice = ResolveWholesalePrice(command.WholesalePrice, product.Cost, product.WholesaleProfitPercent); product.WholesaleMinimumQuantity = decimal.Round(command.WholesaleMinimumQuantity, 3); product.UnitOfMeasure = NormalizeUnit(command.UnitOfMeasure); product.DepartmentId = command.DepartmentId; product.IsKit = command.IsKit;
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(product);
    }

    public async Task<bool?> DeactivateAsync(string accessToken, Guid id, CancellationToken cancellationToken)
    {
        var userId = await GetAuthorizedUserAsync(accessToken, "ManageProducts", cancellationToken);
        if (userId is null) return null;
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Producto no encontrado.");
        product.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    public async Task<IReadOnlyList<DepartmentResult>?> ListDepartmentsAsync(string accessToken, CancellationToken cancellationToken) => await GetAuthorizedUserAsync(accessToken, "ViewProducts", cancellationToken) is null ? null : await database.Departments.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.Name).Select(item => new DepartmentResult(item.Id, item.Name, item.IsActive)).ToListAsync(cancellationToken);
    public async Task<DepartmentResult?> CreateDepartmentAsync(string accessToken, string name, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(accessToken, "ManageProducts", cancellationToken) is null) return null;
        var normalized = NormalizeName(name); if (normalized.Length is 0 or > 100) throw new ArgumentException("El departamento debe tener entre 1 y 100 caracteres.");
        if (await database.Departments.AnyAsync(item => item.NormalizedName == normalized, cancellationToken)) throw new InvalidOperationException("El departamento ya existe.");
        var result = new DepartmentRecord { Id = Guid.NewGuid(), Name = name.Trim(), NormalizedName = normalized, CreatedAtUtc = DateTimeOffset.UtcNow }; database.Departments.Add(result); await database.SaveChangesAsync(cancellationToken); return new DepartmentResult(result.Id, result.Name, result.IsActive);
    }
    public async Task<DepartmentResult?> UpdateDepartmentAsync(string accessToken, Guid id, string name, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(accessToken, "ManageProducts", cancellationToken) is null) return null;
        var normalized = NormalizeName(name); var item = await database.Departments.SingleOrDefaultAsync(department => department.Id == id && department.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Departamento no encontrado.");
        if (normalized.Length is 0 or > 100) throw new ArgumentException("El departamento debe tener entre 1 y 100 caracteres."); if (await database.Departments.AnyAsync(department => department.Id != id && department.NormalizedName == normalized, cancellationToken)) throw new InvalidOperationException("El departamento ya existe."); item.Name = name.Trim(); item.NormalizedName = normalized; await database.SaveChangesAsync(cancellationToken); return new DepartmentResult(item.Id, item.Name, item.IsActive);
    }
    public async Task<bool?> DeactivateDepartmentAsync(string accessToken, Guid id, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(accessToken, "ManageProducts", cancellationToken) is null) return null; var item = await database.Departments.SingleOrDefaultAsync(department => department.Id == id && department.IsActive, cancellationToken) ?? throw new KeyNotFoundException("Departamento no encontrado."); item.IsActive = false; await database.SaveChangesAsync(cancellationToken); return true;
    }
    public async Task<CatalogPageResult?> CatalogAsync(string accessToken, string? query, Guid? departmentId, decimal? minimumPrice, decimal? maximumPrice, decimal? minimumProfit, string sort, bool descending, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (await GetAuthorizedUserAsync(accessToken, "ViewProducts", cancellationToken) is null) return null;
        page = Math.Max(1, page); pageSize = 500; var products = database.Products.AsNoTracking().Where(item => item.IsActive).Include(item => item.Department).AsQueryable();
        var text = query?.Trim().ToUpperInvariant(); if (!string.IsNullOrWhiteSpace(text)) products = products.Where(item => item.NormalizedCode.Contains(text) || item.Description.ToUpper().Contains(text));
        if (departmentId is not null) products = products.Where(item => item.DepartmentId == departmentId); if (minimumPrice is not null) products = products.Where(item => item.Price >= minimumPrice); if (maximumPrice is not null) products = products.Where(item => item.Price <= maximumPrice); if (minimumProfit is not null) products = products.Where(item => item.ProfitPercent >= minimumProfit);
        products = (sort.ToLowerInvariant()) switch { "code" => descending ? products.OrderByDescending(item => item.Code) : products.OrderBy(item => item.Code), "department" => descending ? products.OrderByDescending(item => item.Department!.Name) : products.OrderBy(item => item.Department!.Name), "cost" => descending ? products.OrderByDescending(item => item.Cost) : products.OrderBy(item => item.Cost), "price" => descending ? products.OrderByDescending(item => item.Price) : products.OrderBy(item => item.Price), "wholesale" => descending ? products.OrderByDescending(item => item.WholesalePrice) : products.OrderBy(item => item.WholesalePrice), "profit" => descending ? products.OrderByDescending(item => item.ProfitPercent) : products.OrderBy(item => item.ProfitPercent), "stock" => descending ? products.OrderByDescending(item => item.Stock) : products.OrderBy(item => item.Stock), "minimumstock" => descending ? products.OrderByDescending(item => item.MinimumStock) : products.OrderBy(item => item.MinimumStock), "maximumstock" => descending ? products.OrderByDescending(item => item.MaximumStock) : products.OrderBy(item => item.MaximumStock), _ => descending ? products.OrderByDescending(item => item.Description) : products.OrderBy(item => item.Description) };
        var total = await products.CountAsync(cancellationToken); var rows = await products.Skip((page - 1) * pageSize).Take(pageSize).Select(item => new CatalogProductResult(item.Id, item.Code, item.Description, item.Department == null ? string.Empty : item.Department.Name, item.DepartmentId, item.Cost, item.Price, item.ProfitPercent, item.Price - item.Cost, item.WholesalePrice, item.WholesaleProfitPercent, item.WholesalePrice > 0m ? item.WholesalePrice - item.Cost : 0m, item.WholesaleMinimumQuantity, item.Stock, item.MinimumStock, item.MaximumStock, item.UnitOfMeasure, item.IsKit, item.IsActive)).ToListAsync(cancellationToken); return new CatalogPageResult(rows, page, pageSize, total, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
    }
    private ProductRecord CreateRecord(ProductCommand command, string normalizedCode) => new() { Id = Guid.NewGuid(), Code = command.Code.Trim(), NormalizedCode = normalizedCode, Description = command.Description.Trim(), Price = ResolvePrice(command.Price, command.Cost, command.ProfitPercent), Cost = decimal.Round(command.Cost, 2), ProfitPercent = decimal.Round(command.ProfitPercent, 2), WholesalePrice = ResolveWholesalePrice(command.WholesalePrice, command.Cost, command.WholesaleProfitPercent), WholesaleProfitPercent = decimal.Round(command.WholesaleProfitPercent, 2), WholesaleMinimumQuantity = decimal.Round(command.WholesaleMinimumQuantity, 3), DepartmentId = command.DepartmentId, UnitOfMeasure = NormalizeUnit(command.UnitOfMeasure), IsKit = command.IsKit, IsActive = true };
    private async Task ValidateDepartmentAsync(Guid? id, CancellationToken cancellationToken) { if (id is not null && !await database.Departments.AnyAsync(item => item.Id == id && item.IsActive, cancellationToken)) throw new ArgumentException("El departamento no existe o esta inactivo."); }
    private static decimal ResolvePrice(decimal price, decimal cost, decimal profit) => price > 0m ? decimal.Round(price, 2) : decimal.Round(cost * (1m + profit / 100m), 2, MidpointRounding.AwayFromZero);
    private static decimal ResolveWholesalePrice(decimal price, decimal cost, decimal profit) => price > 0m ? decimal.Round(price, 2) : profit > 0m ? decimal.Round(cost * (1m + profit / 100m), 2, MidpointRounding.AwayFromZero) : 0m;
    private static string NormalizeName(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    private static ProductResult ToResult(ProductRecord product) => new(product.Id, product.Code, product.Description, product.Price, product.Cost, product.ProfitPercent, product.WholesalePrice, product.WholesaleProfitPercent, product.WholesaleMinimumQuantity, product.DepartmentId, product.IsKit, product.UnitOfMeasure, product.IsActive);
    private static string NormalizeUnit(string value) { var normalized = string.IsNullOrWhiteSpace(value) ? "Pieza" : value.Trim(); return normalized.Equals("Kilogramo", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Gramo", StringComparison.OrdinalIgnoreCase) ? "Granel (peso)" : normalized; }

    private async Task<Guid?> GetAuthorizedUserAsync(string accessToken, string permission, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        if (user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == permission, cancellationToken)) return user.Id;
        return null;
    }

    private static void Validate(ProductCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Code) || command.Code.Trim().Length > 80) throw new ArgumentException("El codigo es obligatorio y debe tener maximo 80 caracteres.");
        if (string.IsNullOrWhiteSpace(command.Description) || command.Description.Trim().Length > 200) throw new ArgumentException("La descripcion es obligatoria y debe tener maximo 200 caracteres.");
        if (command.UnitOfMeasure.Trim().Length > 30) throw new ArgumentException("La unidad de venta debe tener maximo 30 caracteres.");
        if (command.Price < 0m || command.Price > 9999999999999999.99m || command.Cost < 0m || command.Cost > 9999999999999999.99m || command.WholesalePrice < 0m || command.WholesaleMinimumQuantity < 0m || command.ProfitPercent is < 0m or > 1000m || command.WholesaleProfitPercent is < 0m or > 1000m || (command.WholesalePrice > 0m && command.WholesaleMinimumQuantity <= 0m)) throw new ArgumentException("Los precios, porcentajes y cantidad minima de mayoreo no son validos.");
    }
}
