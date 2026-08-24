using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using Pos.Domain;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pos.Infrastructure;

#pragma warning disable CA1416 // JetVenta production is Windows-only; development skips license storage.

public sealed record LicenseStatusResult(
    bool IsActive,
    string State,
    string Message,
    string MachineFingerprint,
    string RequestCode,
    string? LicenseId,
    DateTimeOffset? ExpiresAtUtc,
    string? StoreName);

public sealed record ImportLicenseCommand(string Content);

public sealed class LicenseService(PosDbContext database)
{
    private static readonly byte[] StorageEntropy = SHA256.HashData(Encoding.UTF8.GetBytes("JetVenta license storage v1"));
    private static readonly byte[] TrialEntropy = SHA256.HashData(Encoding.UTF8.GetBytes("JetVenta trial storage v1"));
    private static readonly string LicenseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "license");
    private static readonly string LicensePath = Path.Combine(LicenseDirectory, "licencia.jv.dpapi");
    private static readonly string TrialPath = Path.Combine(LicenseDirectory, "demo.jv.dpapi");

    // Temporalmente se dejan 30 minutos para probar la expiración sin esperar 30 días.
    // Antes de una liberación comercial se cambia únicamente a TimeSpan.FromDays(30).
    private static readonly TimeSpan TrialDuration = TimeSpan.FromMinutes(30);

    public LicenseStatusResult GetRuntimeStatus()
    {
        var fingerprint = GetMachineFingerprint();
        var request = JetVentaLicensing.CreateRequestCode(fingerprint);
        if (string.Equals(Environment.GetEnvironmentVariable("POS_LICENSE_BYPASS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "development", "Modo de desarrollo: validación de licencia omitida.", fingerprint, request, null, null, null);
        }

        if (!File.Exists(LicensePath))
        {
            return GetTrialStatus(fingerprint, request);
        }

        if (!TryReadStored(out var license, out var error))
        {
            return new(false, "invalid", error, fingerprint, request, null, null, null);
        }

        if (!JetVentaLicensing.Verify(license!))
        {
            return new(false, "invalid", "La firma de la licencia no es válida.", fingerprint, request, null, null, null);
        }

        var claims = license!.License;
        if (!string.Equals(claims.MachineFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new(false, "machine_mismatch", "La licencia pertenece a otro equipo. Genera un nuevo archivo de activación.", fingerprint, request, claims.LicenseId, claims.ExpiresAtUtc, claims.StoreName);
        }

        if (claims.ExpiresAtUtc is not null && claims.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return new(false, "expired", "La licencia venció. Solicita una renovación.", fingerprint, request, claims.LicenseId, claims.ExpiresAtUtc, claims.StoreName);
        }

        return new(true, "active", "Licencia válida para este equipo.", fingerprint, request, claims.LicenseId, claims.ExpiresAtUtc, claims.StoreName);
    }

    private static LicenseStatusResult GetTrialStatus(string fingerprint, string request)
    {
        var now = DateTimeOffset.UtcNow;
        if (!TryReadTrial(out var trial, out var error))
        {
            if (File.Exists(TrialPath))
            {
                return new(false, "trial_invalid", error, fingerprint, request, null, null, null);
            }

            trial = new TrialState(1, fingerprint, now, now);
            if (!TryWriteTrial(trial, out error))
            {
                return new(false, "trial_unavailable", error, fingerprint, request, null, null, null);
            }
        }

        if (!string.Equals(trial!.MachineFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new(false, "trial_machine_mismatch", "La prueba ya fue iniciada en otro equipo. Activa una licencia para continuar.", fingerprint, request, null, null, null);
        }

        if (trial.LastSeenAtUtc > now)
        {
            return new(false, "trial_clock_changed", "La fecha del equipo parece haber retrocedido. Corrígela o activa una licencia para continuar.", fingerprint, request, null, trial.StartedAtUtc + TrialDuration, null);
        }

        var expiresAt = trial.StartedAtUtc + TrialDuration;
        if (expiresAt <= now)
        {
            return new(false, "trial_expired", "El periodo de prueba terminó. Carga una licencia válida para continuar usando JetVenta.", fingerprint, request, null, expiresAt, null);
        }

        if (now > trial.LastSeenAtUtc)
        {
            trial = trial with { LastSeenAtUtc = now };
            if (!TryWriteTrial(trial, out error))
            {
                return new(false, "trial_unavailable", error, fingerprint, request, null, expiresAt, null);
            }
        }

        return new(true, "trial", $"No hay una licencia activada. Modo de prueba activo; te quedan {FormatTrialRemaining(expiresAt - now)}. Activa una licencia antes de que termine.", fingerprint, request, null, expiresAt, null);
    }

    private static string FormatTrialRemaining(TimeSpan remaining)
    {
        var totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        if (totalMinutes >= 60)
        {
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return minutes == 0 ? $"{hours} horas" : $"{hours} horas y {minutes} minutos";
        }

        return $"{totalMinutes} {(totalMinutes == 1 ? "minuto" : "minutos")}";
    }

    public async Task<LicenseStatusResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        return await IsAuthorizedAsync(token, requireConfigurationPermission: false, cancellationToken) ? GetRuntimeStatus() : null;
    }

    public async Task<LicenseStatusResult?> ImportAsync(string token, ImportLicenseCommand command, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(token, requireConfigurationPermission: true, cancellationToken)) return null;

        if (!JetVentaLicensing.TryReadLicense(command.Content, out var signedLicense, out var error)) throw new ArgumentException(error);
        if (!JetVentaLicensing.Verify(signedLicense!)) throw new ArgumentException("La firma de la licencia no es válida.");
        var fingerprint = GetMachineFingerprint();
        if (!string.Equals(signedLicense!.License.MachineFingerprint, fingerprint, StringComparison.Ordinal)) throw new ArgumentException("Esta licencia fue creada para otro equipo.");
        if (signedLicense.License.ExpiresAtUtc is not null && signedLicense.License.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new ArgumentException("Esta licencia ya venció.");

        Directory.CreateDirectory(LicenseDirectory);
        var bytes = Encoding.UTF8.GetBytes(JetVentaLicensing.Serialize(signedLicense));
        var protectedBytes = ProtectedData.Protect(bytes, StorageEntropy, DataProtectionScope.LocalMachine);
        var temporary = LicensePath + ".new";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, LicensePath, true);
        return GetRuntimeStatus();
    }

    public static string GetMachineFingerprint()
    {
        var machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", string.Empty)?.ToString() ?? string.Empty;
        var material = $"{machineGuid}|{Environment.MachineName}|{GetSystemVolumeSerial()}";
        return "JV1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
    }

    private static bool TryReadStored(out JetVentaSignedLicense? license, out string error)
    {
        license = null;
        error = "No hay una licencia activa en este equipo.";
        if (!File.Exists(LicensePath)) return false;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(LicensePath), StorageEntropy, DataProtectionScope.LocalMachine);
            return JetVentaLicensing.TryReadLicense(Encoding.UTF8.GetString(bytes), out license, out error);
        }
        catch (CryptographicException)
        {
            error = "La licencia local no pudo validarse en este equipo.";
            return false;
        }
        catch (IOException)
        {
            error = "No se pudo leer la licencia local.";
            return false;
        }
    }

    private static bool TryReadTrial(out TrialState? trial, out string error)
    {
        trial = null;
        error = "No se encontró el estado de la prueba.";
        if (!File.Exists(TrialPath)) return false;

        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(TrialPath), TrialEntropy, DataProtectionScope.LocalMachine);
            trial = JsonSerializer.Deserialize<TrialState>(bytes);
            if (trial is null || trial.Version != 1 || trial.StartedAtUtc == default || trial.LastSeenAtUtc == default)
            {
                error = "El estado local de la prueba no es válido.";
                return false;
            }

            return true;
        }
        catch (CryptographicException)
        {
            error = "El estado local de la prueba no pudo validarse en este equipo.";
            return false;
        }
        catch (JsonException)
        {
            error = "El estado local de la prueba está dañado.";
            return false;
        }
        catch (IOException)
        {
            error = "No se pudo leer el estado local de la prueba.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = "JetVenta no tiene permiso para consultar el estado de la prueba.";
            return false;
        }
    }

    private static bool TryWriteTrial(TrialState trial, out string error)
    {
        error = "No se pudo guardar el estado de la prueba.";
        try
        {
            Directory.CreateDirectory(LicenseDirectory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(trial);
            var protectedBytes = ProtectedData.Protect(bytes, TrialEntropy, DataProtectionScope.LocalMachine);
            var temporary = TrialPath + ".new";
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, TrialPath, true);
            return true;
        }
        catch (CryptographicException)
        {
            error = "Windows no pudo proteger el estado de la prueba.";
            return false;
        }
        catch (IOException)
        {
            error = "No se pudo guardar el estado de la prueba en ProgramData.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = "JetVenta no tiene permiso para guardar el estado de la prueba.";
            return false;
        }
    }

    private sealed record TrialState(int Version, string MachineFingerprint, DateTimeOffset StartedAtUtc, DateTimeOffset LastSeenAtUtc);

    private static string GetSystemVolumeSerial()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            return GetVolumeInformation(root, null, 0, out var serial, out _, out _, null, 0) ? serial.ToString("X8") : "unknown";
        }
        catch { return "unknown"; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(string rootPathName, StringBuilder? volumeNameBuffer, uint volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder? fileSystemNameBuffer, uint fileSystemNameSize);

    private async Task<bool> IsAuthorizedAsync(string token, bool requireConfigurationPermission, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return false;
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == session.UserId && item.IsActive, cancellationToken);
        if (user is null) return false;
        return !requireConfigurationPermission || user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == Permission.ConfigureStore.ToString(), cancellationToken);
    }
}

#pragma warning restore CA1416
