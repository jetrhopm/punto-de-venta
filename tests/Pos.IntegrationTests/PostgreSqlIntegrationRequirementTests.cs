using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure;

namespace Pos.IntegrationTests;

public sealed class PostgreSqlIntegrationTests
{
    [Fact]
    public async Task ConnectsToPostgreSqlAndFindsAppliedMigrations()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);

        Assert.True(await database.Database.CanConnectAsync());
        await database.Database.MigrateAsync();
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Contains(applied, migration => migration.Contains("CreaConfiguracionInicial", StringComparison.Ordinal));
        Assert.Contains(applied, migration => migration.Contains("AgregaCatalogoDeProductos", StringComparison.Ordinal));
        Assert.Contains(applied, migration => migration.Contains("AgregaRecibidoYCambioEnPagos", StringComparison.Ordinal));
        Assert.Contains(applied, migration => migration.Contains("AgregaColaDeImpresion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsProductCatalogFromTheRealDatabase()
    {
        await using var database = new PosDbContextFactory().CreateDbContext([]);

        var productCount = await database.Products.CountAsync();

        Assert.True(productCount >= 0);
    }
}
