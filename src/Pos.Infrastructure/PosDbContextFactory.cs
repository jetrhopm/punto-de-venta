using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace Pos.Infrastructure;

public sealed class PosDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args)
    {
        var connectionString = ReadConfiguredConnectionString();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseNpgsql(connectionString).Options;
        return new PosDbContext(options);
    }

    public static string ReadConfiguredConnectionString()
    {
        var encryptedPath = Environment.GetEnvironmentVariable("POS_CONNECTION_FILE");
        if (!string.IsNullOrWhiteSpace(encryptedPath) && File.Exists(encryptedPath))
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("La configuracion DPAPI de produccion solo esta disponible en Windows.");
            }
            var encrypted = File.ReadAllBytes(encryptedPath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }

        return Environment.GetEnvironmentVariable("POS_CONNECTION_STRING") ?? ReadDevelopmentConnectionString();
    }

    public static string ReadDevelopmentConnectionString()
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
