using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pos.Integrations.MercadoPago;

public sealed record MercadoPagoTerminal(string Id, string PosId, string StoreId, string ExternalPosId, string OperatingMode);
public sealed record MercadoPagoOrder(string Id, string Status, string StatusDetail, string? PaymentId, decimal Amount);

public sealed class MercadoPagoPointException(string message, int statusCode, string? providerCode = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? ProviderCode { get; } = providerCode;
}

public sealed class MercadoPagoPointClient(HttpClient client)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<MercadoPagoTerminal>> ListTerminalsAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "terminals/v1/list?limit=50&offset=0", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        var result = JsonSerializer.Deserialize<TerminalListResponse>(body, JsonOptions);
        return result?.Data?.Terminals?.Select(item => new MercadoPagoTerminal(
            JsonValue(item.Id),
            JsonValue(item.PosId),
            JsonValue(item.StoreId),
            JsonValue(item.ExternalPosId),
            string.IsNullOrWhiteSpace(JsonValue(item.OperatingMode)) ? "UNDEFINED" : JsonValue(item.OperatingMode))).ToArray() ?? [];
    }

    public async Task<MercadoPagoOrder> CreateOrderAsync(string accessToken, string terminalId, Guid operationId, decimal amount, string description, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "v1/orders", accessToken);
        request.Headers.Add("X-Idempotency-Key", operationId.ToString());
        request.Content = JsonContent.Create(new
        {
            type = "point",
            external_reference = $"jetventa_{operationId:N}",
            expiration_time = "PT10M",
            transactions = new { payments = new[] { new { amount = decimal.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture) } } },
            config = new
            {
                point = new { terminal_id = terminalId, print_on_terminal = "no_ticket" },
                payment_method = new { default_type = "credit_card" }
            },
            description = string.IsNullOrWhiteSpace(description) ? "Venta JetVenta" : description[..Math.Min(description.Length, 150)]
        }, options: JsonOptions);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        return ParseOrder(body);
    }

    public async Task<MercadoPagoOrder> GetOrderAsync(string accessToken, string orderId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"v1/orders/{Uri.EscapeDataString(orderId)}", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        return ParseOrder(body);
    }

    public async Task CancelOrderAsync(string accessToken, string orderId, Guid cancellationId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"v1/orders/{Uri.EscapeDataString(orderId)}/cancel", accessToken);
        request.Headers.Add("X-Idempotency-Key", cancellationId.ToString());
        request.Content = JsonContent.Create(new { }, options: JsonOptions);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
    }

    public async Task<OAuthTokenResult> ExchangeAuthorizationCodeAsync(string clientId, string clientSecret, string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "oauth/token")
        {
            Content = JsonContent.Create(new
            {
                client_id = clientId,
                client_secret = clientSecret,
                grant_type = "authorization_code",
                code,
                redirect_uri = redirectUri,
                code_verifier = codeVerifier
            }, options: JsonOptions)
        };
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<OAuthTokenResult>(body, JsonOptions) ?? throw new MercadoPagoPointException("Mercado Pago devolvió una autorización vacía.", 502);
    }

    public async Task<OAuthTokenResult> RefreshAccessTokenAsync(string clientId, string clientSecret, string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "oauth/token")
        {
            Content = JsonContent.Create(new
            {
                client_id = clientId,
                client_secret = clientSecret,
                grant_type = "refresh_token",
                refresh_token = refreshToken
            }, options: JsonOptions)
        };
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<OAuthTokenResult>(body, JsonOptions) ?? throw new MercadoPagoPointException("Mercado Pago devolvió una renovación vacía.", 502);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static MercadoPagoOrder ParseOrder(string body)
    {
        var result = JsonSerializer.Deserialize<OrderResponse>(body, JsonOptions) ?? throw new MercadoPagoPointException("Mercado Pago devolvió una order vacía.", 502);
        var payment = result.Transactions?.Payments?.FirstOrDefault();
        _ = decimal.TryParse(payment?.PaidAmount ?? payment?.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount);
        return new MercadoPagoOrder(result.Id ?? string.Empty, result.Status ?? "unknown", result.StatusDetail ?? string.Empty, payment?.Id, amount);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) => await response.Content.ReadAsStringAsync(cancellationToken);

    private static string JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.ToString()
    };

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;
        string? code = null;
        string? message = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("code", out var codeElement)) code = codeElement.ToString();
            if (root.TryGetProperty("message", out var messageElement)) message = messageElement.ToString();
            if (string.IsNullOrWhiteSpace(message) && root.TryGetProperty("error", out var errorElement)) message = errorElement.ToString();
        }
        catch (JsonException) { }
        throw new MercadoPagoPointException(message ?? $"Mercado Pago respondió con HTTP {(int)response.StatusCode}.", (int)response.StatusCode, code);
    }

    private sealed record TerminalListResponse(TerminalData? Data);
    private sealed record TerminalData(IReadOnlyList<TerminalResponse>? Terminals);
    // Mercado Pago puede devolver identificadores como texto o como número según la cuenta.
    private sealed record TerminalResponse(JsonElement Id, [property: JsonPropertyName("pos_id")] JsonElement PosId, [property: JsonPropertyName("store_id")] JsonElement StoreId, [property: JsonPropertyName("external_pos_id")] JsonElement ExternalPosId, [property: JsonPropertyName("operating_mode")] JsonElement OperatingMode);
    private sealed record OrderResponse(string? Id, string? Status, [property: JsonPropertyName("status_detail")] string? StatusDetail, OrderTransactions? Transactions);
    private sealed record OrderTransactions(IReadOnlyList<OrderPayment>? Payments);
    private sealed record OrderPayment(string? Id, string? Amount, [property: JsonPropertyName("paid_amount")] string? PaidAmount);
}

public sealed record OAuthTokenResult(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("user_id")] long UserId,
    [property: JsonPropertyName("expires_in")] long ExpiresIn,
    string? Scope);
