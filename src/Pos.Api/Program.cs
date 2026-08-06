using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("POS_CONNECTION_STRING") ?? PosDbContextFactory.ReadDevelopmentConnectionString();
builder.Services.AddDbContext<PosDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<PasswordHasher<UserRecord>>();
builder.Services.AddScoped<InitialSetupService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<ShiftService>();
builder.Services.AddScoped<ProductCatalogService>();
builder.Services.AddScoped<SaleService>();
builder.Services.AddScoped<CashRegisterService>();

var app = builder.Build();

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

app.MapPost("/api/sales/complete", async (HttpRequest request, CompleteSaleCommand command, SaleService sales, CancellationToken cancellationToken) =>
{
    try { var result = await sales.CompleteAsync(request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase), command, cancellationToken); return result is null ? Results.Unauthorized() : Results.Ok(result); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["sale"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
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
