using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pos.Desktop;

public static class ApiClient
{
    private static readonly HttpClient ClientInstance = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuntoDeVenta", "client-settings.json");

    public static HttpClient Client => ClientInstance;
    public static string BaseUrl { get; private set; } = "http://127.0.0.1:5000";
    public static Guid? DeviceId { get; private set; }
    public static Guid? StoreId { get; private set; }
    public static Guid? RegisterId { get; private set; }

    static ApiClient() => Load();

    public static void SetServer(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("El servidor es obligatorio.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var value = host.Trim();
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = $"http://{value}";
        var uri = new UriBuilder(value) { Port = port }.Uri;
        BaseUrl = uri.ToString().TrimEnd('/');
        ClientInstance.BaseAddress = new Uri(BaseUrl + "/");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new ClientSettings(BaseUrl)));
    }

    public static void ApplySession(string? accessToken) => ClientInstance.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken) ? null : new AuthenticationHeaderValue("Bearer", accessToken);

    public static void SaveDeviceIdentity(Guid deviceId, Guid storeId, Guid registerId, string deviceToken)
    {
        DeviceId = deviceId; StoreId = storeId; RegisterId = registerId;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var protectedToken = Convert.ToBase64String(ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(deviceToken), null, DataProtectionScope.CurrentUser));
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new ClientSettings(BaseUrl, deviceId, storeId, registerId, protectedToken)));
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SettingsPath));
                if (settings is not null && Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri))
                {
                    BaseUrl = settings.BaseUrl.TrimEnd('/');
                    DeviceId = settings.DeviceId; StoreId = settings.StoreId; RegisterId = settings.RegisterId;
                    ClientInstance.BaseAddress = new Uri(BaseUrl + "/");
                    return;
                }
            }
        }
        catch (IOException) { }
        ClientInstance.BaseAddress = new Uri(BaseUrl + "/");
    }

    private sealed record ClientSettings(string BaseUrl, Guid? DeviceId = null, Guid? StoreId = null, Guid? RegisterId = null, string? DeviceTokenProtected = null);
}
