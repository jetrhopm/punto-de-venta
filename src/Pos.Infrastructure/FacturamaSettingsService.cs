using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record FacturamaSettingsResult(
    bool Enabled,
    string Environment,
    string AccountEmail,
    bool CredentialsStored,
    DateTimeOffset? LastVerifiedAtUtc,
    string LastVerificationMessage);

public sealed record ConfigureFacturamaCommand(bool Enabled, string Environment, string AccountEmail, string? Password);
public sealed record FacturamaConnectionTestResult(bool Connected, string Message, DateTimeOffset CheckedAtUtc);

/// <summary>
/// Store-owned Facturama API Web configuration. Credentials remain protected on
/// the local server and are never returned to desktop clients or secondary boxes.
/// </summary>
public sealed class FacturamaSettingsService(PosDbContext database, HttpClient client)
{
    private const string SandboxUrl = "https://apisandbox.facturama.mx/";
    private const string ProductionUrl = "https://api.facturama.mx/";

    public async Task<FacturamaSettingsResult?> GetAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, cancellationToken) is null) return null;
        var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        return ToResult(store);
    }

    public async Task<FacturamaSettingsResult?> UpdateAsync(string token, ConfigureFacturamaCommand command, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, cancellationToken) is null) return null;
        var environment = NormalizeEnvironment(command.Environment);
        var accountEmail = command.AccountEmail?.Trim() ?? string.Empty;
        var password = command.Password?.Trim() ?? string.Empty;
        if (accountEmail.Length > 200 || (accountEmail.Length > 0 && !accountEmail.Contains('@', StringComparison.Ordinal)))
            throw new ArgumentException("Escribe el correo de la cuenta Facturama.");

        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        var changingAccount = !string.Equals(store.FacturamaAccountEmail, accountEmail, StringComparison.OrdinalIgnoreCase);
        if (command.Enabled && (string.IsNullOrWhiteSpace(accountEmail) || (string.IsNullOrWhiteSpace(store.FacturamaPasswordProtected) && string.IsNullOrWhiteSpace(password)) || (changingAccount && string.IsNullOrWhiteSpace(password))))
            throw new ArgumentException("Para activar Facturama indica el correo y la contraseña de la cuenta de este negocio.");

        store.FacturamaEnabled = command.Enabled;
        store.FacturamaEnvironment = environment;
        store.FacturamaAccountEmail = accountEmail;
        if (!string.IsNullOrWhiteSpace(password)) store.FacturamaPasswordProtected = Protect(password);
        if (changingAccount && string.IsNullOrWhiteSpace(password)) store.FacturamaPasswordProtected = string.Empty;
        store.FacturamaLastVerifiedAtUtc = null;
        store.FacturamaLastVerificationMessage = command.Enabled
            ? "Credenciales guardadas. Usa Probar conexión antes de solicitar timbrados."
            : "Facturama está desactivado. Las solicitudes y ventas existentes no se modificaron.";
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(store);
    }

    public async Task<FacturamaConnectionTestResult?> TestConnectionAsync(string token, CancellationToken cancellationToken)
    {
        if (await AuthorizedAsync(token, cancellationToken) is null) return null;
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        if (!store.FacturamaEnabled || string.IsNullOrWhiteSpace(store.FacturamaAccountEmail) || string.IsNullOrWhiteSpace(store.FacturamaPasswordProtected))
            throw new InvalidOperationException("Guarda y activa primero las credenciales de Facturama.");

        var checkedAt = DateTimeOffset.UtcNow;
        FacturamaConnectionTestResult result;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(BaseUrl(store.FacturamaEnvironment)), "api-lite/csds"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{store.FacturamaAccountEmail}:{Unprotect(store.FacturamaPasswordProtected)}")));
            using var response = await client.SendAsync(request, cancellationToken);
            result = response.IsSuccessStatusCode
                ? new(true, "Conexión correcta. La cuenta Facturama respondió. Administra el CSD desde el portal de Facturama.", checkedAt)
                : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? new(false, "Facturama rechazó las credenciales. Revisa el correo, contraseña y ambiente seleccionado.", checkedAt)
                    : new(false, $"Facturama no aceptó la verificación (HTTP {(int)response.StatusCode}). Vuelve a intentarlo más tarde.", checkedAt);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new(false, "Facturama tardó demasiado en responder. Verifica la conexión a Internet e inténtalo de nuevo.", checkedAt);
        }
        catch (HttpRequestException)
        {
            result = new(false, "No se pudo contactar a Facturama. Verifica que este servidor tenga acceso a Internet.", checkedAt);
        }

        store.FacturamaLastVerifiedAtUtc = checkedAt;
        store.FacturamaLastVerificationMessage = result.Message;
        await database.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static string NormalizeEnvironment(string? environment) => environment?.Trim() switch
    {
        "Sandbox" => "Sandbox",
        "Production" => "Production",
        _ => throw new ArgumentException("Selecciona Sandbox o Producción para Facturama.")
    };

    private static string BaseUrl(string environment) => environment == "Production" ? ProductionUrl : SandboxUrl;
    private static FacturamaSettingsResult ToResult(StoreRecord store) => new(store.FacturamaEnabled, store.FacturamaEnvironment, store.FacturamaAccountEmail, !string.IsNullOrWhiteSpace(store.FacturamaPasswordProtected), store.FacturamaLastVerifiedAtUtc, store.FacturamaLastVerificationMessage);

    private async Task<UserRecord?> AuthorizedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        return user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken) ? user : null;
    }

    private static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protección de credenciales de JetVenta requiere Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine));
    }

    private static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protección de credenciales de JetVenta requiere Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.LocalMachine));
    }
}
