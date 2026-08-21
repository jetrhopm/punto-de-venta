using Microsoft.Win32;
using Pos.Domain;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JetVenta.LicenseIssuer;

internal sealed record IssuerRecoveryPackage(
    int Version,
    string Product,
    int Iterations,
    string Salt,
    string Nonce,
    string Tag,
    string Ciphertext);

internal static class IssuerKeyStore
{
    private const int RecoveryIterations = 600_000;
    private const int EncryptionKeySize = 32;
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    private const int MaximumRecoveryDocumentBytes = 64 * 1024;

    private static readonly string IssuerDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetVentaLicencias");
    private static readonly string KeyPath = Path.Combine(IssuerDirectory, "issuer-private-key.bin");
    private static readonly string EnrollmentKeyPath = Path.Combine(IssuerDirectory, "issuer-enrollment-private-key.bin");
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("JetVenta issuer key v1"));
    private static readonly byte[] EnrollmentEntropy = SHA256.HashData(Encoding.UTF8.GetBytes("JetVenta issuer enrollment key v1"));
    private static readonly byte[] RecoveryAssociatedData = Encoding.UTF8.GetBytes("JetVenta-Emisor-Recuperacion-v1");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static bool HasAuthorizedIssuer
    {
        get
        {
            try
            {
                using var _ = Load();
                return true;
            }
            catch { return false; }
        }
    }

    public static ECDsa Load()
    {
        if (!File.Exists(KeyPath)) throw new InvalidOperationException("No existe la llave emisora local. Crea una solicitud de autorización o importa un respaldo de recuperación.");
        var encrypted = File.ReadAllBytes(KeyPath);
        var keyBytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return ImportAndValidateIssuerKey(keyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    public static string CreateEnrollmentRequest()
    {
        Directory.CreateDirectory(IssuerDirectory);
        using var rsa = RSA.Create(3072);
        var privateBytes = rsa.ExportPkcs8PrivateKey();
        try
        {
            WriteProtected(EnrollmentKeyPath, privateBytes, EnrollmentEntropy);
            var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            return JetVentaIssuerAuthorization.CreateEnrollmentRequestCode(GetMachineFingerprint(), publicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
        }
    }

    public static string CreateAuthorizationDocument(string enrollmentRequestCode)
    {
        if (!JetVentaIssuerAuthorization.TryReadEnrollmentRequest(enrollmentRequestCode, out var request, out var error)) throw new InvalidOperationException(error);

        using var issuer = Load();
        using var recipient = RSA.Create();
        recipient.ImportSubjectPublicKeyInfo(Convert.FromBase64String(request!.EncryptionPublicKey), out _);
        var issuerBytes = issuer.ExportPkcs8PrivateKey();
        try
        {
            var encryptedIssuerKey = recipient.Encrypt(issuerBytes, RSAEncryptionPadding.OaepSHA256);
            try
            {
                return JetVentaIssuerAuthorization.CreateAuthorizationDocument(request, encryptedIssuerKey, issuer);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encryptedIssuerKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(issuerBytes);
        }
    }

    public static void ImportAuthorizationDocument(string content)
    {
        if (!JetVentaIssuerAuthorization.TryReadAndVerifyAuthorization(content, out var package, out var error)) throw new InvalidOperationException(error);
        if (!string.Equals(package!.MachineFingerprint, GetMachineFingerprint(), StringComparison.Ordinal)) throw new InvalidOperationException("Esta autorización fue creada para otra computadora.");
        if (!File.Exists(EnrollmentKeyPath)) throw new InvalidOperationException("Primero crea una solicitud de autorización en esta computadora.");

        var protectedEnrollmentKey = File.ReadAllBytes(EnrollmentKeyPath);
        var enrollmentKeyBytes = ProtectedData.Unprotect(protectedEnrollmentKey, EnrollmentEntropy, DataProtectionScope.CurrentUser);
        try
        {
            using var recipient = RSA.Create();
            recipient.ImportPkcs8PrivateKey(enrollmentKeyBytes, out _);
            var encryptedIssuerKey = Convert.FromBase64String(package.EncryptedIssuerKey);
            try
            {
                var issuerKeyBytes = recipient.Decrypt(encryptedIssuerKey, RSAEncryptionPadding.OaepSHA256);
                try
                {
                    SaveIssuerKey(issuerKeyBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(issuerKeyBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encryptedIssuerKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedEnrollmentKey);
            CryptographicOperations.ZeroMemory(enrollmentKeyBytes);
        }

        File.Delete(EnrollmentKeyPath);
    }

    public static string CreateRecoveryDocument(string password)
    {
        EnsureRecoveryPassword(password);
        using var issuer = Load();
        var issuerKeyBytes = issuer.ExportPkcs8PrivateKey();
        var salt = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var derivedKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, RecoveryIterations, HashAlgorithmName.SHA512, EncryptionKeySize);
        var ciphertext = new byte[issuerKeyBytes.Length];
        var tag = new byte[GcmTagSize];
        try
        {
            using var aes = new AesGcm(derivedKey, GcmTagSize);
            aes.Encrypt(nonce, issuerKeyBytes, ciphertext, tag, RecoveryAssociatedData);
            var package = new IssuerRecoveryPackage(1, JetVentaLicensing.Product, RecoveryIterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
            return JsonSerializer.Serialize(package, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(issuerKeyBytes);
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    public static void ImportRecoveryDocument(string content, string password)
    {
        EnsureRecoveryPassword(password);
        if (string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > MaximumRecoveryDocumentBytes) throw new InvalidOperationException("El respaldo de recuperación está vacío o es demasiado grande.");

        IssuerRecoveryPackage? package;
        try { package = JsonSerializer.Deserialize<IssuerRecoveryPackage>(content, JsonOptions); }
        catch (JsonException) { throw new InvalidOperationException("El respaldo de recuperación no tiene un formato válido."); }

        if (package is null || package.Version != 1 || !string.Equals(package.Product, JetVentaLicensing.Product, StringComparison.Ordinal) || package.Iterations < 300_000 || package.Iterations > 2_000_000)
            throw new InvalidOperationException("El respaldo de recuperación no corresponde a JetVenta.");

        byte[] salt;
        byte[] nonce;
        byte[] tag;
        byte[] ciphertext;
        try
        {
            salt = Convert.FromBase64String(package.Salt);
            nonce = Convert.FromBase64String(package.Nonce);
            tag = Convert.FromBase64String(package.Tag);
            ciphertext = Convert.FromBase64String(package.Ciphertext);
        }
        catch (FormatException) { throw new InvalidOperationException("El respaldo de recuperación está incompleto."); }

        if (salt.Length != 32 || nonce.Length != GcmNonceSize || tag.Length != GcmTagSize || ciphertext.Length is < 64 or > 16 * 1024)
            throw new InvalidOperationException("El respaldo de recuperación tiene parámetros inválidos.");

        var derivedKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, package.Iterations, HashAlgorithmName.SHA512, EncryptionKeySize);
        var issuerKeyBytes = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(derivedKey, GcmTagSize);
            aes.Decrypt(nonce, ciphertext, tag, issuerKeyBytes, RecoveryAssociatedData);
            SaveIssuerKey(issuerKeyBytes);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("La contraseña no coincide o el respaldo fue alterado.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(issuerKeyBytes);
        }
    }

    private static void SaveIssuerKey(byte[] keyBytes)
    {
        using var _ = ImportAndValidateIssuerKey(keyBytes);
        Directory.CreateDirectory(IssuerDirectory);
        WriteProtected(KeyPath, keyBytes, Entropy);
    }

    private static ECDsa ImportAndValidateIssuerKey(byte[] keyBytes)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(keyBytes, out _);
            var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(publicKey), Encoding.ASCII.GetBytes(JetVentaLicensing.IssuerPublicKeyBase64)))
                throw new InvalidOperationException("La llave no corresponde a la llave pública incluida en JetVenta.");
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private static void WriteProtected(string destination, byte[] value, byte[] entropy)
    {
        var protectedBytes = ProtectedData.Protect(value, entropy, DataProtectionScope.CurrentUser);
        var temporary = destination + ".new";
        try
        {
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, destination, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void EnsureRecoveryPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 16) throw new InvalidOperationException("Usa una contraseña de recuperación de al menos 16 caracteres.");
    }

    private static string GetMachineFingerprint()
    {
        var machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", string.Empty)?.ToString() ?? string.Empty;
        var source = $"{machineGuid}|{Environment.MachineName}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}
