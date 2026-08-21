using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pos.Domain;

public sealed record JetVentaLicenseRequest(int Version, string Product, string MachineFingerprint);

public sealed record JetVentaLicenseClaims(
    int Version,
    string Product,
    string LicenseId,
    string MachineFingerprint,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string StoreName);

public sealed record JetVentaSignedLicense(JetVentaLicenseClaims License, string Signature);

/// <summary>
/// Protocol shared by JetVenta and the offline license issuer. The issuer private key is never distributed.
/// </summary>
public static class JetVentaLicensing
{
    public const string Product = "JetVenta";
    public const int ProtocolVersion = 1;

    // ECDSA P-256 public key. The matching private key exists only in the local issuer vault.
    public const string IssuerPublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE1VgsdG8RqdNWHvlZHHyt/D2t3OPICXgrqotG1hgiBG5x5RkFsV9SxZDxtOewysmgSyWh9Z2HFNaOK7t08A03sA==";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string CreateRequestCode(string machineFingerprint)
    {
        var request = new JetVentaLicenseRequest(ProtocolVersion, Product, machineFingerprint);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions)));
    }

    public static bool TryReadRequestCode(string requestCode, out JetVentaLicenseRequest? request, out string error)
    {
        request = null;
        error = string.Empty;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(requestCode.Trim()));
            request = JsonSerializer.Deserialize<JetVentaLicenseRequest>(json, JsonOptions);
            if (request is null || request.Version != ProtocolVersion || !string.Equals(request.Product, Product, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(request.MachineFingerprint))
            {
                error = "El código de solicitud no corresponde a JetVenta.";
                request = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            error = "El código de solicitud no tiene un formato válido.";
            return false;
        }
    }

    public static string Serialize(JetVentaSignedLicense license) => JsonSerializer.Serialize(license, JsonOptions);

    public static bool TryReadLicense(string content, out JetVentaSignedLicense? license, out string error)
    {
        license = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > 16 * 1024)
        {
            error = "El archivo de licencia está vacío o es demasiado grande.";
            return false;
        }

        try
        {
            license = JsonSerializer.Deserialize<JetVentaSignedLicense>(content, JsonOptions);
            if (license?.License is null || string.IsNullOrWhiteSpace(license.Signature) || !IsValidClaims(license.License, out error))
            {
                license = null;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "El archivo de licencia no tiene un formato reconocido.";
            return false;
        }
    }

    public static string Sign(JetVentaLicenseClaims claims, ECDsa privateKey) =>
        Convert.ToBase64String(privateKey.SignData(CanonicalPayload(claims), HashAlgorithmName.SHA256));

    public static bool Verify(JetVentaSignedLicense signedLicense)
    {
        if (!IsValidClaims(signedLicense.License, out _)) return false;
        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(IssuerPublicKeyBase64), out _);
            return verifier.VerifyData(CanonicalPayload(signedLicense.License), Convert.FromBase64String(signedLicense.Signature), HashAlgorithmName.SHA256);
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
    }

    public static byte[] CanonicalPayload(JetVentaLicenseClaims claims)
    {
        var expiration = claims.ExpiresAtUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
        var store = Convert.ToBase64String(Encoding.UTF8.GetBytes(claims.StoreName ?? string.Empty));
        var payload = string.Join("\n",
            "JetVenta-Licencia",
            claims.Version.ToString(CultureInfo.InvariantCulture),
            claims.Product,
            claims.LicenseId,
            claims.MachineFingerprint,
            claims.IssuedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            expiration,
            store);
        return Encoding.UTF8.GetBytes(payload);
    }

    private static bool IsValidClaims(JetVentaLicenseClaims claims, out string error)
    {
        error = string.Empty;
        if (claims.Version != ProtocolVersion || !string.Equals(claims.Product, Product, StringComparison.Ordinal)) { error = "La licencia no corresponde a esta versión de JetVenta."; return false; }
        if (!Guid.TryParse(claims.LicenseId, out _)) { error = "La licencia no tiene un identificador válido."; return false; }
        if (string.IsNullOrWhiteSpace(claims.MachineFingerprint) || claims.MachineFingerprint.Length > 256) { error = "La licencia no contiene un identificador de equipo válido."; return false; }
        if (claims.StoreName?.Length > 200) { error = "La licencia contiene un nombre de tienda inválido."; return false; }
        if (claims.ExpiresAtUtc is not null && claims.ExpiresAtUtc < claims.IssuedAtUtc) { error = "La vigencia de la licencia es inválida."; return false; }
        return true;
    }
}
