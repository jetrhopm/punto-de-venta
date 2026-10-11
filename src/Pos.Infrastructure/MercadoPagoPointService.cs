using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pos.Integrations.MercadoPago;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure;

public sealed record MercadoPagoSettingsResult(bool Enabled, string Environment, bool AccountConnected, long? AccountUserId, string TerminalId, string TerminalLabel, bool OAuthAvailable, bool WebhookConfigured, string RegisterName, string Message, bool TestTokenAllowed = false);
public sealed record MercadoPagoOAuthStatusResult(string Status, string Message);
public sealed record MercadoPagoTerminalResult(string Id, string Label, string OperatingMode, bool Selected);
public sealed record ConfigureMercadoPagoTestCommand(string AccessToken);
public sealed record SelectMercadoPagoTerminalCommand(string TerminalId, string Label);
public sealed record ActivateMercadoPagoTerminalCommand(string TerminalId);
public sealed record SetMercadoPagoEnabledCommand(bool Enabled);
public sealed record CreateMercadoPagoOrderCommand(Guid SaleOperationId, Guid AttemptId, decimal Amount, string Description);
public sealed record MercadoPagoOrderResult(Guid OperationId, string OrderId, string Status, string StatusDetail, decimal Amount, bool Approved, bool Finished);
public sealed record MercadoPagoPendingOrderResult(Guid OperationId, Guid SaleOperationId, int? TicketNumber, decimal Amount, string Status, string StatusDetail, DateTimeOffset CreatedAtUtc, bool SaleConfirmed);
public sealed record MercadoPagoCheckoutStatus(bool Enabled, string RegisterName);
public sealed record MercadoPagoWebhookReceipt(bool Accepted, string Message);
public sealed record MercadoPagoRefundResult(Guid OperationId, decimal Amount, string Status, string StatusDetail, bool Confirmed);

