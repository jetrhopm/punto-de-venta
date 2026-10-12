using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Pos.Domain;

namespace Pos.Setup;

internal sealed record InstalledLicenseInfo(bool IsActive, bool IncludesMobile, string? StoreName);

internal static class LicenseSetupStore
{
    private static readonly byte[] StorageEntropy = SHA256.HashData(Encoding.UTF8.GetBytes("JetVenta license storage v1"));
    private static readonly byte[] LicenseClockEntropy = SHA256.HashData(Encoding.UTF8.GetBytes("JetVenta license clock storage v1"));
    private static readonly string LicenseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "license");
    private static readonly string LicensePath = Path.Combine(LicenseDirectory, "licencia.jv.dpapi");
    private static readonly string LicenseClockPath = Path.Combine(LicenseDirectory, "licencia-reloj.jv.dpapi");

    public static InstalledLicenseInfo ReadInstalled()
    {
        try
        {
            if (!File.Exists(LicensePath)) return new(false, false, null);
            var protectedBytes = File.ReadAllBytes(LicensePath);
            var bytes = ProtectedData.Unprotect(protectedBytes, StorageEntropy, DataProtectionScope.LocalMachine);
            if (!JetVentaLicensing.TryReadLicense(Encoding.UTF8.GetString(bytes), out var signed, out _) ||
                signed is null || !JetVentaLicensing.Verify(signed) ||
                !string.Equals(signed.License.MachineFingerprint, GetMachineFingerprint(), StringComparison.Ordinal) ||
                signed.License.ExpiresAtUtc is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            {
                return new(false, false, null);
            }

            return new(true, signed.License.HasMobileEntitlement, signed.License.StoreName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return new(false, false, null);
        }
    }

    public static string Validate(string content, bool requireMobile)
    {
        if (!JetVentaLicensing.TryReadLicense(content, out var signed, out var error) || signed is null)
            throw new InvalidOperationException(error);
        if (!JetVentaLicensing.Verify(signed)) throw new InvalidOperationException("La firma de la licencia no es válida.");
        if (!string.Equals(signed.License.MachineFingerprint, GetMachineFingerprint(), StringComparison.Ordinal))
            throw new InvalidOperationException("La licencia seleccionada fue emitida para otro equipo.");
        if (signed.License.ExpiresAtUtc is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("La licencia seleccionada ya venció.");
        if (requireMobile && !signed.License.HasMobileEntitlement)
            throw new InvalidOperationException("Esta instalación ya tiene JetVenta Desktop. Selecciona una licencia Desktop + Móvil para actualizarla.");
        return JetVentaLicensing.Serialize(signed);
    }

    public static string GetEdition(string content)
    {
        if (!JetVentaLicensing.TryReadLicense(content, out var signed, out var error) || signed is null)
            throw new InvalidOperationException(error);
        return signed.License.HasMobileEntitlement ? "Desktop + Móvil" : "Desktop";
    }

    public static void Save(string content)
    {
        var serialized = Validate(content, requireMobile: false);
        if (!JetVentaLicensing.TryReadLicense(serialized, out var signed, out var error) || signed is null)
            throw new InvalidOperationException(error);

        Directory.CreateDirectory(LicenseDirectory);
        var bytes = Encoding.UTF8.GetBytes(serialized);
        var protectedBytes = ProtectedData.Protect(bytes, StorageEntropy, DataProtectionScope.LocalMachine);
        WriteAtomically(LicensePath, protectedBytes);

        if (signed.License.ExpiresAtUtc is not null)
        {
            var clock = new LicenseClockState(1, signed.License.LicenseId, signed.License.MachineFingerprint, DateTimeOffset.UtcNow);
            var clockBytes = JsonSerializer.SerializeToUtf8Bytes(clock);
            var protectedClock = ProtectedData.Protect(clockBytes, LicenseClockEntropy, DataProtectionScope.LocalMachine);
            WriteAtomically(LicenseClockPath, protectedClock);
        }
        else
        {
            try { File.Delete(LicenseClockPath); } catch (IOException) { }
        }
    }

    private static void WriteAtomically(string path, byte[] contents)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, contents);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
        }
    }

    private static string GetMachineFingerprint()
    {
        var machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", string.Empty)?.ToString() ?? string.Empty;
        var material = $"{machineGuid}|{Environment.MachineName}|{GetSystemVolumeSerial()}";
        return "JV1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..24];
    }

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

    private sealed record LicenseClockState(int Version, string LicenseId, string MachineFingerprint, DateTimeOffset LastSeenAtUtc);
}
