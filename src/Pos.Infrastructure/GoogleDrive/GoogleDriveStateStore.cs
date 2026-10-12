using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pos.Infrastructure.GoogleDrive;

public sealed record GoogleDrivePendingUpload(string FileName, DateOnly Day, string Reason, int Attempts, string LastError, DateTimeOffset QueuedAtUtc);
public sealed record GoogleDriveLastUpload(string FileName, long SizeBytes, bool Encrypted, bool Verified, DateTimeOffset UploadedAtUtc);
public sealed record GoogleDriveSessionMarker(DateOnly Day, DateTimeOffset StartedAtUtc);

public sealed record GoogleDriveState
{
    public string RefreshTokenProtected { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTimeOffset? ConnectedAtUtc { get; init; }
    public string RootFolderId { get; init; } = string.Empty;
    public string StoreFolderId { get; init; } = string.Empty;
    public string StoreId { get; init; } = string.Empty;
    public string PasswordProtected { get; init; } = string.Empty;
    public bool NeedsReconnect { get; init; }
    public string LastError { get; init; } = string.Empty;
    public DateTimeOffset? LastErrorAtUtc { get; init; }
    public GoogleDriveLastUpload? LastUpload { get; init; }
    public IReadOnlyList<GoogleDrivePendingUpload> Pending { get; init; } = [];
    public GoogleDriveSessionMarker? OpenSession { get; init; }

    public bool Connected => !string.IsNullOrWhiteSpace(RefreshTokenProtected);
    public bool PasswordEnabled => !string.IsNullOrWhiteSpace(PasswordProtected);
}

// El estado de Google Drive vive sólo en la caja principal, en
// %ProgramData%\PuntoDeVenta\google-drive\state.json. No va en la base de datos para que
// restaurar un respaldo nunca traiga ni borre el permiso de Google de esta computadora.
// El refresh token y la contraseña opcional se cifran con DPAPI (equipo local).
public sealed class GoogleDriveStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public GoogleDriveStateStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "google-drive")) { }

    public GoogleDriveStateStore(string directory)
    {
        Directory = directory;
        _path = Path.Combine(directory, "state.json");
    }

    public string Directory { get; }
    public string TempDirectory => Path.Combine(Directory, "tmp");

    public GoogleDriveState Load()
    {
        lock (_gate) return ReadUnsafe();
    }

    public GoogleDriveState Update(Func<GoogleDriveState, GoogleDriveState> change)
    {
        lock (_gate)
        {
            var updated = change(ReadUnsafe());
            System.IO.Directory.CreateDirectory(Directory);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(updated, JsonOptions));
            File.Move(temp, _path, overwrite: true);
            return updated;
        }
    }

    public (string Token, DateTimeOffset ExpiresAt)? CachedAccessToken
    {
        get { lock (_gate) return _accessToken is null ? null : (_accessToken, _accessTokenExpiresAt); }
    }

    public void CacheAccessToken(string? token, DateTimeOffset expiresAt)
    {
        lock (_gate) { _accessToken = token; _accessTokenExpiresAt = expiresAt; }
    }

    private GoogleDriveState ReadUnsafe()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<GoogleDriveState>(File.ReadAllText(_path), JsonOptions) ?? new GoogleDriveState() : new GoogleDriveState();
        }
        catch (JsonException) { return new GoogleDriveState(); }
    }

    public static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protección de credenciales de JetVenta requiere Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine));
    }

    public static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protección de credenciales de JetVenta requiere Windows.");
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.LocalMachine)); }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new InvalidOperationException("Los datos de Google Drive pertenecen a otra computadora. Vuelve a conectar Google Drive en esta caja principal.", exception);
        }
    }
}
