using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Integrations.MercadoPago;

public sealed record MercadoPagoWebhookValidation(bool IsValid, string Error);

public static class MercadoPagoWebhookSignature
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(10);

    public static MercadoPagoWebhookValidation Validate(string? signatureHeader, string? requestId, string? dataId, string? secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret)) return new(false, "El secreto de Webhook no está configurado en el servidor.");
        if (string.IsNullOrWhiteSpace(signatureHeader)) return new(false, "Falta la firma de Mercado Pago.");

        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in signatureHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                     .Select(item => item.Split('=', 2, StringSplitOptions.TrimEntries))
                     .Where(item => item.Length == 2))
        {
            if (!parts.TryAdd(pair[0], pair[1])) return new(false, "La firma de Mercado Pago está duplicada.");
        }
        if (!parts.TryGetValue("ts", out var timestamp) || !parts.TryGetValue("v1", out var signature) ||
            !long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return new(false, "La firma de Mercado Pago no tiene el formato esperado.");
        }

        var signedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if ((now - signedAt).Duration() > MaximumAge) return new(false, "La notificación de Mercado Pago expiró.");

        var manifest = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(dataId)) manifest.Append("id:").Append(dataId.Trim().ToLowerInvariant()).Append(';');
        if (!string.IsNullOrWhiteSpace(requestId)) manifest.Append("request-id:").Append(requestId.Trim()).Append(';');
        manifest.Append("ts:").Append(timestamp).Append(';');

        byte[] received;
        try { received = Convert.FromHexString(signature); }
        catch (FormatException) { return new(false, "La firma de Mercado Pago no es válida."); }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest.ToString()));
        return received.Length == expected.Length && CryptographicOperations.FixedTimeEquals(received, expected)
            ? new(true, string.Empty)
            : new(false, "La firma de Mercado Pago no coincide.");
    }
}
