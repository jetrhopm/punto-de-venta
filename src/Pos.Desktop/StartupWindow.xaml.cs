using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class StartupWindow : Window
{
    private const string PostgreSqlServiceName = "PuntoDeVentaPostgreSQL";
    private const string ApiServiceName = "PuntoDeVentaApi";
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PuntoDeVenta",
        "logs",
        "startup-check.log");

    private bool _isChecking;
    private Process? _fallbackApiProcess;

    public StartupWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {version}";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await RunStartupCheckAsync();

    private async void OnRetryClick(object sender, RoutedEventArgs e) => await RunStartupCheckAsync();

    private async void OnConfigureClick(object sender, RoutedEventArgs e)
    {
        if (_isChecking) return;
        var window = new ServerConnectionWindow { Owner = this };
        if (window.ShowDialog() == true) await RunStartupCheckAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => System.Windows.Application.Current.Shutdown();

    private async Task RunStartupCheckAsync()
    {
        if (_isChecking) return;
        _isChecking = true;
        ErrorPanel.Visibility = Visibility.Collapsed;

        try
        {
            SetStatus("Revisando datos de la tienda...", 10, "Revisando", "Pendiente", "Pendiente");
            var available = await CheckApiAsync();

            if (!available)
            {
                SetStatus(IsLocalApi() ? "JetVenta esta intentando iniciar sus servicios locales..." : "JetVenta esta intentando conectar con el servidor configurado...", 25, "Preparando", "Recuperando", "Pendiente");
                await TryStartLocalServicesAsync();
                available = await WaitForApiAsync();
            }

            if (!available)
            {
                SetStatus(IsLocalApi() ? "No se pudo iniciar JetVenta. Revisa los servicios locales o configura otra conexión." : "No se pudo conectar con el servidor. Revisa la IP, el puerto y la red local.", 100, "Sin conexión", "Revisar", "Pendiente");
                ErrorPanel.Visibility = Visibility.Visible;
                return;
            }

            SetStatus("Datos listos. Revisando tienda y caja...", 72, "Listo", "Listo", "Revisando");
            var setup = await ReadSetupStatusAsync();
            if (setup is null)
            {
                SetStatus("La base de datos no responde. JetVenta intentara recuperar sus servicios...", 78, "Revisando", "Recuperando", "Pendiente");
                await TryStartLocalServicesAsync();
                available = await WaitForApiAsync();
                setup = available ? await ReadSetupStatusAsync() : null;
                if (setup is null)
                {
                    SetStatus(IsLocalApi() ? "Los servicios locales no respondieron. Puedes configurar otra conexión o reintentar." : "El servidor configurado no respondió. Puedes corregir la conexión o reintentar.", 100, "Sin respuesta", "Revisar", "Pendiente");
                    ErrorPanel.Visibility = Visibility.Visible;
                    return;
                }
            }
            if (!string.IsNullOrWhiteSpace(setup?.StoreName)) StoreNameText.Text = setup.StoreName;
            StoreStatusText.Text = setup?.Configured == true ? "Lista" : "Por configurar";

            SetStatus("Todo listo. Abriendo inicio de sesion...", 100, "Listo", "Listo", StoreStatusText.Text);
            await Task.Delay(450);

            var login = new LoginWindow();
            System.Windows.Application.Current.MainWindow = login;
            login.Show();
            Close();
        }
        catch (Exception exception)
        {
            Log(exception.ToString());
            SetStatus("No se pudo preparar JetVenta. Puedes configurar la conexión o intentar de nuevo.", 100, "Revisar", "Revisar", "Pendiente");
            ErrorPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            _isChecking = false;
        }
    }

    private static async Task<bool> CheckApiAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await ApiClient.Client.GetAsync("health", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> WaitForApiAsync()
    {
        for (var attempt = 1; attempt <= 24; attempt++)
        {
            SetStatus($"Preparando JetVenta... intento {attempt} de 24", 25 + attempt * 2, "Preparando", "Encendiendo", "Pendiente");
            if (await CheckApiAsync()) return true;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return false;
    }

    private static async Task<SetupStatus?> ReadSetupStatusAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var response = await ApiClient.Client.GetAsync("api/setup/status", timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<SetupStatus>(cancellationToken: timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task TryStartLocalServicesAsync()
    {
        if (!IsLocalApi())
        {
            Log($"No se iniciaron servicios locales porque el servidor configurado es remoto: {ApiClient.BaseUrl}");
            return;
        }

        var developmentRoot = FindDevelopmentRoot();
        if (developmentRoot is not null)
        {
            await TryStartDevelopmentServicesAsync(developmentRoot);
            return;
        }

        await TryRunAsync("sc.exe", $"start {PostgreSqlServiceName}");
        var postgresPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "postgresql", "pgsql", "bin", "pg_ctl.exe"));
        var postgresData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "postgresql", "data");
        if (File.Exists(postgresPath) && Directory.Exists(postgresData))
        {
            await TryRunAsync(postgresPath, $"-D \"{postgresData}\" -o \"-p 5432 -c listen_addresses=127.0.0.1\" -w start");
        }
        await Task.Delay(TimeSpan.FromSeconds(2));
        await TryRunAsync("sc.exe", $"start {ApiServiceName}");
        await Task.Delay(TimeSpan.FromSeconds(2));

        if (await CheckApiAsync()) return;

        // Production normally uses Windows services. This fallback also recovers an API
        // that was copied with the application but whose service registration is missing.
        var apiPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "api", "Pos.Api.exe"));
        var connectionFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PuntoDeVenta", "config", "connection.bin");
        if (!File.Exists(apiPath))
        {
            apiPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "api", "Pos.Api.exe"));
        }
        if (!File.Exists(apiPath))
        {
            Log($"No se encontró la API para recuperación directa. Ruta revisada: {apiPath}");
            return;
        }

        try
        {
            _fallbackApiProcess = Process.Start(new ProcessStartInfo
            {
                FileName = apiPath,
                Arguments = $"--Pos:ConnectionFile=\"{connectionFile}\" --Pos:Urls=http://127.0.0.1:5000",
                WorkingDirectory = Path.GetDirectoryName(apiPath)!,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Log($"API iniciada directamente para recuperación: {apiPath}");
        }
        catch (Exception exception)
        {
            Log($"No se pudo iniciar directamente la API: {exception}");
        }
    }

    private async Task TryStartDevelopmentServicesAsync(string root)
    {
        var settingsPath = Path.Combine(root, ".postgres", "development-settings.json");
        var dataPath = Path.Combine(root, ".postgres", "data");
        var logPath = Path.Combine(root, ".postgres", "logs", "postgresql.log");
        var toolsPath = Path.Combine(root, ".tools");
        var port = ReadDevelopmentPort(settingsPath);
        var pgCtlPath = Directory.Exists(toolsPath)
            ? Directory.EnumerateFiles(toolsPath, "pg_ctl.exe", SearchOption.AllDirectories).FirstOrDefault()
            : null;

        if (!await IsTcpPortOpenAsync(port) && !string.IsNullOrWhiteSpace(pgCtlPath) && Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await TryRunAsync(
                pgCtlPath,
                $"-D \"{dataPath}\" -l \"{logPath}\" -o \"-p {port} -c listen_addresses=127.0.0.1 -c fsync=on -c synchronous_commit=on -c full_page_writes=on\" -w start");
        }

        if (await CheckApiAsync()) return;

        var dotnetPath = Path.Combine(root, ".tools", "dotnet", "dotnet.exe");
        var apiAssembly = Path.Combine(root, "src", "Pos.Api", "bin", "Debug", "net10.0", "Pos.Api.dll");
        var apiProject = Path.Combine(root, "src", "Pos.Api", "Pos.Api.csproj");

        try
        {
            var startInfo = File.Exists(dotnetPath) && File.Exists(apiAssembly)
                ? new ProcessStartInfo
                {
                    FileName = dotnetPath,
                    Arguments = $"\"{apiAssembly}\"",
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
                : new ProcessStartInfo
                {
                    FileName = dotnetPath,
                    Arguments = $"run --project \"{apiProject}\" --no-launch-profile --no-restore",
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

            if (!File.Exists(startInfo.FileName))
            {
                Log($"No se encontro el ejecutable para iniciar la API de desarrollo: {startInfo.FileName}");
                return;
            }

            _fallbackApiProcess = Process.Start(startInfo);
            Log($"API de desarrollo iniciada desde: {startInfo.FileName}");
        }
        catch (Exception exception)
        {
            Log($"No se pudo iniciar la API de desarrollo: {exception}");
        }
    }

    private static string? FindDevelopmentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PuntoDeVenta.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "Pos.Api", "Pos.Api.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static int ReadDevelopmentPort(string settingsPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return document.RootElement.TryGetProperty("Port", out var port) ? port.GetInt32() : 55432;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            Log($"No se pudo leer el puerto de desarrollo; se usara 55432. {exception.Message}");
            return 55432;
        }
    }

    private static async Task<bool> IsTcpPortOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
            await client.ConnectAsync("127.0.0.1", port, timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static bool IsLocalApi()
    {
        if (!Uri.TryCreate(ApiClient.BaseUrl, UriKind.Absolute, out var uri)) return true;
        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               System.Net.IPAddress.TryParse(uri.Host, out var address) && System.Net.IPAddress.IsLoopback(address);
    }

    private static async Task TryRunAsync(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return;
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Log($"{fileName} {arguments} => {process.ExitCode}{Environment.NewLine}{output}{error}");
        }
        catch (Exception exception)
        {
            Log(exception.ToString());
        }
    }

    private void SetStatus(string message, int progress, string data, string service, string store)
    {
        CurrentStatusText.Text = message;
        StartupProgress.Value = Math.Clamp(progress, 0, 100);
        DataStatusText.Text = data;
        ServiceStatusText.Text = service;
        StoreStatusText.Text = store;
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // Startup diagnostics must never block opening the app.
        }
    }

    private sealed record SetupStatus(bool Configured, string? StoreName);
}
