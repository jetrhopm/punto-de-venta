using Pos.Integrations.DigitalServices;

namespace Pos.Integrations.LaRedMas;

public sealed record LaRedMasProviderOptions(Uri? BaseAddress, string UserName, string Password, bool IsTestEnvironment = true);

public sealed class LaRedMasProvider(HttpClient httpClient, LaRedMasProviderOptions options) : IDigitalServicesProvider
{
    private readonly HttpClient client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public DigitalProviderDescriptor Descriptor => DigitalProviderCatalog.All.Single(item => item.Id == DigitalProviderId.LaRedMas);

    public Task<IReadOnlyList<DigitalProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        throw new DigitalProviderNotConfiguredException("La Red+", "Red de Prepago de México debe entregar la URL, credenciales y contrato del web service.");
    }

    public Task<DigitalTransactionResult> RequestAsync(DigitalTransactionRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        throw new DigitalProviderNotConfiguredException("La Red+", "Red de Prepago de México debe entregar el contrato de pruebas antes de enviar operaciones.");
    }

    public Task<DigitalTransactionResult> GetStatusAsync(string providerTransactionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        throw new DigitalProviderNotConfiguredException("La Red+", "Red de Prepago de México debe entregar el contrato de consulta de estado y reversión.");
    }

    private void EnsureConfigured()
    {
        if (options.BaseAddress is null || !options.BaseAddress.IsAbsoluteUri || string.IsNullOrWhiteSpace(options.UserName) || string.IsNullOrWhiteSpace(options.Password))
            throw new DigitalProviderNotConfiguredException("La Red+", "se requieren URL, usuario y contraseña protegidos.");
    }
}
