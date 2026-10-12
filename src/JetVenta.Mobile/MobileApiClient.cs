using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JetVenta.Mobile;

public sealed class MobileSessionExpiredException : InvalidOperationException
{
    public MobileSessionExpiredException() : base("Tu sesión venció. Inicia sesión nuevamente.") { }
}

public sealed class MobileCountConflictException(decimal currentStock, string message) : InvalidOperationException(message)
{
    public decimal CurrentStock { get; } = currentStock;
}

public sealed class MobileApiClient(MobileSettings settings)
{
    private const string ProtocolVersion = "2";

    public async Task PairAsync(string server, string code, string deviceName, CancellationToken cancellationToken = default)
    {
        var normalizedServer = NormalizeServer(server);
        using var client = CreateClient(normalizedServer, null, null);
        using var response = await client.PostAsJsonAsync("api/mobile/pair", new MobilePairRequest(code.Trim(), deviceName.Trim()), cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo emparejar el teléfono."));
        var result = await response.Content.ReadFromJsonAsync<MobilePairResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("JetVenta no devolvió la identidad del teléfono.");
        await settings.SavePairingAsync(normalizedServer, result.DeviceToken);
    }

    public async Task<MobileLoginResponse> LoginAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var (server, deviceToken) = await RequirePairingAsync();
        using var client = CreateClient(server, deviceToken, null);
        using var response = await client.PostAsJsonAsync("api/mobile/login", new MobileLoginRequest(userName.Trim(), password), cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo iniciar sesión."));
        var result = await response.Content.ReadFromJsonAsync<MobileLoginResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("JetVenta no devolvió una sesión válida.");
        await settings.SaveLoginAsync(result.AccessToken, userName.Trim());
        return result;
    }

    public async Task<MobileProduct?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync($"api/mobile/products/by-code?code={Uri.EscapeDataString(code.Trim())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            settings.ClearLogin();
            throw new MobileSessionExpiredException();
        }
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo consultar el producto."));
        return await response.Content.ReadFromJsonAsync<MobileProduct>(cancellationToken: cancellationToken);
    }

    public async Task<MobileCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync("api/mobile/session/capabilities", cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudieron consultar los permisos del usuario."));
        return await response.Content.ReadFromJsonAsync<MobileCapabilities>(cancellationToken: cancellationToken) ?? new MobileCapabilities(false, false);
    }

