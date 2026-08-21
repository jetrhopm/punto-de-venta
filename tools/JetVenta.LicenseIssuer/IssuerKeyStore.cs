using System.Security.Cryptography;
using System.IO;

namespace JetVenta.LicenseIssuer;

internal static class IssuerKeyStore
{
    private static readonly string KeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetVentaLicencias", "issuer-private-key.bin");
    private static readonly byte[] Entropy = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("JetVenta issuer key v1"));

    public static ECDsa Load()
    {
        if (!File.Exists(KeyPath)) throw new InvalidOperationException("No existe la llave emisora local. Esta herramienta solo funciona en la computadora autorizada para emitir licencias.");
        var encrypted = File.ReadAllBytes(KeyPath);
        var keyBytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(keyBytes, out _);
        return key;
    }
}
