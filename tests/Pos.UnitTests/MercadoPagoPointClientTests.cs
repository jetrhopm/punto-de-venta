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
        Assert.DoesNotContain("default_type", handler.Body);
        Assert.Equal("ORD-123", result.Id);
        Assert.Equal(125.5m, result.Amount);
    }

    [Fact]
    public async Task ParsesPaidAmountSeparatelyFromRequestedAmount()
    {
        var handler = new RecordingHandler("""
            {"id":"ORD-124","status":"processed","status_detail":"accredited","transactions":{"payments":[{"id":"PAY-10","amount":"125.50","paid_amount":"120.00"}]}}
            """);
        var client = CreateClient(handler);

        var result = await client.GetOrderAsync("TEST-token", "ORD-124", CancellationToken.None);

        Assert.Equal(125.5m, result.Amount);
        Assert.Equal(120m, result.PaidAmount);
    }

    [Fact]
    public async Task PartialRefundUsesPaymentTransactionAndIdempotency()
    {
        var refundId = Guid.Parse("22d10896-026c-47f0-a2b4-d75e7dcd913d");
        var handler = new RecordingHandler("""
            {"id":"ORD-125","status":"processed","status_detail":"partially_refunded","transactions":{"payments":[{"id":"PAY-11","amount":"125.50","paid_amount":"125.50"}],"refunds":[{"id":"REF-1","transaction_id":"PAY-11","amount":"25.50","status":"processed"}]}}
            """);
        var client = CreateClient(handler);

        var result = await client.RefundOrderAsync("TEST-token", "ORD-125", "PAY-11", 25.5m, 125.5m, refundId, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.mercadopago.com/v1/orders/ORD-125/refund", handler.Request.RequestUri!.ToString());
        Assert.Equal(refundId.ToString(), handler.Request.Headers.GetValues("X-Idempotency-Key").Single());
        Assert.Contains("\"id\":\"PAY-11\"", handler.Body);
        Assert.Contains("\"amount\":\"25.50\"", handler.Body);
        var refund = Assert.Single(result.Refunds);
        Assert.Equal("REF-1", refund.Id);
        Assert.Equal(25.5m, refund.Amount);
    }

    [Fact]
    public async Task TotalRefundSendsAnEmptyBody()
    {
        var handler = new RecordingHandler("""
            {"id":"ORD-126","status":"refunded","status_detail":"refunded","transactions":{"refunds":[{"id":"REF-2","transaction_id":"PAY-12","amount":"50.00","status":"processing"}]}}
            """);
        var client = CreateClient(handler);

        await client.RefundOrderAsync("TEST-token", "ORD-126", "PAY-12", 50m, 50m, Guid.NewGuid(), CancellationToken.None);

        Assert.True(string.IsNullOrEmpty(handler.Body));
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
    public async Task ListTerminalsAcceptsNumericIdentifiers()
    {
        var handler = new RecordingHandler("""
            {"data":{"terminals":[{"id":"TERM-2","pos_id":23545678,"store_id":12354567,"external_pos_id":"CAJA-2","operating_mode":"PDV"}]}}
            """);
        var client = CreateClient(handler);

        var terminal = Assert.Single(await client.ListTerminalsAsync("TEST-token", CancellationToken.None));

        Assert.Equal("23545678", terminal.PosId);
        Assert.Equal("12354567", terminal.StoreId);
        Assert.Equal("PDV", terminal.OperatingMode);
    }

    [Fact]
    public async Task ActivatePdvUsesSetupEndpoint()
    {
        var handler = new RecordingHandler("""{"terminals":[{"id":"TERM-1","operating_mode":"PDV"}]}""");
        var client = CreateClient(handler);

        await client.ActivatePdvAsync("TEST-token", "TERM-1", CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.Request!.Method);
        Assert.Equal("https://api.mercadopago.com/terminals/v1/setup", handler.Request.RequestUri!.ToString());
        Assert.Contains("\"operating_mode\":\"PDV\"", handler.Body);
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
