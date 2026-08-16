using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record DiagnosticCheckResult(string Name, string Status, string Detail, string Action);

public sealed record DiagnosticReport(
    DateTimeOffset CheckedAtUtc,
    string ApiVersion,
    IReadOnlyList<DiagnosticCheckResult> Checks,
    int ProductCount,
    int UserCount,
    int CustomerCount,
    int SupplierCount,
    int CompletedSaleCount,
    int OpenTicketCount,
    int PendingPrintJobCount,
    int BackupCount,
    string? LatestBackup,
    string? LatestBackupSha256,
    long? FreeBytes);

public sealed class SystemDiagnosticsService(PosDbContext database)
{
    public async Task<DiagnosticReport?> RunAsync(string token, string apiVersion, CancellationToken cancellationToken)
    {
        if (!await AuthorizedAsync(token, cancellationToken)) return null;

        var checks = new List<DiagnosticCheckResult>();
        var productCount = 0;
        var userCount = 0;
        var customerCount = 0;
        var supplierCount = 0;
        var completedSaleCount = 0;
        var openTicketCount = 0;
        var pendingPrintJobCount = 0;

        if (!await database.Database.CanConnectAsync(cancellationToken))
        {
            checks.Add(new("Base de datos", "Problema", "PostgreSQL no acepta conexiones en este momento.", "Usa Reparar servicios desde la ventana de conexión."));
            return await BuildReportAsync(checks, apiVersion, productCount, userCount, customerCount, supplierCount, completedSaleCount, openTicketCount, pendingPrintJobCount, cancellationToken);
        }

        checks.Add(new("PostgreSQL", "Correcto", "La base de datos acepta conexiones.", ""));

        var pendingMigrations = (await database.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        checks.Add(pendingMigrations.Length == 0
            ? new("Migraciones", "Correcto", "El esquema está actualizado.", "")
            : new("Migraciones", "Problema", $"Hay {pendingMigrations.Length} migración(es) pendiente(s).", "Reinicia JetVenta o usa Reparar servicios."));

        try
        {
            var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
            checks.Add(store is null
                ? new("Tienda", "Pendiente", "La base responde, pero todavía no hay una tienda configurada.", "Completa la configuración inicial.")
                : new("Tienda", "Correcto", $"Tienda configurada: {store.Name}.", ""));

            productCount = await database.Products.CountAsync(item => item.IsActive, cancellationToken);
            userCount = await database.Users.CountAsync(item => item.IsActive, cancellationToken);
            customerCount = await database.Customers.CountAsync(item => item.IsActive, cancellationToken);
            supplierCount = await database.Suppliers.CountAsync(cancellationToken);
            completedSaleCount = await database.Sales.CountAsync(item => item.Status == "Completed", cancellationToken);
            openTicketCount = await database.SaleDrafts.CountAsync(item => item.Status == "Open", cancellationToken);
            pendingPrintJobCount = await database.PrintJobs.CountAsync(item => item.Status == "Pending" || item.Status == "Processing", cancellationToken);

            checks.Add(openTicketCount == 0
                ? new("Tickets pendientes", "Correcto", "No hay tickets abiertos pendientes de recuperar.", "")
                : new("Tickets pendientes", "Aviso", $"Hay {openTicketCount} ticket(s) guardado(s) para recuperar.", "Revísalos en Ventas antes de cerrar el turno."));
            checks.Add(pendingPrintJobCount == 0
                ? new("Cola de impresión", "Correcto", "No hay trabajos de impresión pendientes.", "")
                : new("Cola de impresión", "Aviso", $"Hay {pendingPrintJobCount} trabajo(s) pendiente(s).", "Revisa la impresora y reimprime solo si es necesario."));
        }
        catch (Exception)
        {
            checks.Add(new("Datos de la tienda", "Problema", "La base responde, pero no se pudieron leer todas las tablas.", "Revisa las migraciones y ejecuta Reparar servicios."));
        }

        var backup = await InspectLatestBackupAsync(cancellationToken);
        checks.Add(backup.Count == 0
            ? new("Respaldos", "Aviso", "No hay respaldos locales disponibles.", "Crea un respaldo y guarda una copia externa." )
            : backup.Verified
                ? new("Respaldos", "Correcto", $"El respaldo más reciente tiene checksum válido. Hay {backup.Count} respaldo(s).", "Conserva también una copia fuera de este equipo.")
                : new("Respaldos", "Problema", "El respaldo más reciente no tiene un checksum válido.", "Crea un respaldo nuevo y no uses la copia dañada."));

        var freeBytes = GetFreeBytes();
        checks.Add(freeBytes is null
            ? new("Espacio disponible", "Aviso", "No se pudo consultar el espacio libre del equipo servidor.", "Revisa el almacenamiento desde Windows.")
            : freeBytes < 2L * 1024 * 1024 * 1024
                ? new("Espacio disponible", "Problema", $"Quedan {freeBytes.Value / 1024d / 1024d / 1024d:0.##} GB libres.", "Libera espacio antes de respaldar o actualizar JetVenta.")
                : new("Espacio disponible", "Correcto", $"Hay {freeBytes.Value / 1024d / 1024d / 1024d:0.##} GB libres.", ""));

        return new DiagnosticReport(
            DateTimeOffset.UtcNow,
            apiVersion,
            checks,
            productCount,
            userCount,
            customerCount,
            supplierCount,
            completedSaleCount,
            openTicketCount,
            pendingPrintJobCount,
            backup.Count,
            backup.FileName,
            backup.Sha256,
            freeBytes);
    }

    private static async Task<DiagnosticReport> BuildReportAsync(List<DiagnosticCheckResult> checks, string apiVersion, int productCount, int userCount, int customerCount, int supplierCount, int completedSaleCount, int openTicketCount, int pendingPrintJobCount, CancellationToken cancellationToken)
    {
        var backup = await InspectLatestBackupAsync(cancellationToken);
        return new DiagnosticReport(DateTimeOffset.UtcNow, apiVersion, checks, productCount, userCount, customerCount, supplierCount, completedSaleCount, openTicketCount, pendingPrintJobCount, backup.Count, backup.FileName, backup.Sha256, GetFreeBytes());
    }

    private static async Task<(int Count, string? FileName, string? Sha256, bool Verified)> InspectLatestBackupAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "backups");
        if (!Directory.Exists(directory)) return (0, null, null, false);
        var files = Directory.GetFiles(directory, "*.dump").Select(path => new FileInfo(path)).OrderByDescending(file => file.CreationTimeUtc).ToArray();
        if (files.Length == 0) return (0, null, null, false);

        var latest = files[0];
        var checksumPath = latest.FullName + ".sha256";
        if (!File.Exists(checksumPath)) return (files.Length, latest.Name, null, false);
        var expected = (await File.ReadAllTextAsync(checksumPath, cancellationToken)).Trim().ToUpperInvariant();
        await using var stream = File.OpenRead(latest.FullName);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return (files.Length, latest.Name, actual, expected == actual);
    }

    private static long? GetFreeBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (IOException) { return null; }
    }

    private async Task<bool> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return false;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        return user is not null && (user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken));
    }
}
