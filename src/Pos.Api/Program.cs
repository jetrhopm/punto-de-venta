using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;
using Pos.Infrastructure;
using System.Security.Cryptography;
using System.Text;

var startupLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "logs", "api-startup.log");
void WriteStartupLog(string message)
{
    Directory.CreateDirectory(Path.GetDirectoryName(startupLog)!);
    File.AppendAllText(startupLog, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
}

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => WriteStartupLog($"ERROR NO CONTROLADO: {eventArgs.ExceptionObject}");
WriteStartupLog("Iniciando API.");
var webOptions = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
};
var builder = WebApplication.CreateBuilder(webOptions);
builder.Host.UseWindowsService(options => options.ServiceName = "PuntoDeVentaApi");
builder.WebHost.UseUrls(builder.Configuration["Pos:Urls"] ?? Environment.GetEnvironmentVariable("POS_API_URLS") ?? "http://127.0.0.1:5000");
var connectionString = PosDbContextFactory.ReadConfiguredConnectionString(builder.Configuration["Pos:ConnectionFile"]);
builder.Services.AddDbContext<PosDbContext>(options => options.UseNpgsql(connectionString).ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddScoped<PasswordHasher<UserRecord>>();
builder.Services.AddScoped<InitialSetupService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<ProductCatalogService>();
builder.Services.AddScoped<SaleService>();
builder.Services.AddScoped<SaleDraftService>();
builder.Services.AddScoped<SalesHistoryService>();
builder.Services.AddScoped<CashRegisterService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<UserAdministrationService>();
builder.Services.AddScoped<CustomerCreditService>();
builder.Services.AddScoped<SupplierPurchaseService>();
builder.Services.AddScoped<SaleReversalService>();
builder.Services.AddScoped<SaleReturnService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<KitService>();
builder.Services.AddScoped<TicketSettingsService>();
builder.Services.AddScoped<LanPairingService>();
builder.Services.AddScoped<StoreSettingsService>();
builder.Services.AddScoped<SaleFolioService>();
builder.Services.AddScoped<MeasureSettingsService>();
builder.Services.AddScoped<CurrencySettingsService>();
builder.Services.AddScoped<PaymentMethodSettingsService>();
builder.Services.AddScoped<ProductImportService>();
builder.Services.AddScoped<DatabaseMaintenanceService>();
builder.Services.AddHostedService<DailyBackupHostedService>();

var app = builder.Build();

try
{
    const int migrationAttempts = 12;
    for (var attempt = 1; attempt <= migrationAttempts; attempt++)
    {
        try
        {
            await using var migrationScope = app.Services.CreateAsyncScope();
            var database = migrationScope.ServiceProvider.GetRequiredService<PosDbContext>();
            await database.Database.MigrateAsync();
            break;
        }
        catch (Exception exception) when (attempt < migrationAttempts)
        {
            WriteStartupLog($"PostgreSQL todavía no está disponible. Reintento {attempt}/{migrationAttempts}: {exception.Message}");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
catch (Exception exception)
{
    WriteStartupLog($"ERROR AL INICIAR O APLICAR MIGRACIONES: {exception}");
    throw;
}
WriteStartupLog("Migraciones aplicadas. API lista para recibir solicitudes.");

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        service = "Pos.Api",
        status = "ok",
        utc = DateTimeOffset.UtcNow
    });
})
.WithName("Health");

