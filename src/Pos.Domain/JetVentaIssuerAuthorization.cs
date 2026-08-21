using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pos.Domain;

public sealed record JetVentaIssuerEnrollmentRequest(
    int Version,
    string Product,
    string RequestId,
    string MachineFingerprint,
    string EncryptionPublicKey);

public sealed record JetVentaIssuerAuthorizationPackage(
    int Version,
    string Product,
    string RequestId,
    string MachineFingerprint,
    string EncryptedIssuerKey,
    string Signature);

/// <summary>
/// Offline protocol used to authorize an additional JetVenta license issuer.
/// The authorization package is signed by an existing issuer and encrypted for one destination computer.
/// </summary>
public static class JetVentaIssuerAuthorization
{
    public const int ProtocolVersion = 1;
    private const int MaximumDocumentBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string CreateEnrollmentRequestCode(string machineFingerprint, string encryptionPublicKey)
    {
        var request = new JetVentaIssuerEnrollmentRequest(
            ProtocolVersion,
            JetVentaLicensing.Product,
            Guid.NewGuid().ToString(),
            machineFingerprint,
            encryptionPublicKey);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions)));
    }

    public static bool TryReadEnrollmentRequest(string content, out JetVentaIssuerEnrollmentRequest? request, out string error)
    {
        request = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > MaximumDocumentBytes)
        {
            error = "La solicitud del emisor está vacía o es demasiado grande.";
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(content.Trim()));
            request = JsonSerializer.Deserialize<JetVentaIssuerEnrollmentRequest>(json, JsonOptions);
            if (!IsValidRequest(request, out error))
            {
                request = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            error = "La solicitud del emisor no tiene un formato válido.";
            return false;
        }
    }

    public static string CreateAuthorizationDocument(JetVentaIssuerEnrollmentRequest request, byte[] encryptedIssuerKey, ECDsa signer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encryptedIssuerKey);
        ArgumentNullException.ThrowIfNull(signer);
        if (!IsValidRequest(request, out var error)) throw new ArgumentException(error, nameof(request));

        var encryptedKey = Convert.ToBase64String(encryptedIssuerKey);
        var unsigned = new JetVentaIssuerAuthorizationPackage(
            ProtocolVersion,
            JetVentaLicensing.Product,
            request.RequestId,
            request.MachineFingerprint,
            encryptedKey,
            string.Empty);
        var signature = Convert.ToBase64String(signer.SignData(CanonicalPayload(unsigned), HashAlgorithmName.SHA256));
        return JsonSerializer.Serialize(unsigned with { Signature = signature }, JsonOptions);
    }

    public static bool TryReadAndVerifyAuthorization(string content, out JetVentaIssuerAuthorizationPackage? package, out string error)
    {
        package = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > MaximumDocumentBytes)
        {
            error = "El archivo de autorización está vacío o es demasiado grande.";
            return false;
        }

        try
        {
            package = JsonSerializer.Deserialize<JetVentaIssuerAuthorizationPackage>(content, JsonOptions);
            if (!IsValidPackage(package, out error))
            {
                package = null;
                return false;
            }

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(JetVentaLicensing.IssuerPublicKeyBase64), out _);
            if (!verifier.VerifyData(CanonicalPayload(package!), Convert.FromBase64String(package!.Signature), HashAlgorithmName.SHA256))
            {
                package = null;
                error = "La firma del archivo de autorización no es válida.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            package = null;
            error = "El archivo de autorización no tiene un formato válido.";
            return false;
        }
    }

    private static byte[] CanonicalPayload(JetVentaIssuerAuthorizationPackage package) =>
        Encoding.UTF8.GetBytes(string.Join("\n", "JetVenta-Emisor-Autorizacion", package.Version, package.Product, package.RequestId, package.MachineFingerprint, package.EncryptedIssuerKey));

    private static bool IsValidRequest(JetVentaIssuerEnrollmentRequest? request, out string error)
    {
        error = string.Empty;
        if (request is null || request.Version != ProtocolVersion || !string.Equals(request.Product, JetVentaLicensing.Product, StringComparison.Ordinal)) { error = "La solicitud no corresponde a JetVenta."; return false; }
        if (!Guid.TryParse(request.RequestId, out _)) { error = "La solicitud no tiene un identificador válido."; return false; }
        if (string.IsNullOrWhiteSpace(request.MachineFingerprint) || request.MachineFingerprint.Length > 256) { error = "La solicitud no contiene una huella de equipo válida."; return false; }
        if (!IsValidBase64(request.EncryptionPublicKey, 8 * 1024)) { error = "La solicitud no contiene una llave de cifrado válida."; return false; }
        return true;
    }

    private static bool IsValidPackage(JetVentaIssuerAuthorizationPackage? package, out string error)
    {
        error = string.Empty;
        if (package is null || package.Version != ProtocolVersion || !string.Equals(package.Product, JetVentaLicensing.Product, StringComparison.Ordinal)) { error = "El archivo no corresponde a JetVenta."; return false; }
        if (!Guid.TryParse(package.RequestId, out _)) { error = "El archivo no tiene un identificador válido."; return false; }
        if (string.IsNullOrWhiteSpace(package.MachineFingerprint) || package.MachineFingerprint.Length > 256) { error = "El archivo no contiene una huella de equipo válida."; return false; }
        if (!IsValidBase64(package.EncryptedIssuerKey, 16 * 1024) || !IsValidBase64(package.Signature, 1024)) { error = "El archivo de autorización está incompleto."; return false; }
        return true;
    }

    private static bool IsValidBase64(string? value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumBytes) return false;
        try { _ = Convert.FromBase64String(value); return true; }
        catch (FormatException) { return false; }
    }
}
