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
    public static string? PrinterName { get; private set; }
    public static string PrinterFontFamily { get; private set; } = "Consolas";
    public static double PrinterFontSize { get; private set; } = 9d;
    public static bool UseNormalTotals { get; private set; }
    public static int PrinterTicketWidthMm { get; private set; } = 80;

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
        SaveSettings();
    }

    public static void ApplySession(string? accessToken) => ClientInstance.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken) ? null : new AuthenticationHeaderValue("Bearer", accessToken);

    public static async Task<bool> WaitUntilAvailableAsync(Action<int, int>? reportAttempt = null, CancellationToken cancellationToken = default)
    {
        const int maximumAttempts = 15;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            reportAttempt?.Invoke(attempt, maximumAttempts);
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var response = await ClientInstance.GetAsync("health", requestTimeout.Token);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (HttpRequestException) { }

            if (attempt < maximumAttempts) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        return false;
    }

    public static void SaveDeviceIdentity(Guid deviceId, Guid storeId, Guid registerId, string deviceToken)
    {
        DeviceId = deviceId; StoreId = storeId; RegisterId = registerId;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var protectedToken = Convert.ToBase64String(ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(deviceToken), null, DataProtectionScope.CurrentUser));
        SaveSettings(protectedToken);
    }

    public static void SetPrinterProfile(string? printerName, string fontFamily, double fontSize, bool useNormalTotals, int widthMm)
    {
        PrinterName = string.IsNullOrWhiteSpace(printerName) ? null : printerName.Trim();
        PrinterFontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Consolas" : fontFamily.Trim();
        PrinterFontSize = fontSize is >= 6d and <= 24d ? fontSize : 9d;
        UseNormalTotals = useNormalTotals;
        PrinterTicketWidthMm = widthMm == 58 ? 58 : 80;
        SaveSettings();
    }

    public static void SetPrinter(string? printerName) => SetPrinterProfile(printerName, PrinterFontFamily, PrinterFontSize, UseNormalTotals, PrinterTicketWidthMm);

    public static void SetPrinterTicketWidth(int widthMm) => SetPrinterProfile(PrinterName, PrinterFontFamily, PrinterFontSize, UseNormalTotals, widthMm);

    private static void SaveSettings(string? protectedToken = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var currentToken = protectedToken;
        if (currentToken is null && File.Exists(SettingsPath))
        {
            try { currentToken = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SettingsPath))?.DeviceTokenProtected; }
            catch (JsonException) { }
        }
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new ClientSettings(BaseUrl, DeviceId, StoreId, RegisterId, currentToken, PrinterName, PrinterFontFamily, PrinterFontSize, UseNormalTotals, PrinterTicketWidthMm)));
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
                    PrinterName = settings.PrinterName;
                    PrinterFontFamily = string.IsNullOrWhiteSpace(settings.PrinterFontFamily) ? "Consolas" : settings.PrinterFontFamily;
                    PrinterFontSize = settings.PrinterFontSize is >= 6d and <= 24d ? settings.PrinterFontSize : 9d;
                    UseNormalTotals = settings.UseNormalTotals;
                    PrinterTicketWidthMm = settings.PrinterTicketWidthMm == 58 ? 58 : 80;
                    ClientInstance.BaseAddress = new Uri(BaseUrl + "/");
                    return;
                }
            }
        }
        catch (IOException) { }
        ClientInstance.BaseAddress = new Uri(BaseUrl + "/");
    }

    private sealed record ClientSettings(
        string BaseUrl,
        Guid? DeviceId = null,
        Guid? StoreId = null,
        Guid? RegisterId = null,
        string? DeviceTokenProtected = null,
        string? PrinterName = null,
        string? PrinterFontFamily = null,
        double PrinterFontSize = 9d,
        bool UseNormalTotals = false,
        int PrinterTicketWidthMm = 80);
}
