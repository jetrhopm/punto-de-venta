using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pos.Integrations.MercadoPago;

public sealed record BrokerInstallation(string InstallationId, string InstallationSecret);
public sealed record BrokerOAuthStart(Guid AttemptId, string AuthorizationUrl, DateTimeOffset ExpiresAt);
public sealed record BrokerOAuthPickup(string Status, string? Message, OAuthTokenResult? Tokens);

// Cliente del intermediario OAuth de JetVenta (services/mp-broker).
// El intermediario guarda el Client Secret de Mercado Pago; esta instalación sólo
// firma sus solicitudes con su propio secreto (HMAC-SHA256).
public sealed class JetVentaBrokerClient(HttpClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => client.BaseAddress is not null;

    public async Task<BrokerInstallation> RegisterAsync(string fingerprint, string productVersion, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { fingerprint, productVersion }, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, Resolve("v1/installations/register")) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var response = await SendAsync(request, cancellationToken);
        var content = await ReadAsync(response, cancellationToken);
        var result = JsonSerializer.Deserialize<RegisterResponse>(content, JsonOptions);
        if (result is null || string.IsNullOrWhiteSpace(result.InstallationId) || string.IsNullOrWhiteSpace(result.InstallationSecret))
            throw new MercadoPagoPointException("El intermediario de JetVenta devolvió un registro vacío.", 502);
        return new BrokerInstallation(result.InstallationId, result.InstallationSecret);
    }

    public async Task<BrokerOAuthStart> StartAsync(BrokerInstallation installation, CancellationToken cancellationToken)
    {
        using var response = await SendSignedAsync(installation, "v1/oauth/start", new { }, cancellationToken);
        var content = await ReadAsync(response, cancellationToken);
        var result = JsonSerializer.Deserialize<StartResponse>(content, JsonOptions);
        if (result is null || result.AttemptId == Guid.Empty || string.IsNullOrWhiteSpace(result.AuthorizationUrl) || !result.AuthorizationUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new MercadoPagoPointException("El intermediario de JetVenta devolvió una autorización no válida.", 502);
        return new BrokerOAuthStart(result.AttemptId, result.AuthorizationUrl, result.ExpiresAt);
    }

    public async Task<BrokerOAuthPickup> PickupAsync(BrokerInstallation installation, Guid attemptId, CancellationToken cancellationToken)
    {
        using var response = await SendSignedAsync(installation, "v1/oauth/pickup", new { attemptId }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted) return new BrokerOAuthPickup("pending", null, null);
        if (response.StatusCode == HttpStatusCode.Gone) return new BrokerOAuthPickup("picked_up", "Los datos de esta autorización ya se entregaron.", null);
        var content = await ReadAsync(response, cancellationToken);
        var result = JsonSerializer.Deserialize<TokenResponse>(content, JsonOptions) ?? throw new MercadoPagoPointException("El intermediario de JetVenta devolvió una respuesta vacía.", 502);
        return result.Status == "completed"
            ? new BrokerOAuthPickup("completed", null, ToTokenResult(result))
            : new BrokerOAuthPickup(result.Status ?? "failed", result.Message, null);
    }

    public async Task<OAuthTokenResult> RefreshAsync(BrokerInstallation installation, string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await SendSignedAsync(installation, "v1/oauth/refresh", new { refreshToken }, cancellationToken);
        var content = await ReadAsync(response, cancellationToken);
        var result = JsonSerializer.Deserialize<TokenResponse>(content, JsonOptions) ?? throw new MercadoPagoPointException("El intermediario de JetVenta devolvió una renovación vacía.", 502);
        return ToTokenResult(result);
    }

    private Uri Resolve(string relative)
    {
        var baseAddress = client.BaseAddress ?? throw new InvalidOperationException("Falta configurar MercadoPago:BrokerUrl en la API de JetVenta.");
        return new Uri(baseAddress, relative);
    }

    private async Task<HttpResponseMessage> SendSignedAsync(BrokerInstallation installation, string relative, object payload, CancellationToken cancellationToken)
    {
        var uri = Resolve(relative);
        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(18));
        var signature = Sign(installation.InstallationSecret, "POST", uri.AbsolutePath, timestamp, nonce, body);
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-JV-Installation", installation.InstallationId);
        request.Headers.Add("X-JV-Timestamp", timestamp);
        request.Headers.Add("X-JV-Nonce", nonce);
        request.Headers.Add("X-JV-Signature", signature);
        try { return await SendAsync(request, cancellationToken); }
        finally { request.Dispose(); }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try { return await client.SendAsync(request, cancellationToken); }
        catch (HttpRequestException exception) { throw new MercadoPagoPointException("No se pudo conectar con el intermediario de JetVenta. Revisa la conexión a Internet.", 503, exception.Message); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new MercadoPagoPointException("El intermediario de JetVenta tardó demasiado en responder.", 504, "timeout"); }
    }

    // Texto firmado: METHOD \n PATH \n TIMESTAMP \n NONCE \n SHA256(body) en hex minúsculas.
    public static string Sign(string secret, string method, string path, string timestamp, string nonce, string body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var canonical = $"{method.ToUpperInvariant()}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}";
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode) return content;
        string? message = null;
        string? code = null;
        try
        {
            using var json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String) message = messageElement.GetString();
            if (json.RootElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String) code = codeElement.GetString();
        }
        catch (JsonException) { }
        throw new MercadoPagoPointException(message ?? $"El intermediario de JetVenta respondió HTTP {(int)response.StatusCode}.", (int)response.StatusCode, code);
    }

    private static OAuthTokenResult ToTokenResult(TokenResponse result)
    {
        if (string.IsNullOrWhiteSpace(result.AccessToken)) throw new MercadoPagoPointException("El intermediario de JetVenta no entregó el Access Token.", 502);
        _ = long.TryParse(result.UserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId);
        return new OAuthTokenResult(result.AccessToken, result.RefreshToken, userId, result.ExpiresIn, result.Scope);
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record RegisterResponse(string InstallationId, string InstallationSecret);
    private sealed record StartResponse(Guid AttemptId, string AuthorizationUrl, DateTimeOffset ExpiresAt);
    private sealed record TokenResponse(
        string? Status,
        string? Message,
        string? AccessToken,
        string? RefreshToken,
        string? UserId,
        long ExpiresIn,
        string? Scope);
}
