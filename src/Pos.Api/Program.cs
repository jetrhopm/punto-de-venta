using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("POS_CONNECTION_STRING") ?? PosDbContextFactory.ReadDevelopmentConnectionString();
builder.Services.AddDbContext<PosDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<PasswordHasher<UserRecord>>();
builder.Services.AddScoped<InitialSetupService>();

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

app.MapPost("/api/setup/initial", async (InitialSetupCommand command, InitialSetupService setup, CancellationToken cancellationToken) =>
{
    try { return Results.Created("/api/setup/initial", await setup.ExecuteAsync(command, cancellationToken)); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["setup"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.Run();