app.MapGet("/api/setup/status", async (PosDbContext database, CancellationToken cancellationToken) =>
{
    var store = await database.Stores.AsNoTracking().OrderBy(store => store.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
    return Results.Ok(new { configured = store is not null, storeName = store?.Name });
});

app.MapGet("/api/lan/info", () => Results.Ok(new
{
    service = "Pos.Api",
    protocolVersion = "1",
    apiVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
    machine = Environment.MachineName
}));
app.MapPost("/api/lan/pairing-codes", async (HttpRequest request, LanPairingService pairing, CancellationToken cancellationToken) =>
{
    var result = await pairing.CreateCodeAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPost("/api/lan/pair", async (PairDeviceCommand command, LanPairingService pairing, CancellationToken cancellationToken) =>
{
    try { var result = await pairing.PairAsync(command, cancellationToken); return result is null ? Results.BadRequest(new { message = "Codigo invalido, usado o expirado." }) : Results.Ok(result); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapGet("/api/ticket-settings", async (HttpRequest request, TicketSettingsService settings, CancellationToken cancellationToken) => { var result = await settings.GetAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(new { result.Name, result.LegalName, result.TaxId, result.Address, result.Phone, result.TicketHeader, result.TicketFooter, result.TicketWidthMm }); });
app.MapPut("/api/ticket-settings", async (HttpRequest request, TicketSettingsCommand command, TicketSettingsService settings, CancellationToken cancellationToken) => { try { var result = await settings.UpdateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(new { result.Name, result.LegalName, result.TaxId, result.Address, result.Phone, result.TicketHeader, result.TicketFooter, result.TicketWidthMm }); } catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["ticket"] = [exception.Message] }); } });
app.MapGet("/api/currency-settings", async (HttpRequest request, CurrencySettingsService settings, CancellationToken cancellationToken) => { var result = await settings.GetAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); });
app.MapPut("/api/currency-settings", async (HttpRequest request, SetCurrencySettingsCommand command, CurrencySettingsService settings, CancellationToken cancellationToken) => { try { var result = await settings.UpdateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); } catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["currency"] = [exception.Message] }); } });
app.MapGet("/api/payment-method-settings", async (HttpRequest request, PaymentMethodSettingsService settings, CancellationToken cancellationToken) => { var result = await settings.GetAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); });
app.MapPut("/api/payment-method-settings", async (HttpRequest request, SetPaymentMethodSettingsCommand command, PaymentMethodSettingsService settings, CancellationToken cancellationToken) => { try { var result = await settings.UpdateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); } catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["paymentMethods"] = [exception.Message] }); } });

