using Pos.Integrations.MercadoPago;
using System.Security.Cryptography;
using System.Text;

namespace Pos.UnitTests;

public sealed class MercadoPagoWebhookSignatureTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_742_505_638);

    [Fact]
    public void AcceptsValidSignatureAndNormalizesOrderIdentifier()
    {
        const string secret = "webhook-secret";
        const string requestId = "request-42";
        const string orderId = "ORD01JYH1Z1YJN4HZ8J3Q0RB3YP6D";
        var signature = CreateSignature(secret, requestId, orderId, Now.ToUnixTimeSeconds());

        var result = MercadoPagoWebhookSignature.Validate(signature, requestId, orderId, secret, Now);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsTamperedSignature()
    {
        var result = MercadoPagoWebhookSignature.Validate("ts=1742505638,v1=00", "request-42", "ORD-42", "webhook-secret", Now);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void RejectsExpiredNotification()
    {
        const string secret = "webhook-secret";
        var oldTimestamp = Now.AddMinutes(-11).ToUnixTimeSeconds();
        var signature = CreateSignature(secret, "request-42", "ORD-42", oldTimestamp);

        var result = MercadoPagoWebhookSignature.Validate(signature, "request-42", "ORD-42", secret, Now);

        Assert.False(result.IsValid);
        Assert.Contains("expir", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateSignature(string secret, string requestId, string orderId, long timestamp)
    {
        var manifest = $"id:{orderId.ToLowerInvariant()};request-id:{requestId};ts:{timestamp};";
        var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return $"ts={timestamp},v1={hash}";
    }
}