public sealed class MercadoPagoPointService(PosDbContext database, MercadoPagoPointClient client, JetVentaBrokerClient broker, IConfiguration configuration, SaleService sales)
{
    private static readonly SemaphoreSlim TokenRefreshGate = new(1, 1);
    public async Task<MercadoPagoSettingsResult?> GetSettingsAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        var connected = !string.IsNullOrWhiteSpace(store.MercadoPagoAccessTokenProtected);
        // La autorización se hace a través del intermediario central de JetVenta,
        // que es el único que conoce el Client Secret de la aplicación.
        var oauth = broker.IsConfigured;
        var webhook = !string.IsNullOrWhiteSpace(configuration["MercadoPago:WebhookSecret"]);
        var message = !connected ? "Autoriza la cuenta de Mercado Pago o guarda un Access Token de prueba." : string.IsNullOrWhiteSpace(register.MercadoPagoTerminalId) ? "Cuenta conectada. Selecciona una terminal Point en modo PDV para esta caja." : !register.MercadoPagoEnabled ? "Cuenta y terminal configuradas. Point está desactivado sólo en esta caja." : !webhook ? "Cuenta y terminal listas para cobrar en esta caja. Falta configurar el secreto Webhook HTTPS para conciliación automática." : "Cuenta, terminal y conciliación Webhook listas para cobrar en esta caja.";
        return new(register.MercadoPagoEnabled, store.MercadoPagoEnvironment, connected, store.MercadoPagoUserId, register.MercadoPagoTerminalId, register.MercadoPagoTerminalLabel, oauth, webhook, register.Name, message, TestTokenAllowed);
    }

    // El cajero necesita saber si esta caja cobra con Point, pero no debe recibir
    // credenciales ni permiso para modificar la configuración.
    public async Task<MercadoPagoCheckoutStatus?> GetCheckoutStatusAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        return register is null ? null : new MercadoPagoCheckoutStatus(register.MercadoPagoEnabled, register.Name);
    }

    public async Task<MercadoPagoSettingsResult?> ConfigureTestTokenAsync(string token, ConfigureMercadoPagoTestCommand command, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        if (!TestTokenAllowed) throw new ArgumentException("El modo de pruebas está desactivado en esta instalación. Usa Autorizar cuenta.");
        var accessToken = command.AccessToken?.Trim() ?? string.Empty;
        if (!IsTestAccessToken(accessToken)) throw new ArgumentException("Escribe un Access Token de prueba de Mercado Pago. Normalmente inicia con APP_USR-; obténlo desde Tus integraciones > Pruebas > Credenciales de prueba.");
        _ = await client.ListTerminalsAsync(accessToken, cancellationToken);
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        store.MercadoPagoAccessTokenProtected = Protect(accessToken);
        store.MercadoPagoRefreshTokenProtected = string.Empty;
        store.MercadoPagoEnvironment = "Test";
        register.MercadoPagoEnabled = true;
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
        register.MercadoPagoEnabled = true;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool?> ActivateTerminalAsync(string token, ActivateMercadoPagoTerminalCommand command, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        if (string.IsNullOrWhiteSpace(command.TerminalId)) throw new ArgumentException("Selecciona una terminal antes de activar el modo PDV.");
        // Mercado Pago decide qué modelos admiten el modo PDV (incluye modelos nuevos como
        // Point Smart 2); si no es compatible, su respuesta se muestra al usuario.
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
        register.MercadoPagoEnabled = command.Enabled;
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
            storeRegister.MercadoPagoEnabled = false;
            storeRegister.MercadoPagoTerminalId = string.Empty;
            storeRegister.MercadoPagoTerminalLabel = string.Empty;
        }

        // This historical store flag is retained only as the migration baseline
        // for older installations. The active setting is now on each register.
        store.MercadoPagoEnabled = false;
        store.MercadoPagoEnvironment = "Test";
        store.MercadoPagoAccessTokenProtected = string.Empty;
        store.MercadoPagoRefreshTokenProtected = string.Empty;
        store.MercadoPagoUserId = null;
        store.MercadoPagoTokenExpiresAtUtc = null;
        store.MercadoPagoOAuthState = string.Empty;
        // MercadoPagoOAuthVerifierProtected guarda la identidad de esta instalación ante el
        // intermediario, no datos de la cuenta; se conserva para volver a autorizar.
        store.MercadoPagoOAuthStateExpiresAtUtc = null;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Inicia la autorización en el intermediario. Columnas reutilizadas de la versión anterior:
    //   MercadoPagoOAuthState              -> id del intento en el intermediario
    //   MercadoPagoOAuthStateExpiresAtUtc  -> vencimiento del intento
    //   MercadoPagoOAuthVerifierProtected  -> identidad de la instalación (DPAPI)
    public async Task<string?> BeginOAuthAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        if (!broker.IsConfigured) throw new InvalidOperationException("Falta configurar la dirección del intermediario de JetVenta (MercadoPago:BrokerUrl).");
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        var installation = await EnsureBrokerInstallationAsync(store, cancellationToken);
        BrokerOAuthStart started;
        try { started = await broker.StartAsync(installation, cancellationToken); }
        catch (MercadoPagoPointException exception) when (exception.ProviderCode == "unknown_installation")
        {
            // El intermediario no reconoce esta instalación (base reiniciada o revocada): se registra de nuevo una vez.
            store.MercadoPagoOAuthVerifierProtected = string.Empty;
            installation = await EnsureBrokerInstallationAsync(store, cancellationToken);
            started = await broker.StartAsync(installation, cancellationToken);
        }
        store.MercadoPagoOAuthState = started.AttemptId.ToString();
        store.MercadoPagoOAuthStateExpiresAtUtc = started.ExpiresAt;
        await database.SaveChangesAsync(cancellationToken);
        return started.AuthorizationUrl;
    }

    // La ventana de configuración consulta este estado mientras el administrador acepta en el navegador.
    public async Task<MercadoPagoOAuthStatusResult?> GetOAuthStatusAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: true, cancellationToken);
        if (register is null) return null;
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        if (!Guid.TryParse(store.MercadoPagoOAuthState, out var attemptId))
            return new("none", string.IsNullOrWhiteSpace(store.MercadoPagoAccessTokenProtected) ? "No hay una autorización en curso." : "Cuenta conectada.");

        var installation = ReadBrokerInstallation(store) ?? throw new InvalidOperationException("Falta la identidad de esta instalación. Vuelve a pulsar Autorizar cuenta.");
        BrokerOAuthPickup pickup;
        try { pickup = await broker.PickupAsync(installation, attemptId, cancellationToken); }
        catch (MercadoPagoPointException exception) when (exception.StatusCode is 401 or 404)
        {
            // El intento ya no existe o la instalación no es reconocida: se reinicia el proceso.
            ClearOAuthAttempt(store);
            if (exception.ProviderCode == "unknown_installation") store.MercadoPagoOAuthVerifierProtected = string.Empty;
            await database.SaveChangesAsync(cancellationToken);
            return new("failed", "La autorización ya no está disponible. Vuelve a pulsar Autorizar cuenta.");
        }
        switch (pickup.Status)
        {
            case "pending":
                return new("pending", "Esperando que aceptes el acceso en Mercado Pago.");
            case "completed" when pickup.Tokens is not null:
                store.MercadoPagoAccessTokenProtected = Protect(pickup.Tokens.AccessToken);
                store.MercadoPagoRefreshTokenProtected = string.IsNullOrWhiteSpace(pickup.Tokens.RefreshToken) ? string.Empty : Protect(pickup.Tokens.RefreshToken);
                store.MercadoPagoUserId = pickup.Tokens.UserId == 0 ? null : pickup.Tokens.UserId;
                store.MercadoPagoTokenExpiresAtUtc = pickup.Tokens.ExpiresIn > 0 ? DateTimeOffset.UtcNow.AddSeconds(pickup.Tokens.ExpiresIn) : null;
                store.MercadoPagoEnvironment = "Production";
                ClearOAuthAttempt(store);
                await database.SaveChangesAsync(cancellationToken);
                return new("completed", "Cuenta autorizada. Selecciona la terminal de esta caja.");
            case "picked_up":
                // Otra consulta ya guardó los tokens (por ejemplo, dos ventanas abiertas).
                ClearOAuthAttempt(store);
                await database.SaveChangesAsync(cancellationToken);
                return string.IsNullOrWhiteSpace(store.MercadoPagoAccessTokenProtected)
                    ? new("failed", "La autorización ya se había entregado y no se guardó en esta caja. Vuelve a pulsar Autorizar cuenta.")
                    : new("completed", "Cuenta autorizada.");
            default:
                ClearOAuthAttempt(store);
                await database.SaveChangesAsync(cancellationToken);
                return new(pickup.Status == "expired" ? "expired" : "failed", pickup.Message ?? "La autorización no se completó. Vuelve a intentarlo.");
        }
    }

    private async Task<BrokerInstallation> EnsureBrokerInstallationAsync(StoreRecord store, CancellationToken cancellationToken)
    {
        var existing = ReadBrokerInstallation(store);
        if (existing is not null) return existing;
        var version = typeof(MercadoPagoPointService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var installation = await broker.RegisterAsync(LicenseService.GetMachineFingerprint(), version, cancellationToken);
        store.MercadoPagoOAuthVerifierProtected = Protect(System.Text.Json.JsonSerializer.Serialize(installation));
        await database.SaveChangesAsync(cancellationToken);
        return installation;
    }

    private static BrokerInstallation? ReadBrokerInstallation(StoreRecord store)
    {
        if (string.IsNullOrWhiteSpace(store.MercadoPagoOAuthVerifierProtected)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<BrokerInstallation>(Unprotect(store.MercadoPagoOAuthVerifierProtected)); }
        catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException) { return null; }
    }

    private static void ClearOAuthAttempt(StoreRecord store)
    {
        store.MercadoPagoOAuthState = string.Empty;
        store.MercadoPagoOAuthStateExpiresAtUtc = null;
    }

    private bool TestTokenAllowed => string.Equals(configuration["MercadoPago:AllowTestToken"], "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestAccessToken(string token) =>
        token.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase) ||
        token.StartsWith("APP_USR-", StringComparison.OrdinalIgnoreCase);

    public async Task<MercadoPagoOrderResult?> CreateOrderAsync(string token, CreateMercadoPagoOrderCommand command, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        if (register is null) return null;
        if (command.SaleOperationId == Guid.Empty || command.AttemptId == Guid.Empty || command.Amount <= 0m) throw new ArgumentException("La venta, el intento y el importe del cobro son obligatorios.");
        if (decimal.Round(command.Amount, 2) < 5m) throw new ArgumentException("El importe mínimo para cobrar con Mercado Pago Point es $5.00.");
        var existing = await database.MercadoPagoOrders.SingleOrDefaultAsync(item => item.OperationId == command.AttemptId, cancellationToken);
        if (existing is not null && existing.RegisterId != register.Id) throw new InvalidOperationException("El cobro pertenece a otra caja.");
        if (existing?.Status == "CreationPending" && existing.Amount != decimal.Round(command.Amount, 2)) throw PendingAmountChanged(existing.Amount);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.ProviderOrderId)) return ToResult(existing);
        var store = await database.Stores.SingleAsync(item => item.Id == register.StoreId, cancellationToken);
        if (!register.MercadoPagoEnabled) throw new InvalidOperationException("Mercado Pago Point no está activado en esta caja.");
        if (string.IsNullOrWhiteSpace(register.MercadoPagoTerminalId)) throw new InvalidOperationException("Selecciona una terminal Point para esta caja desde Configuración.");
        var approved = await database.MercadoPagoOrders.SingleOrDefaultAsync(item => item.SaleOperationId == command.SaleOperationId && item.RegisterId == register.Id && item.Status == "Approved", cancellationToken);
        if (approved is not null)
        {
            if (approved.Amount != decimal.Round(command.Amount, 2)) throw new InvalidOperationException("Este ticket ya tiene un cobro Point aprobado por un importe distinto. No generes otro cobro; primero concilia o devuelve el pago anterior.");
            return ToResult(approved);
        }
        var unresolved = await database.MercadoPagoOrders.SingleOrDefaultAsync(item => item.SaleOperationId == command.SaleOperationId && item.RegisterId == register.Id && item.Status != "Approved" && item.Status != "Rejected" && item.Status != "Canceled" && item.Status != "Expired" && item.Status != "Refunded" && item.Status != "AmountMismatch" && item.Status != "CreationFailed", cancellationToken);
        if (unresolved is not null && unresolved.OperationId != command.AttemptId)
        {
            if (unresolved.Amount != decimal.Round(command.Amount, 2))
            {
                if (unresolved.Status == "CreationPending") throw PendingAmountChanged(unresolved.Amount);
                throw new InvalidOperationException("Ya hay un cobro Point pendiente con un importe distinto. Confírmalo o cancélalo antes de modificar el ticket o iniciar otro cobro.");
            }
            if (string.IsNullOrWhiteSpace(unresolved.ProviderOrderId))
            {
                // La primera solicitud pudo llegar a Mercado Pago aunque la respuesta
                // se haya perdido. Reintentar con la misma llave evita un segundo cargo.
                existing = unresolved;
            }
            else
            {
                // La ventana pudo cerrarse por una caída de red. Se entrega la misma orden
                // para que el cliente continúe consultándola, sin duplicar el cargo.
                return ToResult(unresolved);
            }
        }
        existing ??= new MercadoPagoOrderRecord { Id = Guid.NewGuid(), StoreId = store.Id, RegisterId = register.Id, OperationId = command.AttemptId, SaleOperationId = command.SaleOperationId, Amount = decimal.Round(command.Amount, 2), Status = "Pending", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        if (database.Entry(existing).State == EntityState.Detached) { database.MercadoPagoOrders.Add(existing); await database.SaveChangesAsync(cancellationToken); }
        try
        {
            var order = await client.CreateOrderAsync(await GetAccessTokenAsync(store, cancellationToken), register.MercadoPagoTerminalId, existing.OperationId, existing.Amount, command.Description, cancellationToken);
            Apply(existing, order);
            await database.SaveChangesAsync(cancellationToken);
            return ToResult(existing);
        }
        catch (Exception exception) when (IsAmbiguousCreationFailure(exception))
        {
            existing.Status = "CreationPending";
            existing.StatusDetail = exception.Message[..Math.Min(exception.Message.Length, 120)];
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(CancellationToken.None);
            throw new InvalidOperationException("No se pudo confirmar si Mercado Pago recibió el cobro. JetVenta conservó el intento; vuelve a pulsar cobrar para retomarlo sin generar otro cargo.", exception);
        }
        catch (MercadoPagoPointException exception)
        {
            existing.Status = "CreationFailed";
            existing.StatusDetail = exception.Message[..Math.Min(exception.Message.Length, 120)];
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<MercadoPagoOrderResult?> RefreshOrderAsync(string token, Guid operationId, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        if (register is null) return null;
        var record = await database.MercadoPagoOrders.SingleOrDefaultAsync(item => item.OperationId == operationId, cancellationToken) ?? throw new KeyNotFoundException("No se encontró el cobro de Mercado Pago.");
        if (record.RegisterId != register.Id) throw new InvalidOperationException("El cobro pertenece a otra caja.");
        if (!string.IsNullOrWhiteSpace(record.ProviderOrderId) && !IsFinished(record.Status))
        {
            var store = await database.Stores.SingleAsync(item => item.Id == record.StoreId, cancellationToken);
            var order = await client.GetOrderAsync(await GetAccessTokenAsync(store, cancellationToken), record.ProviderOrderId!, cancellationToken);
            Apply(record, order);
            await ReconcileRefundsAsync(record, order, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
        }
        return ToResult(record);
    }

    // Devuelve únicamente órdenes que podrían requerir una acción humana. Las aprobadas
    // sólo aparecen si la venta local todavía no se confirmó.
    public async Task<IReadOnlyList<MercadoPagoPendingOrderResult>?> ListPendingOrdersAsync(string token, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        if (register is null) return null;

        var orders = await database.MercadoPagoOrders.AsNoTracking()
            .Where(item => item.RegisterId == register.Id)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(40)
            .ToListAsync(cancellationToken);
        var operationIds = orders.Select(item => item.SaleOperationId).Distinct().ToArray();
        var confirmedSales = operationIds.Length == 0
            ? []
            : (await database.Sales.AsNoTracking().Where(item => operationIds.Contains(item.OperationId)).Select(item => item.OperationId).ToListAsync(cancellationToken)).ToHashSet();
        var ticketNumbers = operationIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await database.SaleDrafts.AsNoTracking()
                .Where(item => operationIds.Contains(item.OperationId) && item.Status == "Open")
                .ToDictionaryAsync(item => item.OperationId, item => item.TicketNumber, cancellationToken);

        return orders
            .Where(item => !IsFinished(item.Status) || (item.Status == "Approved" && !confirmedSales.Contains(item.SaleOperationId)))
            .Select(item => new MercadoPagoPendingOrderResult(
                item.OperationId,
                item.SaleOperationId,
                ticketNumbers.GetValueOrDefault(item.SaleOperationId),
                item.Amount,
                item.Status,
                item.StatusDetail,
                item.CreatedAtUtc,
                confirmedSales.Contains(item.SaleOperationId)))
            .ToArray();
    }

    // Recupera una venta cuyo pago Point fue aprobado mientras el escritorio no podía
    // terminar el flujo. SaleService vuelve a validar ticket, turno, precios e inventario.
    public async Task<CompleteSaleResult?> CompleteApprovedOrderAsync(string token, Guid operationId, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        if (register is null) return null;
        var order = await database.MercadoPagoOrders.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == operationId, cancellationToken)
            ?? throw new KeyNotFoundException("No se encontró el cobro Point.");
        if (order.RegisterId != register.Id) throw new InvalidOperationException("El cobro pertenece a otra caja.");
        if (order.Status != "Approved") throw new InvalidOperationException("Sólo se puede finalizar una venta cuando Mercado Pago la marque como aprobada.");

        var existing = await database.Sales.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == order.SaleOperationId, cancellationToken);
        if (existing is not null) return new CompleteSaleResult(existing.Id, existing.OperationId, existing.Total, 0m, 0m, true);

        var draft = await database.SaleDrafts.AsNoTracking().Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.OperationId == order.SaleOperationId && item.Status == "Open", cancellationToken)
            ?? throw new InvalidOperationException("No se encontró el ticket original. No se registrará una venta automática; revisa el cobro y el historial antes de continuar.");
        if (draft.Lines.Count == 0) throw new InvalidOperationException("El ticket original no tiene productos. No es seguro finalizar esta venta automáticamente.");

        return await sales.CompleteAsync(token, new CompleteSaleCommand(
            draft.OperationId,
            draft.Lines.Select(line => new SaleLineCommand(line.ProductId, line.Quantity)).ToArray(),
            CashReceived: 0m,
            PaymentMethod: "Card",
            CardAmount: order.Amount,
            DraftId: draft.Id,
            PrintRequested: true), cancellationToken);
    }

    // Se invoca desde cancelaciones y devoluciones antes de alterar inventario o caja.
    // Un reembolso pendiente nunca autoriza el movimiento local: evita devolver dinero
    // dos veces cuando la conexión con Point falla a mitad de la operación.
    public async Task<MercadoPagoRefundResult?> EnsureSalePointRefundedAsync(Guid saleId, Guid saleOperationId, Guid registerId, Guid operationId, decimal? amount, CancellationToken cancellationToken)
    {
        if (amount is <= 0m) return null;
        var orders = await database.MercadoPagoOrders
            .Where(item => item.SaleOperationId == saleOperationId && item.RegisterId == registerId && item.Status == "Approved")
            .ToListAsync(cancellationToken);
        if (orders.Count == 0) return null;
        if (orders.Count != 1) throw new InvalidOperationException("La venta tiene más de un cobro Point aprobado. Revisa la conciliación antes de solicitar un reembolso.");

        var order = orders[0];
        if (string.IsNullOrWhiteSpace(order.ProviderOrderId)) throw new InvalidOperationException("El cobro Point no conserva su identificador de Mercado Pago. No es seguro reembolsarlo automáticamente.");
        var nonCashPaid = await database.Payments.Where(item => item.SaleId == saleId && item.Method != "Cash").SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        if (decimal.Round(nonCashPaid, 2) != decimal.Round(order.Amount, 2)) throw new InvalidOperationException("La venta combina Mercado Pago Point con otra forma de pago distinta de efectivo. Revisa el reembolso manualmente para no devolver un importe equivocado.");
        var requestedAmount = decimal.Round(amount ?? order.Amount, 2);
        var existing = await database.MercadoPagoRefunds.SingleOrDefaultAsync(item => item.OperationId == operationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.SaleId != saleId || existing.MercadoPagoOrderId != order.Id || existing.Amount != requestedAmount) throw new InvalidOperationException("La operación de reembolso ya está asociada a otro importe.");
            if (!IsRefundConfirmed(existing.Status))
                await RefreshRefundAsync(existing, order, cancellationToken);
            if (!IsRefundConfirmed(existing.Status)) throw new InvalidOperationException("Mercado Pago está procesando el reembolso. Espera la confirmación antes de registrar la devolución o cancelación.");
            return ToRefundResult(existing);
        }

        var confirmedAmount = await database.MercadoPagoRefunds
            .Where(item => item.MercadoPagoOrderId == order.Id && item.Status == "Confirmed")
            .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
        var remaining = decimal.Round(order.Amount - confirmedAmount, 2);
        if (remaining <= 0m || requestedAmount > remaining) throw new InvalidOperationException("El importe solicitado supera el saldo disponible para reembolso en Mercado Pago.");

        var refund = new MercadoPagoRefundRecord
        {
            Id = Guid.NewGuid(), MercadoPagoOrderId = order.Id, SaleId = saleId, OperationId = operationId,
            Amount = requestedAmount, Status = "Pending", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        database.MercadoPagoRefunds.Add(refund);
        await database.SaveChangesAsync(cancellationToken);
        try
        {
            var store = await database.Stores.SingleAsync(item => item.Id == order.StoreId, cancellationToken);
            var providerOrder = await client.RefundOrderAsync(await GetAccessTokenAsync(store, cancellationToken), order.ProviderOrderId, order.ProviderPaymentId ?? string.Empty, refund.Amount, remaining, refund.OperationId, cancellationToken);
            await ApplyRefundAsync(refund, order, providerOrder, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is MercadoPagoPointException or HttpRequestException or TaskCanceledException)
        {
            refund.Status = "RetryPending";
            refund.StatusDetail = exception.Message[..Math.Min(exception.Message.Length, 240)];
            refund.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            throw;
        }

        if (!IsRefundConfirmed(refund.Status)) throw new InvalidOperationException("Mercado Pago recibió el reembolso pero aún no lo confirma. Espera unos minutos y vuelve a intentar la operación; JetVenta no modificó la venta.");
        return ToRefundResult(refund);
    }

    public async Task<MercadoPagoOrderResult?> CancelOrderAsync(string token, Guid operationId, CancellationToken cancellationToken)
    {
        var register = await CurrentRegisterAsync(token, configure: false, cancellationToken);
        if (register is null) return null;
        var record = await database.MercadoPagoOrders.SingleAsync(item => item.OperationId == operationId, cancellationToken);
        if (record.RegisterId != register.Id) throw new InvalidOperationException("El cobro pertenece a otra caja.");
        if (IsFinished(record.Status)) return ToResult(record);
        if (string.IsNullOrWhiteSpace(record.ProviderOrderId))
        {
            if (record.Status == "CreationPending") throw new InvalidOperationException("No se puede cancelar localmente un cobro cuya creación no fue confirmada. Vuelve a pulsar cobrar para reconciliarlo con la misma operación.");
            record.Status = "Canceled"; record.StatusDetail = "canceled_by_api"; record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            return ToResult(record);
        }
        var store = await database.Stores.SingleAsync(item => item.Id == record.StoreId, cancellationToken);
        Apply(record, await client.CancelOrderAsync(await GetAccessTokenAsync(store, cancellationToken), record.ProviderOrderId, Guid.NewGuid(), cancellationToken));
        await database.SaveChangesAsync(cancellationToken);
        return ToResult(record);
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
                    var providerOrder = await client.GetOrderAsync(await GetAccessTokenAsync(store, cancellationToken), webhook.ProviderOrderId, cancellationToken);
                    Apply(order, providerOrder);
                    await ReconcileRefundsAsync(order, providerOrder, cancellationToken);
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

    private static void Apply(MercadoPagoOrderRecord record, MercadoPagoOrder order)
    {
        record.ProviderOrderId = order.Id;
        record.ProviderPaymentId = order.PaymentId ?? string.Empty;
        record.Status = MapStatus(order.Status);
        record.StatusDetail = order.StatusDetail;
        if (record.Status == "Approved")
        {
            if (order.PaidAmount is not decimal paidAmount)
            {
                // No se acepta un cobro sin monto confirmado. Point puede entregar una
                // respuesta inicial incompleta; se volverá a consultar la orden.
                record.Status = "PendingVerification";
                record.StatusDetail = "Mercado Pago procesó la orden, pero aún no informó el importe cobrado.";
            }
            else if (decimal.Round(paidAmount, 2) != record.Amount)
            {
                record.Status = "AmountMismatch";
                record.StatusDetail = "El importe aprobado por Mercado Pago no coincide con el importe solicitado.";
            }
        }
        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
    private async Task RefreshRefundAsync(MercadoPagoRefundRecord refund, MercadoPagoOrderRecord order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.ProviderOrderId)) return;
        var store = await database.Stores.SingleAsync(item => item.Id == order.StoreId, cancellationToken);
        var providerOrder = await client.GetOrderAsync(await GetAccessTokenAsync(store, cancellationToken), order.ProviderOrderId, cancellationToken);
        Apply(order, providerOrder);
        await ApplyRefundAsync(refund, order, providerOrder, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task ReconcileRefundsAsync(MercadoPagoOrderRecord order, MercadoPagoOrder providerOrder, CancellationToken cancellationToken)
    {
        var refunds = await database.MercadoPagoRefunds.Where(item => item.MercadoPagoOrderId == order.Id && item.Status != "Confirmed").ToListAsync(cancellationToken);
        foreach (var refund in refunds) await ApplyRefundAsync(refund, order, providerOrder, cancellationToken);
    }

    private async Task ApplyRefundAsync(MercadoPagoRefundRecord refund, MercadoPagoOrderRecord order, MercadoPagoOrder providerOrder, CancellationToken cancellationToken)
    {
        var usedIds = await database.MercadoPagoRefunds
            .Where(item => item.MercadoPagoOrderId == order.Id && item.Id != refund.Id && item.ProviderRefundId != null)
            .Select(item => item.ProviderRefundId!)
            .ToArrayAsync(cancellationToken);
        var providerRefund = !string.IsNullOrWhiteSpace(refund.ProviderRefundId)
            ? providerOrder.Refunds.SingleOrDefault(item => item.Id == refund.ProviderRefundId)
            : providerOrder.Refunds.FirstOrDefault(item => item.Amount == refund.Amount && string.Equals(item.TransactionId, order.ProviderPaymentId, StringComparison.Ordinal) && !usedIds.Contains(item.Id, StringComparer.Ordinal));
        if (providerRefund is not null)
        {
            refund.ProviderRefundId = providerRefund.Id;
            refund.StatusDetail = providerRefund.Status;
            refund.Status = IsProviderRefundConfirmed(providerRefund.Status) || string.Equals(providerOrder.Status, "refunded", StringComparison.OrdinalIgnoreCase) ? "Confirmed" : "PendingConfirmation";
        }
        else
        {
            refund.Status = string.Equals(providerOrder.Status, "refunded", StringComparison.OrdinalIgnoreCase) ? "Confirmed" : "PendingConfirmation";
            refund.StatusDetail = string.IsNullOrWhiteSpace(providerOrder.StatusDetail) ? providerOrder.Status : providerOrder.StatusDetail;
        }
        refund.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static bool IsProviderRefundConfirmed(string status) => status.Equals("processed", StringComparison.OrdinalIgnoreCase) || status.Equals("approved", StringComparison.OrdinalIgnoreCase) || status.Equals("refunded", StringComparison.OrdinalIgnoreCase);
    private static bool IsAmbiguousCreationFailure(Exception exception) => exception is HttpRequestException or TaskCanceledException or MercadoPagoPointException { StatusCode: >= 500 };
    private static InvalidOperationException PendingAmountChanged(decimal originalAmount) => new($"Hay un cobro Point cuya creación no pudo confirmarse por ${originalAmount:0.00}. Para evitar un cargo duplicado, restaura el ticket a ese importe y pulsa cobrar para conciliarlo. No se puede modificar ni cancelar hasta conocer el resultado.");
    private static bool IsRefundConfirmed(string status) => status == "Confirmed";
    private static MercadoPagoRefundResult ToRefundResult(MercadoPagoRefundRecord item) => new(item.OperationId, item.Amount, item.Status, item.StatusDetail, IsRefundConfirmed(item.Status));
    private static string MapStatus(string status) => status switch { "created" => "Created", "at_terminal" => "AtTerminal", "processed" => "Approved", "failed" => "Rejected", "canceled" => "Canceled", "expired" => "Expired", "refunded" => "Refunded", "action_required" => "ActionRequired", _ => "PendingReview" };
    private static bool IsFinished(string status) => status is "Approved" or "Rejected" or "Canceled" or "Expired" or "Refunded" or "AmountMismatch" or "CreationFailed";
    private static MercadoPagoOrderResult ToResult(MercadoPagoOrderRecord item) => new(item.OperationId, item.ProviderOrderId ?? string.Empty, item.Status, item.StatusDetail, item.Amount, item.Status == "Approved", IsFinished(item.Status));
    private static string BuildTerminalLabel(MercadoPagoTerminal item) => $"{item.Id} | Caja {item.ExternalPosId} | {item.OperatingMode}";
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
    // El Access Token de Mercado Pago dura 180 días. Se renueva por el intermediario cuando
    // faltan menos de 30 días; si el intermediario no responde y el token sigue vigente,
    // se usa el actual para no detener los cobros.
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromDays(30);

    private async Task<string> GetAccessTokenAsync(StoreRecord store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(store.MercadoPagoAccessTokenProtected)) throw new InvalidOperationException("La cuenta de Mercado Pago no está autorizada.");
        if (!NeedsRefresh(store)) return Unprotect(store.MercadoPagoAccessTokenProtected);
        if (string.IsNullOrWhiteSpace(store.MercadoPagoRefreshTokenProtected)) return CurrentOrExpired(store, "La autorización de Mercado Pago expiró. Vuelve a conectar la cuenta desde Configuración.");
        await TokenRefreshGate.WaitAsync(cancellationToken);
        try
        {
            await database.Entry(store).ReloadAsync(cancellationToken);
            if (!NeedsRefresh(store)) return Unprotect(store.MercadoPagoAccessTokenProtected);
            var installation = ReadBrokerInstallation(store);
            if (installation is null || !broker.IsConfigured) return CurrentOrExpired(store, "La autorización de Mercado Pago expiró y esta instalación no puede renovarla. Vuelve a conectar la cuenta desde Configuración.");
            OAuthTokenResult result;
            try { result = await broker.RefreshAsync(installation, Unprotect(store.MercadoPagoRefreshTokenProtected), cancellationToken); }
            catch (MercadoPagoPointException exception) when (exception.ProviderCode == "authorization_revoked")
            {
                // El comercio revocó el permiso o el refresh token ya no es válido.
                store.MercadoPagoAccessTokenProtected = string.Empty;
                store.MercadoPagoRefreshTokenProtected = string.Empty;
                store.MercadoPagoTokenExpiresAtUtc = null;
                await database.SaveChangesAsync(CancellationToken.None);
                throw new InvalidOperationException("Mercado Pago ya no reconoce la autorización de esta tienda. Vuelve a autorizar la cuenta desde Configuración.");
            }
            catch (MercadoPagoPointException)
            {
                return CurrentOrExpired(store, "No se pudo renovar la autorización de Mercado Pago y el acceso ya venció. Revisa la conexión a Internet e intenta de nuevo.");
            }
            store.MercadoPagoAccessTokenProtected = Protect(result.AccessToken);
            if (!string.IsNullOrWhiteSpace(result.RefreshToken)) store.MercadoPagoRefreshTokenProtected = Protect(result.RefreshToken);
            store.MercadoPagoTokenExpiresAtUtc = result.ExpiresIn > 0 ? DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn) : null;
            await database.SaveChangesAsync(cancellationToken);
            return result.AccessToken;
        }
        finally { TokenRefreshGate.Release(); }
    }

    private static bool NeedsRefresh(StoreRecord store) => store.MercadoPagoTokenExpiresAtUtc is DateTimeOffset expiresAt && expiresAt <= DateTimeOffset.UtcNow.Add(RefreshWindow);

    private static string CurrentOrExpired(StoreRecord store, string expiredMessage)
    {
        if (store.MercadoPagoTokenExpiresAtUtc is DateTimeOffset expiresAt && expiresAt > DateTimeOffset.UtcNow.AddMinutes(5)) return Unprotect(store.MercadoPagoAccessTokenProtected);
        throw new InvalidOperationException(expiredMessage);
    }

    private static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protección de credenciales de JetVenta requiere Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine));
    }

    private static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protección de credenciales de JetVenta requiere Windows.");
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.LocalMachine)); }
        catch (CryptographicException exception) { throw new InvalidOperationException("Las credenciales de Mercado Pago pertenecen a otro equipo o ya no son válidas. Vuelve a conectar la cuenta en esta caja principal.", exception); }
    }

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
