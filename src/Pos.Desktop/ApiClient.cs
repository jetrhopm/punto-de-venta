using System.Net.Http.Headers;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pos.Desktop;

public static class ApiClient
{
    private static HttpClient ClientInstance = CreateClient("http://127.0.0.1:5000");
    // Peripherals and the paired register belong to the Windows computer, not to
    // the Windows profile that happens to open JetVenta.
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "client", "machine-settings.json");
    private static readonly string LegacySettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuntoDeVenta", "client-settings.json");

    public static HttpClient Client => ClientInstance;
    public static string BaseUrl { get; private set; } = "http://127.0.0.1:5000";
    public static Guid? DeviceId { get; private set; }
    public static Guid? StoreId { get; private set; }
    public static Guid? RegisterId { get; private set; }
    public static string? PrinterName { get; private set; }
    public static bool PrintingEnabled { get; private set; }
    public static bool IsTicketPrintingAvailable => PrintingEnabled && !string.IsNullOrWhiteSpace(PrinterName);
    public static string PrinterFontFamily { get; private set; } = "Consolas";
    public static double PrinterFontSize { get; private set; } = 9d;
    public static bool UseNormalTotals { get; private set; }
    public static int PrinterTicketWidthMm { get; private set; } = 80;
    public static BarcodeScannerProfile BarcodeScanner { get; private set; } = BarcodeScannerProfile.Default;
    public static CashDrawerProfile CashDrawer { get; private set; } = CashDrawerProfile.Default;
    public static ScaleProfile Scale { get; private set; } = ScaleProfile.Default;
    private static string? DeviceToken { get; set; }

    static ApiClient() => Load();

    public static void SetServer(string host, int port, bool persist = true)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("El servidor es obligatorio.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var value = host.Trim();
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = $"http://{value}";
        var uri = new UriBuilder(value) { Port = port }.Uri;
        BaseUrl = uri.ToString().TrimEnd('/');
        ReplaceClient(BaseUrl);
        if (persist) SaveSettings();
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
        DeviceId = deviceId; StoreId = storeId; RegisterId = registerId; DeviceToken = deviceToken;
        var protectedToken = ProtectForMachine(deviceToken);
        SaveSettings(protectedToken);
        ApplyDeviceIdentity(ClientInstance);
    }

    public static void SetPrinterProfile(string? printerName, string fontFamily, double fontSize, bool useNormalTotals, int widthMm, bool? printingEnabled = null)
    {
        PrinterName = string.IsNullOrWhiteSpace(printerName) ? null : printerName.Trim();
        PrintingEnabled = printingEnabled ?? !string.IsNullOrWhiteSpace(PrinterName);
        PrinterFontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Consolas" : fontFamily.Trim();
        PrinterFontSize = fontSize is >= 6d and <= 24d ? fontSize : 9d;
        UseNormalTotals = useNormalTotals;
        PrinterTicketWidthMm = widthMm == 58 ? 58 : 80;
        SaveSettings();
    }

    public static void SetPrinter(string? printerName) => SetPrinterProfile(printerName, PrinterFontFamily, PrinterFontSize, UseNormalTotals, PrinterTicketWidthMm);

    public static void SetPrinterTicketWidth(int widthMm) => SetPrinterProfile(PrinterName, PrinterFontFamily, PrinterFontSize, UseNormalTotals, widthMm);

    public static void SetBarcodeScannerProfile(BarcodeScannerProfile profile)
    {
        BarcodeScanner = profile.Normalize();
        SaveSettings();
    }

    public static void SetCashDrawerProfile(CashDrawerProfile profile)
    {
        CashDrawer = profile.Normalize();
        SaveSettings();
    }

    public static void SetScaleProfile(ScaleProfile profile)
    {
        Scale = profile.Normalize();
        SaveSettings();
    }

    private static void SaveSettings(string? protectedToken = null)
    {
        EnsureSettingsDirectory();
        var currentToken = protectedToken;
        if (currentToken is null && File.Exists(SettingsPath))
        {
            try { currentToken = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(SettingsPath))?.DeviceTokenProtected; }
            catch (JsonException) { }
        }
        WriteSettings(new ClientSettings(BaseUrl, DeviceId, StoreId, RegisterId, currentToken, PrinterName, PrinterFontFamily, PrinterFontSize, UseNormalTotals, PrinterTicketWidthMm, BarcodeScanner, PrintingEnabled, 3, CashDrawer, Scale));
    }

    private static void Load()
    {
        try
        {
            var settings = ReadSettings(SettingsPath) ?? MigrateLegacySettings();
            if (settings is not null && Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
            {
                BaseUrl = settings.BaseUrl.TrimEnd('/');
                DeviceId = settings.DeviceId; StoreId = settings.StoreId; RegisterId = settings.RegisterId;
                DeviceToken = UnprotectForMachine(settings.DeviceTokenProtected);
                if (DeviceId is not null && string.IsNullOrWhiteSpace(DeviceToken))
                {
                    DeviceId = null;
                    StoreId = null;
                    RegisterId = null;
                }
                PrinterName = settings.PrinterName;
                PrintingEnabled = settings.PrintingEnabled ?? !string.IsNullOrWhiteSpace(settings.PrinterName);
                PrinterFontFamily = string.IsNullOrWhiteSpace(settings.PrinterFontFamily) ? "Consolas" : settings.PrinterFontFamily;
                PrinterFontSize = settings.PrinterFontSize is >= 6d and <= 24d ? settings.PrinterFontSize : 9d;
                UseNormalTotals = settings.UseNormalTotals;
                PrinterTicketWidthMm = settings.PrinterTicketWidthMm == 58 ? 58 : 80;
                BarcodeScanner = (settings.BarcodeScanner ?? BarcodeScannerProfile.Default).Normalize();
                CashDrawer = (settings.CashDrawer ?? CashDrawerProfile.Default).Normalize();
                Scale = (settings.Scale ?? ScaleProfile.Default).Normalize();
                ReplaceClient(BaseUrl);
                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        ReplaceClient(BaseUrl);
    }

    private static ClientSettings? ReadSettings(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(path)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static ClientSettings? MigrateLegacySettings()
    {
        var legacy = ReadSettings(LegacySettingsPath);
        if (legacy is null) return null;

        var migrated = legacy with { SettingsVersion = 2 };
        if (!string.IsNullOrWhiteSpace(legacy.DeviceTokenProtected))
        {
            try
            {
                var token = System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(legacy.DeviceTokenProtected), null, DataProtectionScope.CurrentUser));
                migrated = migrated with { DeviceTokenProtected = ProtectForMachine(token) };
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                // A different Windows profile created the legacy file. Keep its
                // peripheral settings, but force a future pairing instead of
                // persisting an unusable device identity.
                migrated = migrated with { DeviceId = null, StoreId = null, RegisterId = null, DeviceTokenProtected = null };
            }
        }

        try
        {
            EnsureSettingsDirectory();
            WriteSettings(migrated);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return migrated;
    }

    private static string ProtectForMachine(string token) => Convert.ToBase64String(ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(token), null, DataProtectionScope.LocalMachine));

    private static string? UnprotectForMachine(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken)) return null;
        try
        {
            return System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), null, DataProtectionScope.LocalMachine));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException) { return null; }
    }

    private static void EnsureSettingsDirectory() => Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

    private static void WriteSettings(ClientSettings settings)
    {
        var temporaryPath = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
        File.Move(temporaryPath, SettingsPath, true);
    }

    private static HttpClient CreateClient(string baseUrl) => new()
    {
        BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static void ReplaceClient(string baseUrl)
    {
        var previous = ClientInstance;
        var replacement = CreateClient(baseUrl);
        var authorization = previous.DefaultRequestHeaders.Authorization;
        if (authorization is not null) replacement.DefaultRequestHeaders.Authorization = authorization;
        ApplyDeviceIdentity(replacement);
        ClientInstance = replacement;
        previous.Dispose();
    }

    private static void ApplyDeviceIdentity(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-JetVenta-Device-Token");
        if (!string.IsNullOrWhiteSpace(DeviceToken)) client.DefaultRequestHeaders.TryAddWithoutValidation("X-JetVenta-Device-Token", DeviceToken);
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
        int PrinterTicketWidthMm = 80,
        BarcodeScannerProfile? BarcodeScanner = null,
        bool? PrintingEnabled = null,
        int SettingsVersion = 1,
        CashDrawerProfile? CashDrawer = null,
        ScaleProfile? Scale = null);
}

