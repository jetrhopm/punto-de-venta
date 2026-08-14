using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
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

    public StartupWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {version}";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await RunStartupCheckAsync();

    private async void OnRetryClick(object sender, RoutedEventArgs e) => await RunStartupCheckAsync();

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
                SetStatus("JetVenta esta iniciando sus servicios locales...", 25, "Preparando", "Encendiendo", "Pendiente");
                await TryStartLocalServicesAsync();
                available = await WaitForApiAsync();
            }

            if (!available)
            {
                SetStatus("No se pudo preparar JetVenta. Intenta de nuevo o reinicia la computadora principal.", 100, "Sin conexion", "Revisar", "Pendiente");
                ErrorPanel.Visibility = Visibility.Visible;
                return;
            }

            SetStatus("Datos listos. Revisando tienda y caja...", 72, "Listo", "Listo", "Revisando");
            var setup = await ReadSetupStatusAsync();
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
            SetStatus("No se pudo preparar JetVenta. Intenta de nuevo o reinicia la computadora principal.", 100, "Revisar", "Revisar", "Pendiente");
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

    private static async Task TryStartLocalServicesAsync()
    {
        await TryRunAsync("sc.exe", $"start {PostgreSqlServiceName}");
        await Task.Delay(TimeSpan.FromSeconds(2));
        await TryRunAsync("sc.exe", $"start {ApiServiceName}");
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
