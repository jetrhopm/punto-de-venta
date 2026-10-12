using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Pos.Infrastructure.GoogleDrive;

public sealed record GoogleDriveStatusResult(
    bool Configured,
    bool IsPrincipal,
    bool CanManage,
    bool Connected,
    string Email,
    bool NeedsReconnect,
    bool PasswordEnabled,
    GoogleDriveLastUpload? LastUpload,
    int PendingCount,
    string LastError,
    string ConnectState,
    string ConnectMessage,
    bool StaleWarning);

public sealed record GoogleDriveCloseResult(bool Uploaded, bool Pending, string Message);
public sealed record GoogleDriveFileResult(string Id, string Name, DateOnly Day, long SizeBytes, bool Encrypted, DateTimeOffset CreatedAtUtc);
public sealed record GoogleDriveRestoreResult(string FileName, long SizeBytes);
public sealed record SetGoogleDrivePasswordCommand(bool Enabled, string? Password);
public sealed record RestoreFromGoogleDriveCommand(string FileId, string? Password);

// Respaldos en el Google Drive del dueño. Sólo opera en la caja principal (la PC de la base):
// cada cierre correcto sube un respaldo; al iniciar sube lo pendiente y, si el uso anterior no
// terminó bien, crea uno de recuperación. Del día en curso se conservan todos; de cada día
// anterior sólo el último, durante 7 días.
public sealed class GoogleDriveBackupService(
    PosDbContext database,
    DatabaseMaintenanceService maintenance,
    GoogleDriveClient drive,
    GoogleDriveStateStore stateStore,
    GoogleDriveAuthorizationFlow authorization,
    IConfiguration configuration)
{
    private const string RootFolderName = "JetVenta Respaldos";
    private const string CloudMarkerSuffix = ".cloud.json";
    private static readonly SemaphoreSlim UploadGate = new(1, 1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    private string ClientId => configuration["GoogleDrive:ClientId"]?.Trim() ?? string.Empty;
    private string ClientSecret => configuration["GoogleDrive:ClientSecret"]?.Trim() ?? string.Empty;
    public bool Configured => ClientId.Length > 0 && ClientSecret.Length > 0;

    // ---------- Estado ----------

    public async Task<GoogleDriveStatusResult?> GetStatusAsync(string token, bool isPrincipal, CancellationToken cancellationToken)
    {
        var user = await SessionUserAsync(token, cancellationToken);
        if (user is null) return null;
        var state = stateStore.Load();
        var connect = authorization.Status;
        var lastSuccess = state.LastUpload?.UploadedAtUtc ?? state.ConnectedAtUtc;
        var stale = state.Connected && lastSuccess is DateTimeOffset at && DateTimeOffset.UtcNow - at > StaleAfter;
        return new GoogleDriveStatusResult(Configured, isPrincipal, user.CanManage, state.Connected, state.Email, state.NeedsReconnect, state.PasswordEnabled,
            state.LastUpload, state.Pending.Count, state.LastError, connect.State.ToString(), connect.Message, stale);
    }

    // ---------- Conectar y desconectar ----------

    public async Task<string?> BeginConnectAsync(string token, bool isPrincipal, CancellationToken cancellationToken)
    {
        var user = await SessionUserAsync(token, cancellationToken);
        if (user is null || !user.CanManage) return null;
        RequirePrincipal(isPrincipal);
        if (!Configured) throw new InvalidOperationException("Google Drive todavía no está configurado en esta versión de JetVenta (falta GoogleDrive:ClientId).");
        var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).Select(item => new { item.Id, item.Name }).FirstAsync(cancellationToken);
        var clientId = ClientId;
        var clientSecret = ClientSecret;
        return authorization.Start(clientId, async (code, verifier, redirectUri) =>
        {
            var tokens = await drive.ExchangeCodeAsync(clientId, clientSecret, code, verifier, redirectUri, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(tokens.RefreshToken)) throw new InvalidOperationException("Google no entregó un permiso permanente. Quita el acceso de JetVenta en tu cuenta de Google y vuelve a conectar.");
            var rootId = await drive.EnsureFolderAsync(tokens.AccessToken, RootFolderName, null, CancellationToken.None);
            var storeFolderId = await drive.EnsureFolderAsync(tokens.AccessToken, FolderName(store.Name), rootId, CancellationToken.None);
            stateStore.CacheAccessToken(tokens.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, tokens.ExpiresInSeconds - 120)));
            stateStore.Update(current => current with
            {
                RefreshTokenProtected = GoogleDriveStateStore.Protect(tokens.RefreshToken),
                Email = tokens.Email ?? string.Empty,
                ConnectedAtUtc = DateTimeOffset.UtcNow,
                RootFolderId = rootId,
                StoreFolderId = storeFolderId,
                StoreId = store.Id.ToString(),
                NeedsReconnect = false,
                LastError = string.Empty,
                LastErrorAtUtc = null
            });
        });
    }

    public async Task<bool?> DisconnectAsync(string token, CancellationToken cancellationToken)
    {
        var user = await SessionUserAsync(token, cancellationToken);
        if (user is null || !user.CanManage) return null;
        var state = stateStore.Load();
        if (state.Connected)
        {
            try { await drive.RevokeAsync(GoogleDriveStateStore.Unprotect(state.RefreshTokenProtected), cancellationToken); }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
        }
        stateStore.CacheAccessToken(null, DateTimeOffset.MinValue);
        stateStore.Update(current => current with
        {
            RefreshTokenProtected = string.Empty, Email = string.Empty, ConnectedAtUtc = null, RootFolderId = string.Empty,
            StoreFolderId = string.Empty, NeedsReconnect = false, LastError = string.Empty, LastErrorAtUtc = null, LastUpload = null
        });
        return true;
    }

    public async Task<bool?> SetPasswordAsync(string token, SetGoogleDrivePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await SessionUserAsync(token, cancellationToken);
        if (user is null || !user.CanManage) return null;
        if (command.Enabled && (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 8))
            throw new ArgumentException("La contraseña de respaldo debe tener al menos 8 caracteres.");
        stateStore.Update(current => current with { PasswordProtected = command.Enabled ? GoogleDriveStateStore.Protect(command.Password!) : string.Empty });
        return true;
    }

    // ---------- Cierre e inicio ----------

    // Se llama al cerrar JetVenta correctamente en la caja principal. Crea el respaldo y lo sube;
    // si no alcanza (sin internet, tiempo agotado o el usuario omitió), queda pendiente.
    public async Task<GoogleDriveCloseResult?> OnCloseAsync(string token, bool isPrincipal, CancellationToken cancellationToken)
    {
        if (await SessionUserAsync(token, CancellationToken.None) is null) return null;
        if (!isPrincipal) return new(false, false, "Los respaldos en la nube se hacen desde la caja principal.");
        stateStore.Update(current => current with { OpenSession = null });
        var state = stateStore.Load();
        if (!state.Connected) return new(false, false, "Google Drive no está conectado.");

        var backup = await maintenance.CreateForCloudAsync(CancellationToken.None);
        var today = Today();
        Enqueue(backup.FileName, today, "Cierre");
        try
        {
            var uploaded = await ProcessPendingAsync(cancellationToken);
            return uploaded
                ? new(true, false, "Respaldo guardado en Google Drive.")
                : new(false, true, string.IsNullOrWhiteSpace(stateStore.Load().LastError) ? "El respaldo se subirá la próxima vez que abras JetVenta." : stateStore.Load().LastError);
        }
        catch (OperationCanceledException)
        {
            return new(false, true, "El respaldo se subirá la próxima vez que abras JetVenta.");
        }
    }

    // Se llama al iniciar sesión en la caja principal. Si el uso anterior no terminó bien
    // (apagón o cierre forzado) crea un respaldo de recuperación. Devuelve si hay trabajo pendiente.
    public async Task<bool?> OnStartupAsync(string token, bool isPrincipal, CancellationToken cancellationToken)
    {
        if (await SessionUserAsync(token, cancellationToken) is null) return null;
        if (!isPrincipal) return false;
        var state = stateStore.Load();
        var previous = state.OpenSession;
        stateStore.Update(current => current with { OpenSession = new GoogleDriveSessionMarker(Today(), DateTimeOffset.UtcNow) });
        if (!state.Connected) return false;
        if (previous is not null)
        {
            var backup = await maintenance.CreateForCloudAsync(cancellationToken);
            Enqueue(backup.FileName, previous.Day, "Recuperación");
        }
        return true;
    }

    public async Task<GoogleDriveCloseResult?> UploadNowAsync(string token, bool isPrincipal, CancellationToken cancellationToken)
    {
        var user = await SessionUserAsync(token, cancellationToken);
        if (user is null || !user.CanManage) return null;
        RequirePrincipal(isPrincipal);
        if (!stateStore.Load().Connected) throw new InvalidOperationException("Conecta Google Drive primero.");
        var backup = await maintenance.CreateForCloudAsync(cancellationToken);
        Enqueue(backup.FileName, Today(), "Manual");
        var uploaded = await ProcessPendingAsync(cancellationToken);
        return uploaded ? new(true, false, "Respaldo guardado en Google Drive.") : new(false, true, stateStore.Load().LastError);
    }

    // Sube todo lo pendiente y aplica la limpieza. Devuelve true si no quedó nada pendiente.
    public async Task<bool> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        await UploadGate.WaitAsync(cancellationToken);
        try
        {
            var state = stateStore.Load();
            if (!state.Connected) return false;
            string accessToken;
            try { accessToken = await AccessTokenAsync(cancellationToken); }
            catch (GoogleDriveException exception) { RecordError(exception.Message, exception.AuthorizationRevoked); return false; }
            catch (HttpRequestException) { RecordError("Sin conexión a internet. El respaldo se subirá más tarde.", false); return false; }

            string folderId;
            try { folderId = await EnsureStoreFolderAsync(accessToken, cancellationToken); }
            catch (GoogleDriveException exception) { RecordError(exception.Message, exception.AuthorizationRevoked); return false; }
            catch (HttpRequestException) { RecordError("Sin conexión a internet. El respaldo se subirá más tarde.", false); return false; }

            foreach (var pending in state.Pending.OrderBy(item => item.QueuedAtUtc).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(DatabaseMaintenanceService.BackupDirectoryPath, pending.FileName);
                if (!File.Exists(path)) { Dequeue(pending.FileName); continue; }
                try
                {
                    var uploaded = await UploadFileAsync(accessToken, folderId, path, pending.Day, cancellationToken);
                    Dequeue(pending.FileName);
                    stateStore.Update(current => current with { LastUpload = uploaded, LastError = string.Empty, LastErrorAtUtc = null, NeedsReconnect = false });
                }
                catch (GoogleDriveException exception)
                {
                    MarkAttempt(pending.FileName, exception.Message);
                    RecordError(exception.Message, exception.AuthorizationRevoked);
                    return false;
                }
                catch (HttpRequestException)
                {
                    MarkAttempt(pending.FileName, "Sin conexión a internet.");
                    RecordError("Sin conexión a internet. El respaldo se subirá más tarde.", false);
                    return false;
                }
            }

            try { await ApplyRetentionAsync(accessToken, folderId, cancellationToken); }
            catch (Exception exception) when (exception is GoogleDriveException or HttpRequestException) { RecordError("Los respaldos se subieron, pero no se pudieron limpiar los anteriores: " + exception.Message, false); }
            CleanLocalCloudCopies();
            return stateStore.Load().Pending.Count == 0;
        }
        finally { UploadGate.Release(); }
    }

    // ---------- Restaurar ----------

    public async Task<IReadOnlyList<GoogleDriveFileResult>?> ListFilesAsync(string token, CancellationToken cancellationToken)
    {
        var user = await SessionUserAsync(token, cancellationToken);
        if (user is null || !user.CanManage) return null;
        var state = stateStore.Load();
        if (!state.Connected) throw new InvalidOperationException("Conecta Google Drive primero.");
        var accessToken = await AccessTokenAsync(cancellationToken);
        var files = await drive.ListBackupsAsync(accessToken, await EnsureStoreFolderAsync(accessToken, cancellationToken), state.StoreId, cancellationToken);
        return files.Select(file => new GoogleDriveFileResult(file.Id, file.Name, DayOf(file), file.Size, BackupCipher.IsEncryptedName(file.Name), file.CreatedTime))
            .OrderByDescending(item => item.CreatedAtUtc).ToArray();
    }

    // Descarga el respaldo, lo descifra si hace falta, verifica su SHA-256 y lo deja en la carpeta
    // local de respaldos para usar el proceso de restauración que ya existe.
    public async Task<GoogleDriveRestoreResult?> DownloadForRestoreAsync(string token, bool isPrincipal, RestoreFromGoogleDriveCommand command, CancellationToken cancellationToken)
    {
        var user = await SessionUserAsync(token, cancellationToken);
        if (user is null || !user.CanManage) return null;
        RequirePrincipal(isPrincipal);
        if (string.IsNullOrWhiteSpace(command.FileId)) throw new ArgumentException("Elige un respaldo de Google Drive.");
        var accessToken = await AccessTokenAsync(cancellationToken);
        var file = await drive.GetFileAsync(accessToken, command.FileId, cancellationToken);
        if (!file.AppProperties.TryGetValue("jetventa", out var kind) || kind != "backup") throw new InvalidOperationException("El archivo elegido no es un respaldo de JetVenta.");

        Directory.CreateDirectory(stateStore.TempDirectory);
        var downloaded = Path.Combine(stateStore.TempDirectory, Guid.NewGuid().ToString("N") + ".download");
        var plain = Path.Combine(stateStore.TempDirectory, Guid.NewGuid().ToString("N") + ".dump");
        try
        {
            await using (var output = File.Create(downloaded)) await drive.DownloadAsync(accessToken, file.Id, output, cancellationToken);
            if (BackupCipher.IsEncryptedName(file.Name))
            {
                var password = string.IsNullOrWhiteSpace(command.Password)
                    ? (stateStore.Load().PasswordEnabled ? GoogleDriveStateStore.Unprotect(stateStore.Load().PasswordProtected) : null)
                    : command.Password;
                if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Este respaldo está protegido. Escribe su contraseña.");
                await using var input = File.OpenRead(downloaded);
                await using var output = File.Create(plain);
                try { await BackupCipher.DecryptAsync(input, output, password, cancellationToken); }
                catch (InvalidDataException exception) { throw new ArgumentException(exception.Message); }
            }
            else File.Move(downloaded, plain, overwrite: true);

            string hash;
            await using (var stream = File.OpenRead(plain)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (file.AppProperties.TryGetValue("sha256", out var expected) && !string.Equals(expected, hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El respaldo descargado no coincide con su verificación. No se restauró nada.");

            var name = file.Name.EndsWith(BackupCipher.EncryptedExtension, StringComparison.OrdinalIgnoreCase) ? file.Name[..^BackupCipher.EncryptedExtension.Length] : file.Name;
            name = Path.GetFileName(name);
            if (!name.EndsWith(".dump", StringComparison.OrdinalIgnoreCase)) name += ".dump";
            var destination = Path.Combine(DatabaseMaintenanceService.BackupDirectoryPath, name);
            if (File.Exists(destination)) destination = Path.Combine(DatabaseMaintenanceService.BackupDirectoryPath, Path.GetFileNameWithoutExtension(name) + "-drive.dump");
            Directory.CreateDirectory(DatabaseMaintenanceService.BackupDirectoryPath);
            File.Move(plain, destination, overwrite: true);
            await File.WriteAllTextAsync(destination + ".sha256", hash, cancellationToken);
            var info = new FileInfo(destination);
            await File.WriteAllTextAsync(destination + ".json", JsonSerializer.Serialize(new BackupResult(info.Name, info.Length, hash, file.CreatedTime)), cancellationToken);
            return new GoogleDriveRestoreResult(info.Name, info.Length);
        }
        finally
        {
            TryDelete(downloaded);
            TryDelete(plain);
        }
    }

    // ---------- Internos ----------

    private async Task<GoogleDriveLastUpload> UploadFileAsync(string accessToken, string folderId, string path, DateOnly day, CancellationToken cancellationToken)
    {
        var state = stateStore.Load();
        var fileName = Path.GetFileName(path);
        string hash;
        await using (var stream = File.OpenRead(path)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        var encrypted = state.PasswordEnabled;
        var source = path;
        string? temp = null;
        try
        {
            if (encrypted)
            {
                Directory.CreateDirectory(stateStore.TempDirectory);
                temp = Path.Combine(stateStore.TempDirectory, Guid.NewGuid().ToString("N") + BackupCipher.EncryptedExtension);
                await using (var input = File.OpenRead(path))
                await using (var output = File.Create(temp))
                    await BackupCipher.EncryptAsync(input, output, GoogleDriveStateStore.Unprotect(state.PasswordProtected), cancellationToken);
                source = temp;
            }

            var length = new FileInfo(source).Length;
            var properties = new Dictionary<string, string>
            {
                ["jetventa"] = "backup",
                ["store"] = state.StoreId,
                ["day"] = day.ToString("yyyy-MM-dd"),
                ["sha256"] = hash,
                ["encrypted"] = encrypted ? "1" : "0",
                ["version"] = typeof(GoogleDriveBackupService).Assembly.GetName().Version?.ToString(3) ?? string.Empty
            };
            DriveFile uploaded;
            await using (var content = File.OpenRead(source))
                uploaded = await drive.UploadAsync(accessToken, folderId, fileName + (encrypted ? BackupCipher.EncryptedExtension : string.Empty), content, length, properties, cancellationToken);
            var localMd5 = await Md5File.HexAsync(source, cancellationToken);
            var verified = string.Equals(uploaded.Md5Checksum, localMd5, StringComparison.OrdinalIgnoreCase) && uploaded.Size == length;
            if (!verified)
            {
                await drive.DeleteAsync(accessToken, uploaded.Id, CancellationToken.None);
                throw new GoogleDriveException("El respaldo subido no coincide con el original. Se volverá a intentar.", 502, "verification");
            }
            await File.WriteAllTextAsync(path + CloudMarkerSuffix, JsonSerializer.Serialize(new { day = day.ToString("yyyy-MM-dd"), driveId = uploaded.Id }), cancellationToken);
            return new GoogleDriveLastUpload(fileName, length, encrypted, true, DateTimeOffset.UtcNow);
        }
        finally { if (temp is not null) TryDelete(temp); }
    }

    private async Task ApplyRetentionAsync(string accessToken, string folderId, CancellationToken cancellationToken)
    {
        var state = stateStore.Load();
        var files = await drive.ListBackupsAsync(accessToken, folderId, state.StoreId, cancellationToken);
        var entries = files.Select(file => new BackupRetentionEntry(file.Id, DayOf(file), file.CreatedTime));
        foreach (var id in GoogleDriveRetention.SelectForDeletion(entries, Today()))
            await drive.DeleteAsync(accessToken, id, cancellationToken);
    }

    // Las copias locales creadas para la nube siguen la misma regla, para no llenar el disco.
    // Sólo se tocan archivos con marca .cloud.json que ya se subieron; el respaldo diario local no.
    private void CleanLocalCloudCopies()
    {
        var directory = DatabaseMaintenanceService.BackupDirectoryPath;
        if (!Directory.Exists(directory)) return;
        var pending = stateStore.Load().Pending.Select(item => item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = new List<BackupRetentionEntry>();
        foreach (var marker in Directory.GetFiles(directory, "*" + CloudMarkerSuffix))
        {
            var dump = marker[..^CloudMarkerSuffix.Length];
            if (!File.Exists(dump) || pending.Contains(Path.GetFileName(dump))) continue;
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(marker));
                if (json.RootElement.TryGetProperty("day", out var dayElement) && DateOnly.TryParse(dayElement.GetString(), out var day))
                    entries.Add(new BackupRetentionEntry(dump, day, File.GetCreationTimeUtc(dump)));
            }
            catch (Exception exception) when (exception is JsonException or IOException) { }
        }
        foreach (var dump in GoogleDriveRetention.SelectForDeletion(entries, Today()))
            foreach (var path in new[] { dump, dump + ".sha256", dump + ".json", dump + CloudMarkerSuffix }) TryDelete(path);
    }

    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        var cached = stateStore.CachedAccessToken;
        if (cached is { } value && value.ExpiresAt > DateTimeOffset.UtcNow) return value.Token;
        var state = stateStore.Load();
        if (!state.Connected) throw new InvalidOperationException("Conecta Google Drive primero.");
        if (!Configured) throw new InvalidOperationException("Google Drive no está configurado en esta versión de JetVenta.");
        var tokens = await drive.RefreshAsync(ClientId, ClientSecret, GoogleDriveStateStore.Unprotect(state.RefreshTokenProtected), cancellationToken);
        stateStore.CacheAccessToken(tokens.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, tokens.ExpiresInSeconds - 120)));
        if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
            stateStore.Update(current => current with { RefreshTokenProtected = GoogleDriveStateStore.Protect(tokens.RefreshToken) });
        return tokens.AccessToken;
    }

    private async Task<string> EnsureStoreFolderAsync(string accessToken, CancellationToken cancellationToken)
    {
        var state = stateStore.Load();
        if (!string.IsNullOrWhiteSpace(state.StoreFolderId) && await drive.FolderExistsAsync(accessToken, state.StoreFolderId, cancellationToken)) return state.StoreFolderId;
        // La carpeta se borró o movió a la papelera: se crea de nuevo.
        var store = await database.Stores.AsNoTracking().OrderBy(item => item.CreatedAtUtc).Select(item => new { item.Id, item.Name }).FirstAsync(cancellationToken);
        var rootId = await drive.EnsureFolderAsync(accessToken, RootFolderName, null, cancellationToken);
        var folderId = await drive.EnsureFolderAsync(accessToken, FolderName(store.Name), rootId, cancellationToken);
        stateStore.Update(current => current with { RootFolderId = rootId, StoreFolderId = folderId, StoreId = string.IsNullOrWhiteSpace(current.StoreId) ? store.Id.ToString() : current.StoreId });
        return folderId;
    }

    private void Enqueue(string fileName, DateOnly day, string reason) =>
        stateStore.Update(current => current with
        {
            Pending = current.Pending.Where(item => !item.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .Append(new GoogleDrivePendingUpload(fileName, day, reason, 0, string.Empty, DateTimeOffset.UtcNow)).ToArray()
        });

    private void Dequeue(string fileName) =>
        stateStore.Update(current => current with { Pending = current.Pending.Where(item => !item.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)).ToArray() });

    private void MarkAttempt(string fileName, string error) =>
        stateStore.Update(current => current with
        {
            Pending = current.Pending.Select(item => item.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) ? item with { Attempts = item.Attempts + 1, LastError = Truncate(error) } : item).ToArray()
        });

    private void RecordError(string message, bool revoked)
    {
        if (revoked) stateStore.CacheAccessToken(null, DateTimeOffset.MinValue);
        stateStore.Update(current => current with { LastError = Truncate(message), LastErrorAtUtc = DateTimeOffset.UtcNow, NeedsReconnect = current.NeedsReconnect || revoked });
    }

    private static DateOnly DayOf(DriveFile file) =>
        file.AppProperties.TryGetValue("day", out var value) && DateOnly.TryParse(value, out var day) ? day : DateOnly.FromDateTime(file.CreatedTime.LocalDateTime);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private static string FolderName(string storeName)
    {
        var name = string.Concat((storeName ?? string.Empty).Where(character => !char.IsControl(character) && character is not ('/' or '\\'))).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Mi tienda" : name.Length > 80 ? name[..80] : name;
    }

    private static void RequirePrincipal(bool isPrincipal)
    {
        if (!isPrincipal) throw new InvalidOperationException("Google Drive sólo se administra desde la caja principal (la computadora donde está la base de datos).");
    }

    private static string Truncate(string value) => value.Length > 300 ? value[..300] : value;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record SessionUser(Guid Id, bool CanManage);

    private async Task<SessionUser?> SessionUserAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session is null) return null;
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
        var canManage = user.IsAdministrator || await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ImportOrExportData", cancellationToken);
        return new SessionUser(user.Id, canManage);
    }
}
