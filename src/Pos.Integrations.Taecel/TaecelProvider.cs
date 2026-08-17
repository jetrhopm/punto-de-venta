using Pos.Integrations.DigitalServices;

namespace Pos.Integrations.Taecel;

public sealed record TaecelProviderOptions(Uri? BaseAddress, string Key, string Nip, bool IsTestEnvironment = true);

public sealed class TaecelProvider(HttpClient httpClient, TaecelProviderOptions options) : IDigitalServicesProvider
{
    private readonly HttpClient client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public DigitalProviderDescriptor Descriptor => DigitalProviderCatalog.All.Single(item => item.Id == DigitalProviderId.Taecel);

    public Task<IReadOnlyList<DigitalProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        throw new DigitalProviderNotConfiguredException("Taecel", "falta aplicar al cliente la URL de pruebas o producción entregada por Taecel.");
    }

    public Task<DigitalTransactionResult> RequestAsync(DigitalTransactionRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        throw new DigitalProviderNotConfiguredException("Taecel", "falta validar el contrato de pruebas y el catálogo de productos de esta cuenta.");
    }

    public Task<DigitalTransactionResult> GetStatusAsync(string providerTransactionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        throw new DigitalProviderNotConfiguredException("Taecel", "falta validar el contrato de pruebas y el catálogo de productos de esta cuenta.");
    }

    private void EnsureConfigured()
    {
        if (options.BaseAddress is null || !options.BaseAddress.IsAbsoluteUri || string.IsNullOrWhiteSpace(options.Key) || string.IsNullOrWhiteSpace(options.Nip))
            throw new DigitalProviderNotConfiguredException("Taecel", "se requieren URL, Key y NIP protegidos.");
    }
}
