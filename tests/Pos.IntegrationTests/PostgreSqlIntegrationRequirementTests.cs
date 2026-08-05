namespace Pos.IntegrationTests;

public sealed class PostgreSqlIntegrationRequirementTests
{
    [Fact(Skip = "Pendiente: dev-setup.ps1 debe preparar el cluster PostgreSQL aislado antes de agregar pruebas de persistencia real.")]
    public void RequiresAnIsolatedPostgreSqlCluster()
    {
    }
}
