using System.Net;
using System.Security.Cryptography;
using System.Text;
using Pos.Integrations.MercadoPago;

namespace Pos.UnitTests;

public sealed class JetVentaBrokerClientTests
{
    private static readonly BrokerInstallation Installation = new("7c0b5a8e-2f41-4d8e-9a57-3a5f1f0d9c11", "test-secret");

    // Vectores calculados con services/mp-broker (Node.js) y verificados con Python.
    [Theory]
    [InlineData("test-secret", "/v1/oauth/start", "1790000000", "nonce-1234567890abcd", "{}", "86f8da61d068f306c619600ff3789266c068b77ebc478de01f2006a4bf675e1b")]
    [InlineData("s3crét", "/v1/oauth/refresh", "1790000123", "AbCdEfGhIjKlMnOpQrSt", "{\"refreshToken\":\"TG-ñ\"}", "88d45f24f63d483fa16cefb10ce21da6fcbc0d103ca44c1097efe0ea6d7ac676")]
    public void SignatureMatchesBroker(string secret, string path, string timestamp, string nonce, string body, string expected)
    {
        Assert.Equal(expected, JetVentaBrokerClient.Sign(secret, "POST", path, timestamp, nonce, body));
    }

    [Fact]
    public async Task SignedRequestCarriesVerifiableHeaders()
    {
        var handler = new BrokerHandler(HttpStatusCode.Accepted, "{\"status\":\"pending\"}");
        var client = CreateClient(handler);

        var result = await client.PickupAsync(Installation, Guid.Parse("b5a7c3f1-0d2e-4f6a-8b9c-1d2e3f4a5b6c"), CancellationToken.None);

        Assert.Equal("pending", result.Status);
        var request = handler.Request!;
        Assert.Equal("https://mp.example.com/v1/oauth/pickup", request.RequestUri!.ToString());
        var timestamp = request.Headers.GetValues("X-JV-Timestamp").Single();
        var nonce = request.Headers.GetValues("X-JV-Nonce").Single();
        Assert.Equal(Installation.InstallationId, request.Headers.GetValues("X-JV-Installation").Single());
        Assert.Equal(JetVentaBrokerClient.Sign(Installation.InstallationSecret, "POST", "/v1/oauth/pickup", timestamp, nonce, handler.Body), request.Headers.GetValues("X-JV-Signature").Single());
        Assert.Matches("^[A-Za-z0-9_-]{16,64}$", nonce);
    }

    [Fact]
    public async Task PickupReturnsTokensWhenCompleted()
    {
        var handler = new BrokerHandler(HttpStatusCode.OK, "{\"status\":\"completed\",\"accessToken\":\"APP_USR-1\",\"refreshToken\":\"TG-1\",\"userId\":\"987\",\"expiresIn\":15552000,\"scope\":\"offline_access\"}");
        var result = await CreateClient(handler).PickupAsync(Installation, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal("APP_USR-1", result.Tokens!.AccessToken);
        Assert.Equal("TG-1", result.Tokens.RefreshToken);
        Assert.Equal(987, result.Tokens.UserId);
        Assert.Equal(15552000, result.Tokens.ExpiresIn);
    }

    [Fact]
    public async Task RefreshReportsRevokedAuthorization()
    {
        var handler = new BrokerHandler(HttpStatusCode.Unauthorized, "{\"message\":\"Mercado Pago ya no reconoce esta autorizaci\\u00f3n.\",\"code\":\"authorization_revoked\"}");
        var exception = await Assert.ThrowsAsync<MercadoPagoPointException>(() => CreateClient(handler).RefreshAsync(Installation, "TG-old", CancellationToken.None));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("authorization_revoked", exception.ProviderCode);
        Assert.Contains("autorización", exception.Message);
    }

    [Fact]
    public void NotConfiguredWithoutBaseAddress()
    {
        Assert.False(new JetVentaBrokerClient(new HttpClient()).IsConfigured);
    }

    private static JetVentaBrokerClient CreateClient(HttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://mp.example.com/") });

    private sealed class BrokerHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
