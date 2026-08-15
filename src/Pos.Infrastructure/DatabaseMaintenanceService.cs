using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pos.Infrastructure;

public sealed record BackupResult(string FileName, long SizeBytes, string Sha256, DateTimeOffset CreatedAtUtc);

public sealed class DatabaseMaintenanceService(PosDbContext database)
{
    private static readonly SemaphoreSlim BackupLock = new(1, 1);
    public async Task<IReadOnlyList<BackupResult>?> ListAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, cancellationToken) is null) return null;
        var directory = BackupDirectory();
        Directory.CreateDirectory(directory);
        return Directory.GetFiles(directory, "*.dump")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Select(file => new BackupResult(file.Name, file.Length, File.Exists(file.FullName + ".sha256") ? File.ReadAllText(file.FullName + ".sha256").Trim() : string.Empty, file.CreationTimeUtc))
            .ToArray();
    }

    public async Task<BackupResult?> CreateAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, cancellationToken) is null) return null;
        return await CreateCoreAsync(cancellationToken);
    }

    public async Task<bool> EnsureDailyAutomaticBackupAsync(CancellationToken cancellationToken)
    {
        if (!await database.Stores.AsNoTracking().AnyAsync(cancellationToken)) return false;
        var directory = BackupDirectory();
        Directory.CreateDirectory(directory);
        if (Directory.GetFiles(directory, "*.dump").Select(path => new FileInfo(path)).Any(file => file.CreationTime.Date == DateTime.Now.Date)) return false;
        await CreateCoreAsync(cancellationToken);
        return true;
    }

    private async Task<BackupResult> CreateCoreAsync(CancellationToken cancellationToken)
    {
        await BackupLock.WaitAsync(cancellationToken);
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(database.Database.GetConnectionString());
            var pgDump = FindPgDump();
            var directory = BackupDirectory();
            Directory.CreateDirectory(directory);
            var createdAt = DateTimeOffset.UtcNow;
            var path = Path.Combine(directory, $"punto-venta-{createdAt:yyyyMMdd-HHmmss}.dump");
            var start = new ProcessStartInfo(pgDump) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            start.ArgumentList.Add($"--host={connection.Host}");
            start.ArgumentList.Add($"--port={connection.Port}");
            start.ArgumentList.Add($"--username={connection.Username}");
            start.ArgumentList.Add($"--dbname={connection.Database}");
            start.ArgumentList.Add("--format=custom");
            start.ArgumentList.Add($"--file={path}");
            start.ArgumentList.Add("--no-password");
            start.Environment["PGPASSWORD"] = connection.Password;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar pg_dump.");
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) { File.Delete(path); throw new InvalidOperationException($"PostgreSQL no pudo crear el respaldo: {error.Trim()}"); }
            await using var backupStream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(backupStream, cancellationToken));
            await File.WriteAllTextAsync(path + ".sha256", hash, cancellationToken);
            var info = new FileInfo(path);
            var result = new BackupResult(info.Name, info.Length, hash, createdAt);
            await File.WriteAllTextAsync(path + ".json", JsonSerializer.Serialize(result), cancellationToken);
            TrimLocalBackups(directory, 5);
            return result;
        }
        finally
        {
            BackupLock.Release();
        }
    }

    public async Task<string?> ResolveFileAsync(string token, string fileName, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, cancellationToken) is null) return null;
        var safeName = Path.GetFileName(fileName);
        if (!safeName.Equals(fileName, StringComparison.Ordinal) || !safeName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Nombre de respaldo inválido.");
        var path = Path.Combine(BackupDirectory(), safeName);
        return File.Exists(path) ? path : throw new FileNotFoundException("Respaldo no encontrado.");
    }

    private static string FindPgDump()
    {
        var configured = Environment.GetEnvironmentVariable("POS_PG_BIN");
        var candidates = new List<string>
        {
            string.IsNullOrWhiteSpace(configured) ? string.Empty : Path.Combine(configured, "pg_dump.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "postgresql", "pgsql", "bin", "pg_dump.exe"))
        };
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            candidates.Add(Path.Combine(directory.FullName, ".tools", "postgresql-18.4", "pgsql", "bin", "pg_dump.exe"));
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("No se encontró pg_dump dentro de la instalación.");
    }

    private static string BackupDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "backups");

    private static void TrimLocalBackups(string directory, int keep)
    {
        foreach (var obsolete in Directory.GetFiles(directory, "*.dump").Select(path => new FileInfo(path)).OrderByDescending(file => file.CreationTimeUtc).Skip(keep))
        {
            File.Delete(obsolete.FullName);
            File.Delete(obsolete.FullName + ".sha256");
            File.Delete(obsolete.FullName + ".json");
        }
    }

    private async Task<Guid?> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ImportOrExportData", cancellationToken) ? user.Id : null;
    }
}