app.MapGet("/api/store-settings", async (HttpRequest request, StoreSettingsService settings, CancellationToken cancellationToken) =>
{
    var result = await settings.GetAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPut("/api/store-settings", async (HttpRequest request, StoreSettingsCommand command, StoreSettingsService settings, CancellationToken cancellationToken) =>
{
    try { var result = await settings.UpdateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["store"] = [exception.Message] }); }
});
app.MapGet("/api/sale-folios", async (HttpRequest request, SaleFolioService folios, CancellationToken cancellationToken) =>
{
    var result = await folios.GetAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPut("/api/sale-folios", async (HttpRequest request, SetNextSaleFolioCommand command, SaleFolioService folios, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await folios.SetNextAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["folio"] = [exception.Message] }); }
});
app.MapGet("/api/measure-settings", async (HttpRequest request, MeasureSettingsService settings, CancellationToken cancellationToken) =>
{
    var result = await settings.GetAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPut("/api/measure-settings", async (HttpRequest request, SetMeasureSettingsCommand command, MeasureSettingsService settings, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await settings.UpdateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["unit"] = [exception.Message] }); }
});
app.MapPost("/api/products/import", async (HttpRequest request, ProductImportCommand command, ProductImportService importer, CancellationToken cancellationToken) =>
{
    try { var result = await importer.ImportAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["import"] = [exception.Message] }); }
});
app.MapGet("/api/maintenance/backups", async (HttpRequest request, DatabaseMaintenanceService maintenance, CancellationToken cancellationToken) =>
{
    var result = await maintenance.ListAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPost("/api/maintenance/backups", async (HttpRequest request, DatabaseMaintenanceService maintenance, CancellationToken cancellationToken) =>
{
    try { var result = await maintenance.CreateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (InvalidOperationException exception) { return Results.Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError); }
});
app.MapGet("/api/maintenance/backups/{fileName}", async (string fileName, HttpRequest request, DatabaseMaintenanceService maintenance, CancellationToken cancellationToken) =>
{
    try
    {
        var path = await maintenance.ResolveFileAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), fileName, cancellationToken);
        return path is null ? Results.Unauthorized() : Results.File(path, "application/octet-stream", Path.GetFileName(path));
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
    catch (FileNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});

app.MapGet("/api/users", async (HttpRequest request, UserAdministrationService users, CancellationToken cancellationToken) =>
{
    var result = await users.ListAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPost("/api/users", async (HttpRequest request, UserCommand command, UserAdministrationService users, CancellationToken cancellationToken) =>
{
    try { var result = await users.CreateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Created($"/api/users/{result.Id}", result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["user"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapPut("/api/users/{userId:guid}/status", async (Guid userId, HttpRequest request, UserStatusCommand command, UserAdministrationService users, CancellationToken cancellationToken) =>
{
    try { var result = await users.SetStatusAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), userId, command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapPut("/api/users/{userId:guid}/password", async (Guid userId, HttpRequest request, UserPasswordCommand command, UserAdministrationService users, CancellationToken cancellationToken) =>
{
    try { var result = await users.ResetPasswordAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), userId, command.Password, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapPut("/api/users/{userId:guid}/permissions", async (Guid userId, HttpRequest request, UserPermissionsCommand command, UserAdministrationService users, CancellationToken cancellationToken) =>
{
    try { var result = await users.SetPermissionsAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), userId, command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["permissions"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.MapGet("/api/customers", async (string? q, HttpRequest request, CustomerCreditService customers, CancellationToken cancellationToken) =>
{
    var result = await customers.ListAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), q, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPost("/api/customers", async (HttpRequest request, CustomerCommand command, CustomerCreditService customers, CancellationToken cancellationToken) =>
{
    try { var result = await customers.CreateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Created($"/api/customers/{result.Id}", result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["customer"] = [exception.Message] }); }
});
app.MapPut("/api/customers/{customerId:guid}", async (Guid customerId, HttpRequest request, CustomerCommand command, CustomerCreditService customers, CancellationToken cancellationToken) =>
{
    try { var result = await customers.UpdateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), customerId, command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["customer"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapPost("/api/customers/credit-payments", async (HttpRequest request, CreditPaymentCommand command, CustomerCreditService customers, CancellationToken cancellationToken) =>
{
    try { var result = await customers.ApplyPaymentAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["credit"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapGet("/api/customers/{customerId:guid}/statement", async (Guid customerId, HttpRequest request, CustomerCreditService customers, CancellationToken cancellationToken) =>
{
    var result = await customers.StatementAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), customerId, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});

app.MapGet("/api/suppliers", async (string? q, HttpRequest request, SupplierPurchaseService suppliers, CancellationToken cancellationToken) =>
{
    var result = await suppliers.ListSuppliersAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), q, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPost("/api/suppliers", async (HttpRequest request, SupplierCommand command, SupplierPurchaseService suppliers, CancellationToken cancellationToken) =>
{
    try { var result = await suppliers.CreateSupplierAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Created($"/api/suppliers/{result.Id}", result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["supplier"] = [exception.Message] }); }
});
app.MapPost("/api/purchases/receive", async (HttpRequest request, ReceivePurchaseCommand command, SupplierPurchaseService purchases, CancellationToken cancellationToken) =>
{
    try { var result = await purchases.ReceiveAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["purchase"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});

app.MapGet("/api/products/search", async (string? q, PosDbContext database, CancellationToken cancellationToken) =>
{
    var search = (q ?? string.Empty).Trim();
    if (search.Length == 0) return Results.Ok(Array.Empty<object>());
    var normalized = search.ToUpperInvariant();
    var products = await database.Products.AsNoTracking()
        .Where(product => product.IsActive && (product.NormalizedCode.Contains(normalized) || product.Description.ToUpper().Contains(normalized)))
        .OrderBy(product => product.Description).Take(30)
        .Select(product => new { product.Id, product.Code, product.Description, product.Category, product.Price, product.Cost, product.WholesalePrice, product.WholesaleMinimumQuantity, product.Stock, product.MinimumStock, product.MaximumStock, product.IsKit, product.UnitOfMeasure, product.IsActive })
        .ToListAsync(cancellationToken);
    return Results.Ok(products);
});

app.MapGet("/api/products/catalog", async (string? q, Guid? departmentId, decimal? minimumPrice, decimal? maximumPrice, decimal? minimumProfit, string? sort, bool descending, int? page, ProductCatalogService catalog, HttpRequest request, CancellationToken cancellationToken) =>
{
    var result = await catalog.CatalogAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), q, departmentId, minimumPrice, maximumPrice, minimumProfit, sort ?? "description", descending, page ?? 1, 500, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/departments", async (ProductCatalogService catalog, HttpRequest request, CancellationToken cancellationToken) =>
{
    var result = await catalog.ListDepartmentsAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPost("/api/departments", async (DepartmentCommand command, ProductCatalogService catalog, HttpRequest request, CancellationToken cancellationToken) =>
{
    try { var result = await catalog.CreateDepartmentAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command.Name, cancellationToken); return result is null ? Results.Unauthorized() : Results.Created($"/api/departments/{result.Id}", result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["department"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapPut("/api/departments/{id:guid}", async (Guid id, DepartmentCommand command, ProductCatalogService catalog, HttpRequest request, CancellationToken cancellationToken) =>
{
    try { var result = await catalog.UpdateDepartmentAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), id, command.Name, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["department"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapDelete("/api/departments/{id:guid}", async (Guid id, ProductCatalogService catalog, HttpRequest request, CancellationToken cancellationToken) =>
{
    try { var result = await catalog.DeactivateDepartmentAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), id, cancellationToken); return result is null ? Results.Unauthorized() : Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});

app.MapPost("/api/products/quick-sale", async (HttpRequest request, ProductCommand command, PosDbContext database, CancellationToken cancellationToken) =>
{
    var token = request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
    var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
    if (session is null) return Results.Unauthorized();
    var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
    if (user is null) return Results.Unauthorized();
    var requiredPermission = command.IsCommonProduct ? "UseCommonProduct" : "ManageProducts";
    var allowed = user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == requiredPermission, cancellationToken);
    if (!allowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (string.IsNullOrWhiteSpace(command.Code) || string.IsNullOrWhiteSpace(command.Description) || command.Price < 0m) return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = ["Codigo, descripcion y precio valido son obligatorios."] });

    var normalized = ProductCatalogService.NormalizeCode(command.Code);
    var existing = await database.Products.AsNoTracking().SingleOrDefaultAsync(item => item.NormalizedCode == normalized && item.IsActive, cancellationToken);
    if (existing is not null) return Results.Ok(new { existing.Id, existing.Code, existing.Description, existing.Price, existing.UnitOfMeasure });

    var product = new ProductRecord
    {
        Id = Guid.NewGuid(),
        Code = command.Code.Trim(),
        NormalizedCode = normalized,
        Description = command.Description.Trim(),
        Price = decimal.Round(command.Price, 2),
        Cost = decimal.Round(command.Cost, 2),
        WholesalePrice = decimal.Round(command.WholesalePrice, 2),
        WholesaleMinimumQuantity = decimal.Round(command.WholesaleMinimumQuantity, 3),
        UnitOfMeasure = string.IsNullOrWhiteSpace(command.UnitOfMeasure) ? "Pieza" : command.UnitOfMeasure.Trim(),
        Stock = 0m,
        IsActive = true
    };
    database.Products.Add(product);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/products/{product.Id}", new { product.Id, product.Code, product.Description, product.Price, product.UnitOfMeasure });
});

app.MapPost("/api/products", async (HttpRequest request, ProductCommand command, ProductCatalogService catalog, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await catalog.CreateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Created($"/api/products/{result.Id}", result);
    }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.MapPut("/api/products/{id:guid}", async (Guid id, HttpRequest request, ProductCommand command, ProductCatalogService catalog, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await catalog.UpdateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), id, command, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["product"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.MapDelete("/api/products/{id:guid}", async (Guid id, HttpRequest request, ProductCatalogService catalog, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await catalog.DeactivateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), id, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.NoContent();
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});
app.MapPost("/api/promotions", async (HttpRequest request, PromotionCommand command, PromotionService promotions, CancellationToken cancellationToken) =>
{
    try { var result = await promotions.CreateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Created($"/api/promotions/{result.Id}", result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["promotion"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});
app.MapGet("/api/promotions", async (Guid? productId, HttpRequest request, PromotionService promotions, CancellationToken cancellationToken) =>
{
    var result = await promotions.ListAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), productId, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/promotions/quote", async (Guid productId, decimal price, decimal quantity, HttpRequest request, PromotionService promotions, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await promotions.QuoteAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), productId, price, quantity, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["promotion"] = [exception.Message] }); }
});
app.MapDelete("/api/promotions/{id:guid}", async (Guid id, HttpRequest request, PromotionService promotions, CancellationToken cancellationToken) =>
{
    var result = await promotions.DeactivateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), id, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.NoContent();
});
app.MapPost("/api/kits", async (HttpRequest request, KitCommand command, KitService kits, CancellationToken cancellationToken) =>
{
    try { var result = await kits.SetAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["kit"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});

app.MapPost("/api/sales/complete", async (HttpRequest request, CompleteSaleCommand command, SaleService sales, CancellationToken cancellationToken) =>
{
    try { var result = await sales.CompleteAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["sale"] = [exception.Message] }); }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.MapGet("/api/sale-drafts", async (HttpRequest request, SaleDraftService drafts, CancellationToken cancellationToken) =>
{
    var result = await drafts.ListOpenAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPost("/api/sale-drafts", async (HttpRequest request, SaleDraftService drafts, CancellationToken cancellationToken) =>
{
    var result = await drafts.CreateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Created($"/api/sale-drafts/{result.Id}", result);
});
app.MapPut("/api/sale-drafts/{draftId:guid}", async (Guid draftId, HttpRequest request, SaveSaleDraftLinesCommand command, SaleDraftService drafts, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await drafts.SaveLinesAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), draftId, command, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["draft"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});
app.MapDelete("/api/sale-drafts/{draftId:guid}", async (Guid draftId, HttpRequest request, SaleDraftService drafts, CancellationToken cancellationToken) =>
{
    var result = await drafts.DiscardAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), draftId, cancellationToken);
    return result is null ? Results.Unauthorized() : result.Value ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/sales/cancel", async (HttpRequest request, CancelSaleCommand command, SaleReversalService reversals, CancellationToken cancellationToken) =>
{
    try { var result = await reversals.CancelAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["sale"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapGet("/api/sales/history", async (DateTimeOffset from, DateTimeOffset to, Guid? userId, string? q, HttpRequest request, SalesHistoryService history, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await history.ListAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), new SalesHistoryFilter(from, to, userId, q), cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
});
app.MapGet("/api/sales/history/cashiers", async (HttpRequest request, SalesHistoryService history, CancellationToken cancellationToken) =>
{
    var result = await history.CashiersAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/sales/history/{saleId:guid}", async (Guid saleId, HttpRequest request, SalesHistoryService history, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await history.DetailAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), saleId, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});
app.MapPost("/api/sales/return", async (HttpRequest request, ReturnSaleCommand command, SaleReturnService returns, CancellationToken cancellationToken) =>
{
    try { var result = await returns.ReturnAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["return"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapGet("/api/sales/{saleId:guid}/return-lines", async (Guid saleId, HttpRequest request, SaleReturnService returns, CancellationToken cancellationToken) =>
{
    var result = await returns.LinesAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), saleId, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/reports/sales", async (DateTimeOffset from, DateTimeOffset to, HttpRequest request, ReportService reports, CancellationToken cancellationToken) =>
{
    var result = await reports.SalesAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), from, to, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/reports/sales.csv", async (DateTimeOffset from, DateTimeOffset to, HttpRequest request, ReportService reports, CancellationToken cancellationToken) =>
{
    var content = await reports.SalesCsvAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), from, to, cancellationToken); return content is null ? Results.Unauthorized() : Results.File(content, "text/csv", $"ventas-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
});
app.MapGet("/api/reports/analysis", async (HttpRequest request, ReportService reports, CancellationToken cancellationToken) =>
{
    var result = await reports.SalesAnalysisAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/reports/dashboard", async (DateTimeOffset from, DateTimeOffset to, HttpRequest request, ReportService reports, CancellationToken cancellationToken) =>
{
    if (to <= from) return Results.BadRequest(new { message = "El periodo final debe ser posterior al inicial." });
    var result = await reports.SalesDashboardAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), from, to, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/reports/inventory", async (HttpRequest request, ReportService reports, CancellationToken cancellationToken) =>
{
    var result = await reports.InventoryAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/reports/credit", async (HttpRequest request, ReportService reports, CancellationToken cancellationToken) =>
{
    var result = await reports.CreditAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});

app.MapPost("/api/inventory/adjust", async (HttpRequest request, InventoryAdjustmentCommand command, InventoryService inventory, CancellationToken cancellationToken) =>
{
    try { var result = await inventory.AdjustAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["inventory"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
app.MapGet("/api/inventory/catalog", async (string? q, string? status, string? sort, bool descending, int? page, HttpRequest request, InventoryService inventory, CancellationToken cancellationToken) =>
{
    var result = await inventory.CatalogAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), q, status, sort ?? "description", descending, page ?? 1, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapGet("/api/inventory/export", async (HttpRequest request, InventoryService inventory, CancellationToken cancellationToken) =>
{
    var result = await inventory.ExportCsvAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.Unauthorized() : Results.File(result, "text/csv", $"inventario-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
});
app.MapGet("/api/inventory/movements", async (string? q, int? page, HttpRequest request, InventoryService inventory, CancellationToken cancellationToken) =>
{
    var result = await inventory.MovementsAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), q, page ?? 1, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});
app.MapPost("/api/inventory/limits", async (HttpRequest request, InventoryLimitChangeCommand command, InventoryService inventory, CancellationToken cancellationToken) =>
{
    try { var result = await inventory.UpdateLimitsAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["inventory"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});

app.MapGet("/api/sales/{saleId:guid}/ticket.pdf", async (Guid saleId, HttpRequest request, TicketService tickets, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await tickets.GenerateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), saleId, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.File(result.Content, "application/pdf", result.FileName);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});

app.MapGet("/api/sales/{saleId:guid}/ticket-data", async (Guid saleId, HttpRequest request, TicketService tickets, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await tickets.GetDataAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), saleId, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
});

app.MapPost("/api/sales/{saleId:guid}/ticket/printed", async (Guid saleId, HttpRequest request, TicketService tickets, CancellationToken cancellationToken) =>
{
    var result = await tickets.MarkPrintedAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), saleId, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(new { printed = result.Value });
});

app.MapGet("/api/inventory/{productId:guid}/kardex", async (Guid productId, HttpRequest request, InventoryService inventory, CancellationToken cancellationToken) =>
{
    var result = await inventory.KardexAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), productId, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});

app.MapPost("/api/shifts/cash-movements", async (HttpRequest request, CashMovementCommand command, CashRegisterService cash, CancellationToken cancellationToken) =>
{
    try { var result = await cash.AddMovementAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["cash"] = [exception.Message] }); }
});

app.MapPost("/api/shifts/close", async (HttpRequest request, CloseShiftCommand command, CashRegisterService cash, CancellationToken cancellationToken) =>
{
    try { var result = await cash.CloseAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["cash"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.MapGet("/api/shifts/summary", async (HttpRequest request, CashRegisterService cash, CancellationToken cancellationToken) =>
{
    var result = await cash.CurrentSummaryAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/api/shifts/cut", async (HttpRequest request, CashRegisterService cash, CancellationToken cancellationToken) =>
{
    var result = await cash.CurrentCutAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/api/shifts/cut/cashiers", async (HttpRequest request, string date, CashRegisterService cash, CancellationToken cancellationToken) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var selectedDate)) return Results.BadRequest(new { message = "La fecha debe tener formato AAAA-MM-DD." });
    var result = await cash.CashiersForDayAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), selectedDate, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/shifts/cut/day", async (HttpRequest request, string date, Guid? cashierId, CashRegisterService cash, CancellationToken cancellationToken) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var selectedDate)) return Results.BadRequest(new { message = "La fecha debe tener formato AAAA-MM-DD." });
    var result = await cash.CutForDayAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), selectedDate, cashierId, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/api/setup/initial", async (InitialSetupCommand command, InitialSetupService setup, CancellationToken cancellationToken) =>
{
    try { return Results.Created("/api/setup/initial", await setup.ExecuteAsync(command, cancellationToken)); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["setup"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.MapPost("/api/auth/login", async (LoginCommand command, AuthenticationService authentication, CancellationToken cancellationToken) =>
{
    var result = await authentication.LoginAsync(command, cancellationToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});

app.MapPost("/api/shifts/open", async (HttpRequest request, OpenShiftCommand command, ShiftService shifts, CancellationToken cancellationToken) =>
{
    var token = request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    try
    {
        var result = await shifts.OpenAsync(token, command, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.MapGet("/api/shifts/current", async (HttpRequest request, ShiftService shifts, CancellationToken cancellationToken) =>
{
    var token = request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    var result = await shifts.CurrentAsync(token, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/api/shifts/register", async (PosDbContext database, CancellationToken cancellationToken) =>
{
    var register = await database.Registers.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.Name).Select(item => new { item.Id, item.Name }).FirstOrDefaultAsync(cancellationToken);
    return register is null ? Results.NotFound() : Results.Ok(register);
});

app.Run();
