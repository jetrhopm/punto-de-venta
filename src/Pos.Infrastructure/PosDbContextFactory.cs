using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace Pos.Infrastructure;

public sealed class PosDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POS_CONNECTION_STRING") ?? ReadDevelopmentConnectionString();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseNpgsql(connectionString).Options;
        return new PosDbContext(options);
    }

    private static string ReadDevelopmentConnectionString()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        string? path = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".postgres", "development-settings.json");
            if (File.Exists(candidate)) { path = candidate; break; }
            directory = directory.Parent;
        }
        if (path is null) throw new InvalidOperationException("No existe la configuracion local. Ejecuta scripts/dev-setup.ps1.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("ConnectionString").GetString() ?? throw new InvalidOperationException("No existe ConnectionString.");
    }
}