public enum BarcodeScannerMode { Keyboard, Serial, Disabled }

public sealed record BarcodeScannerProfile(BarcodeScannerMode Mode, string? PortName, int BaudRate, string Terminator)
{
    public static BarcodeScannerProfile Default { get; } = new(BarcodeScannerMode.Keyboard, null, 9600, "CRLF");

    public BarcodeScannerProfile Normalize() => new(
        Enum.IsDefined(Mode) ? Mode : BarcodeScannerMode.Keyboard,
        string.IsNullOrWhiteSpace(PortName) ? null : PortName.Trim().ToUpperInvariant(),
        BaudRate is >= 1200 and <= 115200 ? BaudRate : 9600,
        Terminator is "CR" or "LF" or "CRLF" ? Terminator : "CRLF");
}

public sealed record CashDrawerProfile(bool Enabled, string? PrinterName, string Model, string Port)
{
    public static CashDrawerProfile Default { get; } = new(false, null, "PrinterPulse", "USB");

    public CashDrawerProfile Normalize() => new(
        Enabled,
        string.IsNullOrWhiteSpace(PrinterName) ? null : PrinterName.Trim(),
        Model is "PrinterPulse" or "EpsonDrawer1" or "EpsonDrawer2" or "StarDrawer1" or "StarDrawer2" or "Generic" ? Model : "PrinterPulse",
        Port is "USB" or "LPT1" or "LPT2" or "LPT3" or "COM1" or "COM2" or "COM3" or "COM4" ? Port : "USB");
}

public sealed record ScaleProfile(bool Enabled, string? Port, int BaudRate, string Parity, int DataBits, string StopBits, string Terminator, string Unit, int ReadTimeoutMs)
{
    public static ScaleProfile Default { get; } = new(false, null, 9600, "None", 8, "One", "CRLF", "Kilogramo", 1500);

    public ScaleProfile Normalize() => new(
        Enabled,
        string.IsNullOrWhiteSpace(Port) ? null : Port.Trim().ToUpperInvariant(),
        BaudRate is 1200 or 2400 or 4800 or 9600 or 19200 or 38400 or 57600 or 115200 ? BaudRate : 9600,
        Parity is "None" or "Even" or "Odd" ? Parity : "None",
        DataBits is 7 or 8 ? DataBits : 8,
        StopBits is "One" or "Two" ? StopBits : "One",
        Terminator is "CR" or "LF" or "CRLF" ? Terminator : "CRLF",
        Unit is "Kilogramo" or "Gramo" or "Libra" ? Unit : "Kilogramo",
        ReadTimeoutMs is >= 200 and <= 5000 ? ReadTimeoutMs : 1500);
}