    public async Task<IReadOnlyList<MobileProduct>> SearchAsync(string search, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync($"api/mobile/products/search?q={Uri.EscapeDataString(search.Trim())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            settings.ClearLogin();
            throw new MobileSessionExpiredException();
        }
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo buscar el producto."));
        return await response.Content.ReadFromJsonAsync<List<MobileProduct>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<MobilePurchaseSupplier>> GetPurchaseSuppliersAsync(CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync("api/mobile/purchases/suppliers", cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudieron cargar los proveedores."));
        return await response.Content.ReadFromJsonAsync<List<MobilePurchaseSupplier>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<MobilePurchaseProduct?> FindPurchaseProductByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync($"api/mobile/purchases/products/by-code?code={Uri.EscapeDataString(code.Trim())}", cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo consultar el producto."));
        return await response.Content.ReadFromJsonAsync<MobilePurchaseProduct>(cancellationToken: cancellationToken);
    }

    public async Task<MobilePurchaseRequestResult> CreatePurchaseRequestAsync(CreateMobilePurchaseRequestCommand command, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync("api/mobile/purchases/requests", command, cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo enviar la solicitud de compra."));
        return await response.Content.ReadFromJsonAsync<MobilePurchaseRequestResult>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("JetVenta no devolvió la solicitud guardada.");
    }

    public async Task<MobilePurchaseRequestResult?> GetPurchaseRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync($"api/mobile/purchases/requests/{requestId}", cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo consultar el estado de la solicitud."));
        return await response.Content.ReadFromJsonAsync<MobilePurchaseRequestResult>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<MobileProduct>> BrowseInventoryAsync(string? search, string status, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        var url = $"api/mobile/inventory?q={Uri.EscapeDataString(search?.Trim() ?? string.Empty)}&status={Uri.EscapeDataString(status)}";
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo cargar el inventario."));
        return await response.Content.ReadFromJsonAsync<List<MobileProduct>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<MobileCountSession> StartCountSessionAsync(CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsync("api/mobile/inventory-count-sessions", null, cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo iniciar el conteo."));
        return await response.Content.ReadFromJsonAsync<MobileCountSession>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("JetVenta no devolvió la sesión de conteo.");
    }

    public async Task CloseCountSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsync($"api/mobile/inventory-count-sessions/{sessionId}/close", null, cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo cerrar la sesión de conteo."));
    }

    public async Task<MobileCountResult> RegisterCountAsync(MobileCountRequest request, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync("api/mobile/inventory-counts", request, cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<CountConflictResponse>(cancellationToken: cancellationToken);
            throw new MobileCountConflictException(body?.CurrentStock ?? request.ExpectedStock, body?.Message ?? "La existencia cambió. Actualiza el artículo y vuelve a contar.");
        }
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo registrar el conteo."));
        return await response.Content.ReadFromJsonAsync<MobileCountResult>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("JetVenta no devolvió el resultado del conteo.");
    }

    public async Task<IReadOnlyList<MobileCountHistoryRow>> GetCountHistoryAsync(bool differencesOnly = false, CancellationToken cancellationToken = default)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync($"api/mobile/inventory-counts/history?limit=200&differencesOnly={differencesOnly.ToString().ToLowerInvariant()}", cancellationToken);
        await EnsureAuthorizedAsync(response);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response, "No se pudo cargar la bitácora."));
        return await response.Content.ReadFromJsonAsync<List<MobileCountHistoryRow>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await CreateAuthorizedClientAsync();
            using (client) await client.DeleteAsync("api/mobile/session", cancellationToken);
        }
        finally { settings.ClearLogin(); }
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        var (server, deviceToken) = await RequirePairingAsync();
        var accessToken = await settings.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Tu sesión venció. Inicia sesión nuevamente.");
        return CreateClient(server, deviceToken, accessToken);
    }

    private Task EnsureAuthorizedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            settings.ClearLogin();
            throw new MobileSessionExpiredException();
        }
        return Task.CompletedTask;
    }

    private async Task<(string Server, string DeviceToken)> RequirePairingAsync()
    {
        var server = await settings.GetServerAsync();
        var deviceToken = await settings.GetDeviceTokenAsync();
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(deviceToken)) throw new InvalidOperationException("Este teléfono aún no está emparejado con JetVenta.");
        return (server, deviceToken);
    }

    private static HttpClient CreateClient(string server, string? deviceToken, string? accessToken)
    {
        var client = new HttpClient { BaseAddress = new Uri(server.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-JetVenta-Lan-Protocol", ProtocolVersion);
        if (!string.IsNullOrWhiteSpace(deviceToken)) client.DefaultRequestHeaders.TryAddWithoutValidation("X-JetVenta-Mobile-Token", deviceToken);
        if (!string.IsNullOrWhiteSpace(accessToken)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static string NormalizeServer(string value)
    {
        var candidate = value.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) candidate = "http://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !IPAddress.TryParse(uri.Host, out var address) || !IsPrivateIpv4(address)) throw new ArgumentException("Escribe la IP privada de la computadora servidor, por ejemplo 192.168.1.50:5000.");
        if (uri.IsDefaultPort && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) uri = new UriBuilder(uri) { Port = 5000 }.Uri;
        return uri.ToString().TrimEnd('/');
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (document.RootElement.TryGetProperty("message", out var message) && !string.IsNullOrWhiteSpace(message.GetString())) return message.GetString()!;
            if (document.RootElement.TryGetProperty("detail", out var detail) && !string.IsNullOrWhiteSpace(detail.GetString())) return detail.GetString()!;
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.EnumerateObject().SelectMany(item => item.Value.EnumerateArray()).FirstOrDefault().GetString() is { Length: > 0 } error) return error;
        }
        catch (JsonException) { }
        return fallback;
    }

    private sealed record CountConflictResponse(string? Message, decimal? CurrentStock);
}
