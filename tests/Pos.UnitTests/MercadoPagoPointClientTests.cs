using System.Net;
using System.Text;
using Pos.Integrations.MercadoPago;

namespace Pos.UnitTests;

public sealed class MercadoPagoPointClientTests
{
    [Fact]
    public async Task CreateOrderUsesOrdersApiAndIdempotency()
    {
        var operationId = Guid.Parse("1f2926bf-9643-47cb-a7fe-6b6e0db28555");
        var handler = new RecordingHandler("""
            {"id":"ORD-123","status":"created","status_detail":"pending","transactions":{"payments":[{"id":"PAY-9","amount":"125.50"}]}}
            """);
        var client = CreateClient(handler);

        var result = await client.CreateOrderAsync("TEST-token", "PAX_A910__123", operationId, 125.5m, "Venta 42", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.mercadopago.com/v1/orders", handler.Request.RequestUri!.ToString());
        Assert.Equal(operationId.ToString(), handler.Request.Headers.GetValues("X-Idempotency-Key").Single());
        Assert.Contains("\"amount\":\"125.50\"", handler.Body);
        Assert.Contains("\"terminal_id\":\"PAX_A910__123\"", handler.Body);
        Assert.Contains("\"print_on_terminal\":\"no_ticket\"", handler.Body);
        Assert.Equal("ORD-123", result.Id);
        Assert.Equal(125.5m, result.Amount);
    }

    [Fact]
    public async Task ListTerminalsReturnsOperatingMode()
    {
        var handler = new RecordingHandler("""
            {"data":{"terminals":[{"id":"TERM-1","pos_id":"POS-1","store_id":"STORE-1","external_pos_id":"CAJA-1","operating_mode":"PDV"}]}}
            """);
        var client = CreateClient(handler);

        var terminals = await client.ListTerminalsAsync("TEST-token", CancellationToken.None);

        var terminal = Assert.Single(terminals);
        Assert.Equal("TERM-1", terminal.Id);
        Assert.Equal("PDV", terminal.OperatingMode);
        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task RefreshTokenUsesOAuthGrant()
    {
        var handler = new RecordingHandler("""
            {"access_token":"APP_USR-new","refresh_token":"refresh-new","user_id":123,"expires_in":15552000,"scope":"offline_access"}
            """);
        var client = CreateClient(handler);

        var result = await client.RefreshAccessTokenAsync("app-id", "secret", "refresh-old", CancellationToken.None);

        Assert.Equal("https://api.mercadopago.com/oauth/token", handler.Request!.RequestUri!.ToString());
        Assert.Contains("\"grant_type\":\"refresh_token\"", handler.Body);
        Assert.Contains("\"refresh_token\":\"refresh-old\"", handler.Body);
        Assert.Equal("APP_USR-new", result.AccessToken);
    }

    private static MercadoPagoPointClient CreateClient(RecordingHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com/") });

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
