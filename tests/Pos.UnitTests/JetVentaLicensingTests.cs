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
    public void Issuer_enrollment_request_round_trips_without_machine_data_loss()
    {
        var encryptionPublicKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(512));
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
