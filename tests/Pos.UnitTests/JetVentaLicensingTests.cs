using Pos.Domain;
using System.Security.Cryptography;

namespace Pos.UnitTests;

public sealed class JetVentaLicensingTests
{
    [Fact]
    public void RequestCode_round_trips_without_machine_data_loss()
    {
        var code = JetVentaLicensing.CreateRequestCode("JV1-0123456789ABCDEF01234567");

        var valid = JetVentaLicensing.TryReadRequestCode(code, out var request, out var error);

        Assert.True(valid, error);
        Assert.NotNull(request);
        Assert.Equal("JV1-0123456789ABCDEF01234567", request!.MachineFingerprint);
        Assert.Equal(JetVentaLicensing.Product, request.Product);
        Assert.Equal(JetVentaLicensing.ProtocolVersion, request.Version);
    }

    [Fact]
    public void Legacy_request_codes_remain_accepted_by_the_issuer()
    {
        const string legacyJson = "{\"version\":1,\"product\":\"JetVenta\",\"machineFingerprint\":\"JV1-0123456789ABCDEF01234567\"}";
        var code = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(legacyJson));

        var valid = JetVentaLicensing.TryReadRequestCode(code, out var request, out var error);

        Assert.True(valid, error);
        Assert.Equal(JetVentaLicensing.LegacyProtocolVersion, request!.Version);
    }

    [Fact]
    public void Legacy_license_signature_payload_is_unchanged()
    {
        var claims = new JetVentaLicenseClaims(
            JetVentaLicensing.LegacyProtocolVersion,
            JetVentaLicensing.Product,
            "00000000-0000-0000-0000-000000000001",
            "JV1-0123456789ABCDEF01234567",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            null,
            "Tienda");
        var expected = string.Join("\n", "JetVenta-Licencia", "1", "JetVenta", claims.LicenseId, claims.MachineFingerprint,
            "2026-09-05T12:00:00.0000000Z", "", "VGllbmRh");

        Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(JetVentaLicensing.CanonicalPayload(claims)));
        Assert.False(claims.HasMobileEntitlement);
    }

    [Fact]
    public void Mobile_entitlement_is_part_of_the_version_two_signature_payload()
    {
        var claims = new JetVentaLicenseClaims(
            JetVentaLicensing.ProtocolVersion,
            JetVentaLicensing.Product,
            "00000000-0000-0000-0000-000000000001",
            "JV1-0123456789ABCDEF01234567",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            null,
            "Tienda",
            IncludesMobile: true);

        var payload = System.Text.Encoding.UTF8.GetString(JetVentaLicensing.CanonicalPayload(claims));

        Assert.EndsWith("\n1", payload);
        Assert.True(claims.HasMobileEntitlement);
        Assert.False((claims with { IncludesMobile = false }).HasMobileEntitlement);
    }

    [Fact]
    public void License_signed_by_a_different_private_key_is_rejected()
    {
        using var key = ECDsa.Create(ECCurve.CreateFromFriendlyName("nistP256"));
        var claims = new JetVentaLicenseClaims(1, "JetVenta", Guid.NewGuid().ToString(), "JV1-TEST", DateTimeOffset.UtcNow, null, "Tienda de prueba");
        var signed = new JetVentaSignedLicense(claims, JetVentaLicensing.Sign(claims, key));
        var altered = signed with { License = signed.License with { StoreName = "Tienda alterada" } };

        Assert.False(JetVentaLicensing.Verify(signed));
        Assert.False(JetVentaLicensing.Verify(altered));
    }

    [Fact]
    public void License_file_rejects_invalid_payload()
    {
        var valid = JetVentaLicensing.TryReadLicense("{\"license\":{},\"signature\":\"invalid\"}", out _, out var error);

        Assert.False(valid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void License_file_rejects_unknown_fields()
    {
        const string content = """
            {"license":{"version":1,"product":"JetVenta","licenseId":"00000000-0000-0000-0000-000000000001","machineFingerprint":"JV1-0123456789ABCDEF01234567","issuedAtUtc":"2026-09-05T12:00:00Z","expiresAtUtc":null,"storeName":"Tienda","unexpected":true},"signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=="}
            """;

        var valid = JetVentaLicensing.TryReadLicense(content, out _, out var error);

        Assert.False(valid);
        Assert.Equal("El archivo de licencia no tiene un formato reconocido.", error);
    }

    [Fact]
    public void License_file_rejects_oversized_content()
    {
        var valid = JetVentaLicensing.TryReadLicense(new string('A', 16 * 1024 + 1), out _, out var error);

        Assert.False(valid);
        Assert.Contains("demasiado grande", error);
    }

    [Fact]
    public void Issuer_enrollment_request_round_trips_without_machine_data_loss()
    {
        using var rsa = RSA.Create(3072);
        var encryptionPublicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var code = JetVentaIssuerAuthorization.CreateEnrollmentRequestCode("ISSUER-DEVICE-0123456789", encryptionPublicKey);

        var valid = JetVentaIssuerAuthorization.TryReadEnrollmentRequest(code, out var request, out var error);

        Assert.True(valid, error);
        Assert.NotNull(request);
        Assert.Equal("ISSUER-DEVICE-0123456789", request!.MachineFingerprint);
        Assert.Equal(encryptionPublicKey, request.EncryptionPublicKey);
    }

    [Fact]
    public void Issuer_authorization_rejects_unsigned_document()
    {
        const string content = "{\"version\":1,\"product\":\"JetVenta\",\"requestId\":\"00000000-0000-0000-0000-000000000001\",\"machineFingerprint\":\"ISSUER-DEVICE\",\"encryptedIssuerKey\":\"AQIDBA==\",\"signature\":\"AQIDBA==\"}";

        var valid = JetVentaIssuerAuthorization.TryReadAndVerifyAuthorization(content, out _, out var error);

        Assert.False(valid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
