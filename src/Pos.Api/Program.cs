using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;
using Pos.Infrastructure;

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
app.MapGet("/api/ticket-settings", async (HttpRequest request, TicketSettingsService settings, CancellationToken cancellationToken) => { var result = await settings.GetAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(new { result.TicketHeader, result.TicketFooter, result.TicketWidthMm }); });
app.MapPut("/api/ticket-settings", async (HttpRequest request, TicketSettingsCommand command, TicketSettingsService settings, CancellationToken cancellationToken) => { try { var result = await settings.UpdateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(new { result.TicketHeader, result.TicketFooter, result.TicketWidthMm }); } catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["ticket"] = [exception.Message] }); } });

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
});
app.MapPut("/api/users/{userId:guid}/permissions", async (Guid userId, HttpRequest request, UserPermissionsCommand command, UserAdministrationService users, CancellationToken cancellationToken) =>
{
    try { var result = await users.SetPermissionsAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), userId, command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["permissions"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
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
        .Select(product => new { product.Id, product.Code, product.Description, product.Price })
        .ToListAsync(cancellationToken);
    return Results.Ok(products);
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

app.MapPost("/api/promotions", async (HttpRequest request, PromotionCommand command, PromotionService promotions, CancellationToken cancellationToken) =>
{
    try { var result = await promotions.CreateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Created($"/api/promotions/{result.Id}", result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["promotion"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
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
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.MapPost("/api/sales/cancel", async (HttpRequest request, CancelSaleCommand command, SaleReversalService reversals, CancellationToken cancellationToken) =>
{
    try { var result = await reversals.CancelAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["sale"] = [exception.Message] }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
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

app.MapGet("/api/sales/{saleId:guid}/ticket.pdf", async (Guid saleId, HttpRequest request, TicketService tickets, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await tickets.GenerateAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), saleId, cancellationToken);
        return result is null ? Results.Unauthorized() : Results.File(result.Content, "application/pdf", result.FileName);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
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

app.MapGet("/api/shifts/register", async (PosDbContext database, CancellationToken cancellationToken) =>
{
    var register = await database.Registers.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.Name).Select(item => new { item.Id, item.Name }).FirstOrDefaultAsync(cancellationToken);
    return register is null ? Results.NotFound() : Results.Ok(register);
});

app.Run();
