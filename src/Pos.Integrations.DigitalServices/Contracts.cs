namespace Pos.Integrations.DigitalServices;

public enum DigitalProviderId { Taecel, LaRedMas }
public enum DigitalServiceType { MobileTopUp, BillPayment, DigitalPin }

public sealed record DigitalProviderDescriptor(
    DigitalProviderId Id,
    string DisplayName,
    string Description,
    bool HasOfficialTestEnvironment,
    bool RequiresProviderContract,
    string DocumentationUrl);

public sealed record DigitalProviderConfiguration(
    DigitalProviderId Provider,
    Uri? BaseAddress,
    string CredentialLabel,
    bool IsEnabled,
    bool IsTestEnvironment);

public sealed record DigitalProduct(
    string ProviderProductCode,
    string Name,
    DigitalServiceType ServiceType,
    string Operator,
    decimal? Amount,
    decimal? Commission,
    bool IsActive);

public sealed record DigitalTransactionRequest(
    Guid OperationId,
    DigitalServiceType ServiceType,
    string ProviderProductCode,
    string Reference,
    decimal? Amount);

public sealed record DigitalTransactionResult(
    string ProviderTransactionId,
    string Status,
    string Message,
    decimal? Commission);

public interface IDigitalServicesProvider
{
    DigitalProviderDescriptor Descriptor { get; }
    Task<IReadOnlyList<DigitalProduct>> GetProductsAsync(CancellationToken cancellationToken);
    Task<DigitalTransactionResult> RequestAsync(DigitalTransactionRequest request, CancellationToken cancellationToken);
    Task<DigitalTransactionResult> GetStatusAsync(string providerTransactionId, CancellationToken cancellationToken);
}

public static class DigitalProviderCatalog
{
    public static IReadOnlyList<DigitalProviderDescriptor> All { get; } =
    [
        new(DigitalProviderId.Taecel, "Taecel", "Recargas, pagos de servicios y productos digitales mediante la integración oficial de Taecel.", true, true, "https://taecel.com/portal/integracion-web-services"),
        new(DigitalProviderId.LaRedMas, "La Red+", "Recargas, pines y pagos de servicios mediante la plataforma de Red de Prepago de México.", false, true, "https://rpm-mx.net/plataforma/")
    ];
}

public sealed class DigitalProviderNotConfiguredException(string providerName, string detail)
    : InvalidOperationException($"{providerName} aún no está configurado: {detail}");
