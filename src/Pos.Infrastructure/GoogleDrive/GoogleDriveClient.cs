using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pos.Infrastructure.GoogleDrive;

public sealed class GoogleDriveException(string message, int statusCode, string? reason = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Reason { get; } = reason;
    public bool AuthorizationRevoked => Reason is "invalid_grant" or "revoked";
    public bool StorageFull => Reason is "storageQuotaExceeded" or "quotaExceeded";
}

public sealed record GoogleTokens(string AccessToken, string? RefreshToken, int ExpiresInSeconds, string? Email);
public sealed record DriveFile(string Id, string Name, long Size, string? Md5Checksum, DateTimeOffset CreatedTime, IReadOnlyDictionary<string, string> AppProperties);

// Cliente mínimo de Google OAuth y Drive v3 sobre HttpClient, sin el SDK de Google.
// Sólo usa el permiso drive.file: JetVenta ve únicamente los archivos que ella misma crea.
public sealed class GoogleDriveClient(HttpClient client)
{
    public const string Scopes = "https://www.googleapis.com/auth/drive.file openid email";
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private const string DriveApi = "https://www.googleapis.com/drive/v3/files";
    private const string UploadApi = "https://www.googleapis.com/upload/drive/v3/files";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildAuthorizationUrl(string clientId, string redirectUri, string state, string codeChallenge) =>
        AuthorizationEndpoint + "?" + Query(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        });

    public async Task<GoogleTokens> ExchangeCodeAsync(string clientId, string clientSecret, string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        var result = await TokenRequestAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        }, cancellationToken);
        return new GoogleTokens(result.AccessToken, result.RefreshToken, result.ExpiresIn, EmailFromIdToken(result.IdToken));
    }

    public async Task<GoogleTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken cancellationToken)
    {
        var result = await TokenRequestAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }, cancellationToken);
        return new GoogleTokens(result.AccessToken, result.RefreshToken, result.ExpiresIn, EmailFromIdToken(result.IdToken));
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
        using var response = await client.PostAsync(RevokeEndpoint, content, cancellationToken);
        // 400 significa que el permiso ya no existía; no es un error para el usuario.
    }

    public async Task<string> EnsureFolderAsync(string accessToken, string name, string? parentId, CancellationToken cancellationToken)
    {
        var parent = parentId ?? "root";
        var query = $"mimeType='{FolderMimeType}' and name='{Escape(name)}' and trashed=false and '{Escape(parent)}' in parents";
        var found = await ListAsync(accessToken, query, cancellationToken);
        if (found.Count > 0) return found.OrderBy(item => item.CreatedTime).First().Id;

        using var request = Authorized(HttpMethod.Post, $"{DriveApi}?fields=id", accessToken);
        request.Content = JsonBody(new { name, mimeType = FolderMimeType, parents = new[] { parent } });
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await EnsureSuccessAsync(response, cancellationToken);
        return JsonSerializer.Deserialize<IdResponse>(body, JsonOptions)?.Id ?? throw new GoogleDriveException("Google Drive no devolvió la carpeta creada.", 502);
    }

    public async Task<bool> FolderExistsAsync(string accessToken, string folderId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, $"{DriveApi}/{Uri.EscapeDataString(folderId)}?fields=id,trashed", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        var body = await EnsureSuccessAsync(response, cancellationToken);
        return JsonSerializer.Deserialize<TrashedResponse>(body, JsonOptions)?.Trashed != true;
    }

    // Subida reanudable en dos pasos: se abre la sesión con los metadatos y luego se envía el contenido.
    public async Task<DriveFile> UploadAsync(string accessToken, string folderId, string name, Stream content, long length, IReadOnlyDictionary<string, string> appProperties, CancellationToken cancellationToken)
    {
        using var start = Authorized(HttpMethod.Post, $"{UploadApi}?uploadType=resumable&fields={FileFields}", accessToken);
        start.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
        start.Headers.Add("X-Upload-Content-Length", length.ToString(CultureInfo.InvariantCulture));
        start.Content = JsonBody(new { name, parents = new[] { folderId }, appProperties });
        using var started = await client.SendAsync(start, cancellationToken);
        await EnsureSuccessAsync(started, cancellationToken);
        var session = started.Headers.Location ?? throw new GoogleDriveException("Google Drive no abrió la sesión de subida.", 502);

        using var upload = new HttpRequestMessage(HttpMethod.Put, session) { Content = new StreamContent(content) };
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        upload.Content.Headers.ContentLength = length;
        using var uploaded = await client.SendAsync(upload, cancellationToken);
        var body = await EnsureSuccessAsync(uploaded, cancellationToken);
        var file = ToDriveFile(JsonSerializer.Deserialize<FileResponse>(body, JsonOptions));
        return file.Md5Checksum is null ? await GetFileAsync(accessToken, file.Id, cancellationToken) : file;
    }

    public async Task<DriveFile> GetFileAsync(string accessToken, string fileId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, $"{DriveApi}/{Uri.EscapeDataString(fileId)}?fields={FileFields}", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await EnsureSuccessAsync(response, cancellationToken);
        return ToDriveFile(JsonSerializer.Deserialize<FileResponse>(body, JsonOptions));
    }

    public Task<IReadOnlyList<DriveFile>> ListBackupsAsync(string accessToken, string folderId, string storeId, CancellationToken cancellationToken) =>
        ListAsync(accessToken, $"'{Escape(folderId)}' in parents and trashed=false and appProperties has {{ key='jetventa' and value='backup' }} and appProperties has {{ key='store' and value='{Escape(storeId)}' }}", cancellationToken);

    public async Task DeleteAsync(string accessToken, string fileId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Delete, $"{DriveApi}/{Uri.EscapeDataString(fileId)}", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DownloadAsync(string accessToken, string fileId, Stream destination, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, $"{DriveApi}/{Uri.EscapeDataString(fileId)}?alt=media", accessToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await stream.CopyToAsync(destination, cancellationToken);
    }

    private const string FileFields = "id,name,size,md5Checksum,createdTime,appProperties";

    private async Task<IReadOnlyList<DriveFile>> ListAsync(string accessToken, string query, CancellationToken cancellationToken)
    {
        var files = new List<DriveFile>();
        string? pageToken = null;
        do
        {
            var url = $"{DriveApi}?q={Uri.EscapeDataString(query)}&fields=nextPageToken,files({FileFields})&pageSize=100&spaces=drive"
                + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using var request = Authorized(HttpMethod.Get, url, accessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await EnsureSuccessAsync(response, cancellationToken);
            var page = JsonSerializer.Deserialize<ListResponse>(body, JsonOptions);
            files.AddRange(page?.Files?.Select(ToDriveFile) ?? []);
            pageToken = page?.NextPageToken;
        } while (!string.IsNullOrWhiteSpace(pageToken));
        return files;
    }

    private async Task<TokenResponse> TokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(TokenEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string? error = null;
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("error", out var element) && element.ValueKind == JsonValueKind.String) error = element.GetString();
            }
            catch (JsonException) { }
            throw new GoogleDriveException(error == "invalid_grant"
                ? "Google ya no reconoce el permiso de JetVenta. Vuelve a conectar Google Drive."
                : $"Google rechazó la autorización ({(int)response.StatusCode}).", (int)response.StatusCode, error);
        }
        var result = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
        if (result is null || string.IsNullOrWhiteSpace(result.AccessToken)) throw new GoogleDriveException("Google no entregó el acceso.", 502);
        return result;
    }

    private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return body;
        string? reason = null;
        string? message = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("message", out var text)) message = text.GetString();
                if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0 && errors[0].TryGetProperty("reason", out var item)) reason = item.GetString();
            }
        }
        catch (JsonException) { }
        if (reason is "storageQuotaExceeded" or "quotaExceeded") throw new GoogleDriveException("Tu Google Drive está lleno. Libera espacio para seguir guardando respaldos.", (int)response.StatusCode, reason);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new GoogleDriveException("Google rechazó el acceso de JetVenta. Vuelve a conectar Google Drive.", 401, "revoked");
        throw new GoogleDriveException(message is null ? $"Google Drive respondió HTTP {(int)response.StatusCode}." : $"Google Drive: {message}", (int)response.StatusCode, reason);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static StringContent JsonBody(object value) => new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static string Query(Dictionary<string, string> values) => string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    // El id_token llega directo de Google por HTTPS; sólo se lee el correo para mostrarlo.
    private static string? EmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return null; }
    }

    private static DriveFile ToDriveFile(FileResponse? file)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.Id)) throw new GoogleDriveException("Google Drive devolvió un archivo vacío.", 502);
        _ = long.TryParse(file.Size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
        return new DriveFile(file.Id, file.Name ?? string.Empty, size, file.Md5Checksum, file.CreatedTime ?? DateTimeOffset.MinValue, file.AppProperties ?? new Dictionary<string, string>());
    }

    private sealed record IdResponse(string? Id);
    private sealed record TrashedResponse(string? Id, bool Trashed);
    private sealed record ListResponse(string? NextPageToken, IReadOnlyList<FileResponse>? Files);
    private sealed record FileResponse(string? Id, string? Name, string? Size, string? Md5Checksum, DateTimeOffset? CreatedTime, Dictionary<string, string>? AppProperties);
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("id_token")] string? IdToken);
}

public static class Md5File
{
    public static async Task<string> HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await MD5.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
