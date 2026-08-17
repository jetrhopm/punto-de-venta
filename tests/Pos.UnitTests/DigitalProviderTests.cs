using Pos.Integrations.DigitalServices;
using Pos.Integrations.LaRedMas;

namespace Pos.UnitTests;

public sealed class DigitalProviderTests
{
    [Fact]
    public void CatalogExposesTaecelAndLaRedMasAsSeparateProviders()
    {
        Assert.Equal(
            [DigitalProviderId.Taecel, DigitalProviderId.LaRedMas],
            DigitalProviderCatalog.All.Select(provider => provider.Id).ToArray());
    }

    [Fact]
    public async Task LaRedMasDoesNotSendTransactionsWithoutOfficialContract()
    {
        var provider = new LaRedMasProvider(
            new HttpClient(),
            new LaRedMasProviderOptions(new Uri("https://example.invalid/"), "test-user", "test-password"));

        var exception = await Assert.ThrowsAsync<DigitalProviderNotConfiguredException>(() => provider.GetProductsAsync(CancellationToken.None));

        Assert.Contains("contrato", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
