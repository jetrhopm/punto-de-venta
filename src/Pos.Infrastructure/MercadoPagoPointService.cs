using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pos.Integrations.MercadoPago;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record MercadoPagoSettingsResult(bool Enabled, string Environment, bool AccountConnected, long? AccountUserId, string TerminalId, string TerminalLabel, bool OAuthAvailable, bool WebhookConfigured, string Message);
public sealed record MercadoPagoTerminalResult(string Id, string Label, string OperatingMode, bool Selected);
public sealed record ConfigureMercadoPagoTestCommand(string AccessToken);
public sealed record SelectMercadoPagoTerminalCommand(string TerminalId, string Label);
public sealed record ActivateMercadoPagoTerminalCommand(string TerminalId);
public sealed record SetMercadoPagoEnabledCommand(bool Enabled);
public sealed record CreateMercadoPagoOrderCommand(Guid OperationId, decimal Amount, string Description);
public sealed record MercadoPagoOrderResult(Guid OperationId, string OrderId, string Status, string StatusDetail, decimal Amount, bool Approved, bool Finished);
public sealed record MercadoPagoWebhookReceipt(bool Accepted, string Message);

public sealed class MercadoPagoPointService(PosDbContext database, MercadoPagoPointClient client, IConfiguration configuration)
{
    public async Task<MercadoPagoSettingsResult?> GetSettingsAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        var connected = !string.IsNullOrWhiteSpace(store.MercadoPagoAccessTokenProtected);
        var oauth = !string.IsNullOrWhiteSpace(configuration["MercadoPago:ClientId"]) && !string.IsNullOrWhiteSpace(configuration["MercadoPago:ClientSecret"]) && !string.IsNullOrWhiteSpace(configuration["MercadoPago:RedirectUri"]);
        var webhook = !string.IsNullOrWhiteSpace(configuration["MercadoPago:WebhookSecret"]);
        var message = !connected ? "Autoriza la cuenta de Mercado Pago o guarda un Access Token de prueba." : string.IsNullOrWhiteSpace(register.MercadoPagoTerminalId) ? "Cuenta conectada. Selecciona una terminal Point en modo PDV." : !store.MercadoPagoEnabled ? "Cuenta y terminal configuradas. Point está desactivado." : !webhook ? "Cuenta y terminal listas para cobrar. Falta configurar el secreto Webhook HTTPS para conciliación automática." : "Cuenta, terminal y conciliación Webhook listas para cobrar.";
        return new(store.MercadoPagoEnabled, store.MercadoPagoEnvironment, connected, store.MercadoPagoUserId, register.MercadoPagoTerminalId, register.MercadoPagoTerminalLabel, oauth, webhook, message);
    }

    public async Task<MercadoPagoSettingsResult?> ConfigureTestTokenAsync(string token, ConfigureMercadoPagoTestCommand command, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        if (string.IsNullOrWhiteSpace(command.AccessToken) || (!command.AccessToken.StartsWith("TEST-", StringComparison.Ordinal) && !command.AccessToken.StartsWith("APP_USR-", StringComparison.Ordinal))) throw new ArgumentException("Escribe un Access Token de prueba válido de Mercado Pago.");
        _ = await client.ListTerminalsAsync(command.AccessToken.Trim(), cancellationToken);
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        store.MercadoPagoAccessTokenProtected = Protect(command.AccessToken.Trim());
        store.MercadoPagoRefreshTokenProtected = string.Empty;
        store.MercadoPagoEnvironment = "Test";
        store.MercadoPagoEnabled = true;
        store.MercadoPagoUserId = null;
        store.MercadoPagoTokenExpiresAtUtc = null;
        await database.SaveChangesAsync(cancellationToken);
        return await GetSettingsAsync(token, cancellationToken);
    }

    public async Task<IReadOnlyList<MercadoPagoTerminalResult>?> ListTerminalsAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        var terminals = await client.ListTerminalsAsync(await GetAccessTokenAsync(store, cancellationToken), cancellationToken);
        return terminals.Select(item => new MercadoPagoTerminalResult(item.Id, BuildTerminalLabel(item), item.OperatingMode, item.Id == register.MercadoPagoTerminalId)).ToArray();
    }

    public async Task<bool?> SelectTerminalAsync(string token, SelectMercadoPagoTerminalCommand command, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        var available = await client.ListTerminalsAsync(await GetAccessTokenAsync(store, cancellationToken), cancellationToken);
        var terminal = available.SingleOrDefault(item => item.Id == command.TerminalId) ?? throw new ArgumentException("La terminal seleccionada ya no está asociada a esta cuenta.");
        if (!string.Equals(terminal.OperatingMode, "PDV", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La terminal debe estar configurada en modo PDV dentro de Mercado Pago.");
        register.MercadoPagoTerminalId = terminal.Id;
        register.MercadoPagoTerminalLabel = BuildTerminalLabel(terminal);
        store.MercadoPagoEnabled = true;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool?> ActivateTerminalAsync(string token, ActivateMercadoPagoTerminalCommand command, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        if (string.IsNullOrWhiteSpace(command.TerminalId)) throw new ArgumentException("Selecciona una terminal antes de activar el modo PDV.");
        if (!IsPdvCompatible(command.TerminalId)) throw new InvalidOperationException("Este modelo de terminal no admite la integración PDV de Mercado Pago. La API oficial solo permite activar terminales NEWLAND_N950 y PAX_A910.");
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        await client.ActivatePdvAsync(await GetAccessTokenAsync(store, cancellationToken), command.TerminalId.Trim(), cancellationToken);
        return true;
    }

    public async Task<bool?> SetEnabledAsync(string token, SetMercadoPagoEnabledCommand command, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        if (command.Enabled)
        {
            if (string.IsNullOrWhiteSpace(store.MercadoPagoAccessTokenProtected) || string.IsNullOrWhiteSpace(register.MercadoPagoTerminalId)) throw new InvalidOperationException("Autoriza la cuenta y selecciona una terminal PDV antes de activar Mercado Pago Point.");
        }
        store.MercadoPagoEnabled = command.Enabled;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool?> DisconnectAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        var registers = await database.Registers.Where(item => item.StoreId == store.Id).ToListAsync(cancellationToken);
        foreach (var storeRegister in registers)
        {
            storeRegister.MercadoPagoTerminalId = string.Empty;
            storeRegister.MercadoPagoTerminalLabel = string.Empty;
        }

        store.MercadoPagoEnabled = false;
        store.MercadoPagoEnvironment = "Test";
        store.MercadoPagoAccessTokenProtected = string.Empty;
        store.MercadoPagoRefreshTokenProtected = string.Empty;
        store.MercadoPagoUserId = null;
        store.MercadoPagoTokenExpiresAtUtc = null;
        store.MercadoPagoOAuthState = string.Empty;
        store.MercadoPagoOAuthVerifierProtected = string.Empty;
        store.MercadoPagoOAuthStateExpiresAtUtc = null;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<string?> BeginOAuthAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        var clientId = configuration["MercadoPago:ClientId"] ?? throw new InvalidOperationException("JetVenta todavía no tiene configurado su App ID de Mercado Pago.");
        var redirectUri = configuration["MercadoPago:RedirectUri"] ?? throw new InvalidOperationException("Falta registrar el callback HTTPS de Mercado Pago.");
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        store.MercadoPagoOAuthState = state;
        store.MercadoPagoOAuthVerifierProtected = Protect(verifier);
        store.MercadoPagoOAuthStateExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10);
        await database.SaveChangesAsync(cancellationToken);
        return $"https://auth.mercadopago.com/authorization?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256&state={Uri.EscapeDataString(state)}&scope=offline_access";
    }

    public async Task CompleteOAuthAsync(string code, string state, CancellationToken cancellationToken)
    {
        var store = await database.Stores.OrderBy(item => item.CreatedAtUtc).FirstAsync(cancellationToken);
        var receivedState = Encoding.UTF8.GetBytes(state ?? string.Empty);
        var expectedState = Encoding.UTF8.GetBytes(store.MercadoPagoOAuthState);
        if (string.IsNullOrWhiteSpace(code) || receivedState.Length != expectedState.Length || !CryptographicOperations.FixedTimeEquals(receivedState, expectedState) || store.MercadoPagoOAuthStateExpiresAtUtc <= DateTimeOffset.UtcNow) throw new InvalidOperationException("La autorización expiró o no pertenece a esta tienda.");
        var result = await client.ExchangeAuthorizationCodeAsync(configuration["MercadoPago:ClientId"]!, configuration["MercadoPago:ClientSecret"]!, code, configuration["MercadoPago:RedirectUri"]!, Unprotect(store.MercadoPagoOAuthVerifierProtected), cancellationToken);
        store.MercadoPagoAccessTokenProtected = Protect(result.AccessToken);
        store.MercadoPagoRefreshTokenProtected = string.IsNullOrWhiteSpace(result.RefreshToken) ? string.Empty : Protect(result.RefreshToken);
        store.MercadoPagoUserId = result.UserId;
        store.MercadoPagoTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn);
        store.MercadoPagoEnvironment = "Production";
        store.MercadoPagoEnabled = true;
        store.MercadoPagoOAuthState = string.Empty;
        store.MercadoPagoOAuthVerifierProtected = string.Empty;
        store.MercadoPagoOAuthStateExpiresAtUtc = null;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<MercadoPagoOrderResult?> CreateOrderAsync(string token, CreateMercadoPagoOrderCommand command, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        if (register is null) return null;
        if (command.OperationId == Guid.Empty || command.Amount <= 0m) throw new ArgumentException("La operación y el importe del cobro son obligatorios.");
        var existing = await database.MercadoPagoOrders.SingleOrDefaultAsync(item => item.OperationId == command.OperationId, cancellationToken);
        if (existing is not null && existing.RegisterId != register.Id) throw new InvalidOperationException("El cobro pertenece a otra caja.");
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.ProviderOrderId)) return ToResult(existing);
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        if (!store.MercadoPagoEnabled) throw new InvalidOperationException("Mercado Pago Point no está activado en esta tienda.");
        if (string.IsNullOrWhiteSpace(register.MercadoPagoTerminalId)) throw new InvalidOperationException("Selecciona una terminal Point para esta caja desde Configuración.");
        existing ??= new MercadoPagoOrderRecord { Id = Guid.NewGuid(), StoreId = store.Id, RegisterId = register.Id, OperationId = command.OperationId, Amount = decimal.Round(command.Amount, 2), Status = "Pending", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        if (database.Entry(existing).State == EntityState.Detached) { database.MercadoPagoOrders.Add(existing); await database.SaveChangesAsync(cancellationToken); }
        var order = await client.CreateOrderAsync(await GetAccessTokenAsync(store, cancellationToken), register.MercadoPagoTerminalId, command.OperationId, existing.Amount, command.Description, cancellationToken);
        Apply(existing, order);
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(existing);
    }

    public async Task<MercadoPagoOrderResult?> RefreshOrderAsync(string token, Guid operationId, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        if (register is null) return null;
        var record = await database.MercadoPagoOrders.SingleOrDefaultAsync(item => item.OperationId == operationId, cancellationToken) ?? throw new KeyNotFoundException("No se encontró el cobro de Mercado Pago.");
        if (record.RegisterId != register.Id) throw new InvalidOperationException("El cobro pertenece a otra caja.");
        if (!IsFinished(record.Status))
        {
            var store = await database.Stores.SingleAsync(item => item.Id == record.StoreId, cancellationToken);
            Apply(record, await client.GetOrderAsync(await GetAccessTokenAsync(store, cancellationToken), record.ProviderOrderId!, cancellationToken));
            await database.SaveChangesAsync(cancellationToken);
        }
        return ToResult(record);
    }

    public async Task<bool?> CancelOrderAsync(string token, Guid operationId, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        if (register is null) return null;
        var record = await database.MercadoPagoOrders.SingleAsync(item => item.OperationId == operationId, cancellationToken);
        if (record.RegisterId != register.Id) throw new InvalidOperationException("El cobro pertenece a otra caja.");
        if (record.Status == "Created")
        {
            var store = await database.Stores.SingleAsync(item => item.Id == record.StoreId, cancellationToken);
            await client.CancelOrderAsync(await GetAccessTokenAsync(store, cancellationToken), record.ProviderOrderId!, Guid.NewGuid(), cancellationToken);
            record.Status = "Canceled"; record.StatusDetail = "canceled_by_api"; record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
        }
        return true;
    }

    public async Task<MercadoPagoWebhookReceipt> ReceiveWebhookAsync(string providerOrderId, string requestId, string action, string signature, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerOrderId)) return new(false, "La notificación no incluye la orden de Mercado Pago.");
        var now = DateTimeOffset.UtcNow;
        var eventKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{providerOrderId.Trim()}|{requestId.Trim()}|{action.Trim()}|{signature.Trim()}")));
        if (await database.MercadoPagoWebhooks.AnyAsync(item => item.EventKey == eventKey, cancellationToken))
            return new(true, "La notificación ya estaba registrada.");

        database.MercadoPagoWebhooks.Add(new MercadoPagoWebhookRecord
        {
            Id = Guid.NewGuid(),
            EventKey = eventKey,
            ProviderOrderId = providerOrderId.Trim(),
            RequestId = requestId.Trim(),
            Action = action.Trim(),
            Status = "Received",
            ReceivedAtUtc = now,
            NextAttemptAtUtc = now
        });

        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            if (await database.MercadoPagoWebhooks.AsNoTracking().AnyAsync(item => item.EventKey == eventKey, cancellationToken))
                return new(true, "La notificación ya estaba registrada.");
            throw;
        }

        return new(true, "La notificación se registró para conciliación.");
    }

    public async Task<int> ReconcileWebhookQueueAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await database.MercadoPagoWebhooks
            .Where(item => (item.Status == "Received" || item.Status == "Retry") && item.NextAttemptAtUtc <= now)
            .OrderBy(item => item.ReceivedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var webhook in pending)
        {
            webhook.AttemptCount++;
            try
            {
                var order = await database.MercadoPagoOrders.SingleOrDefaultAsync(item => item.ProviderOrderId == webhook.ProviderOrderId, cancellationToken);
                if (order is null)
                {
                    ScheduleRetry(webhook, now, "La orden aún no existe localmente.");
                }
                else
                {
                    var store = await database.Stores.SingleAsync(item => item.Id == order.StoreId, cancellationToken);
                    Apply(order, await client.GetOrderAsync(await GetAccessTokenAsync(store, cancellationToken), webhook.ProviderOrderId, cancellationToken));
                    webhook.Status = "Reconciled";
                    webhook.ProcessedAtUtc = now;
                    webhook.LastError = string.Empty;
                }
            }
            catch (Exception exception) when (exception is MercadoPagoPointException or HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                ScheduleRetry(webhook, now, exception.Message);
            }

            await database.SaveChangesAsync(cancellationToken);
        }

        return pending.Count;
    }

    private static void Apply(MercadoPagoOrderRecord record, MercadoPagoOrder order) { record.ProviderOrderId = order.Id; record.ProviderPaymentId = order.PaymentId ?? string.Empty; record.Status = MapStatus(order.Status); record.StatusDetail = order.StatusDetail; record.UpdatedAtUtc = DateTimeOffset.UtcNow; }
    private static string MapStatus(string status) => status switch { "created" => "Created", "at_terminal" => "AtTerminal", "processed" => "Approved", "failed" => "Rejected", "canceled" => "Canceled", "expired" => "Expired", "refunded" => "Refunded", "action_required" => "Unknown", _ => "Unknown" };
    private static bool IsFinished(string status) => status is "Approved" or "Rejected" or "Canceled" or "Expired" or "Refunded" or "Unknown";
    private static MercadoPagoOrderResult ToResult(MercadoPagoOrderRecord item) => new(item.OperationId, item.ProviderOrderId ?? string.Empty, item.Status, item.StatusDetail, item.Amount, item.Status == "Approved", IsFinished(item.Status));
    private static string BuildTerminalLabel(MercadoPagoTerminal item) => $"{item.Id} | Caja {item.ExternalPosId} | {item.OperatingMode}";
    private static bool IsPdvCompatible(string terminalId) => terminalId.StartsWith("NEWLAND_N950__", StringComparison.OrdinalIgnoreCase) || terminalId.StartsWith("PAX_A910__", StringComparison.OrdinalIgnoreCase);
    private static void ScheduleRetry(MercadoPagoWebhookRecord webhook, DateTimeOffset now, string error)
    {
        if (webhook.AttemptCount >= 12)
        {
            webhook.Status = "Ignored";
            webhook.ProcessedAtUtc = now;
            webhook.LastError = error[..Math.Min(error.Length, 500)];
            return;
        }

        webhook.Status = "Retry";
        webhook.LastError = error[..Math.Min(error.Length, 500)];
        webhook.NextAttemptAtUtc = now.AddMinutes(Math.Min(15, Math.Max(1, webhook.AttemptCount)));
    }
    private async Task<string> GetAccessTokenAsync(StoreRecord store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(store.MercadoPagoAccessTokenProtected)) throw new InvalidOperationException("La cuenta de Mercado Pago no está autorizada.");
        if (store.MercadoPagoTokenExpiresAtUtc is null || store.MercadoPagoTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(5)) return Unprotect(store.MercadoPagoAccessTokenProtected);
        if (string.IsNullOrWhiteSpace(store.MercadoPagoRefreshTokenProtected)) throw new InvalidOperationException("La autorización de Mercado Pago expiró. Vuelve a conectar la cuenta desde Configuración.");
        var result = await client.RefreshAccessTokenAsync(
            configuration["MercadoPago:ClientId"] ?? throw new InvalidOperationException("Falta el App ID de Mercado Pago."),
            configuration["MercadoPago:ClientSecret"] ?? throw new InvalidOperationException("Falta el secreto de la aplicación de Mercado Pago."),
            Unprotect(store.MercadoPagoRefreshTokenProtected),
            cancellationToken);
        store.MercadoPagoAccessTokenProtected = Protect(result.AccessToken);
        if (!string.IsNullOrWhiteSpace(result.RefreshToken)) store.MercadoPagoRefreshTokenProtected = Protect(result.RefreshToken);
        store.MercadoPagoTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn);
        await database.SaveChangesAsync(cancellationToken);
        return result.AccessToken;
    }
    private static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protección de credenciales de JetVenta requiere Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine));
    }

    private static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protección de credenciales de JetVenta requiere Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.LocalMachine));
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<RegisterRecord?> CurrentRegisterAsync(string token, bool configure, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
        var session = await database.Sessions.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (session?.RegisterId is not Guid registerId) return null;
        if (configure)
        {
            var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == session.UserId, cancellationToken);
            if (!user.IsAdministrator && !await database.Permissions.AnyAsync(item => item.UserId == user.Id && item.Code == "ConfigureStore", cancellationToken)) return null;
        }
        return await database.Registers.SingleOrDefaultAsync(item => item.Id == registerId && item.IsActive, cancellationToken);
    }
}
