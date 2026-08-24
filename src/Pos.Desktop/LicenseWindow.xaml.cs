using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pos.Desktop;

public partial class LicenseWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private bool _busy;
    private LicenseStatus? _currentStatus;
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public LicenseWindow()
    {
        InitializeComponent();
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        Loaded += async (_, _) => await LoadStatusAsync();
        Closed += (_, _) => _countdownTimer.Stop();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadStatusAsync();

    private void OnCopyRequestClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RequestCodeTextBox.Text)) return;
        Clipboard.SetDataObject(RequestCodeTextBox.Text, true);
        StatusMessageText.Text = "Código de solicitud copiado al portapapeles.";
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Seleccionar licencia de JetVenta", Filter = "Licencia JetVenta (*.jv)|*.jv|Todos los archivos (*.*)|*.*", Multiselect = false };
        if (picker.ShowDialog(this) != true) return;

        SelectedFileTextBox.Text = picker.FileName;
        try
        {
            SetBusy(true);
            var content = await File.ReadAllTextAsync(picker.FileName);
            using var response = await Client.PostAsJsonAsync("api/license/import", new { content });
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(ExtractMessage(detail) ?? "El archivo no pudo activarse en este equipo.");
            }

            await LoadStatusAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
        {
            SetError($"No se pudo cargar la licencia: {exception.Message}");
        }
        finally { SetBusy(false); }
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            SetBusy(true);
            var result = await Client.GetFromJsonAsync<LicenseStatus>("api/license/status");
            if (result is null) throw new InvalidOperationException("JetVenta no devolvió el estado de la licencia.");
            RequestCodeTextBox.Text = result.RequestCode;
            ApplyStatus(result);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            SetError("No se pudo consultar la licencia. Revisa que JetVenta esté conectado y vuelve a intentar.");
        }
        finally { SetBusy(false); }
    }

    private void ApplyStatus(LicenseStatus status)
    {
        _currentStatus = status;
        var active = status.IsActive;
        var trial = string.Equals(status.State, "trial", StringComparison.OrdinalIgnoreCase);
        StatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active && !trial ? "#EAF7EE" : "#FFF7E2"));
        StatusBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active && !trial ? "#9DD6B1" : "#E9C770"));
        StatusTitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active && !trial ? "#17663A" : "#744F00"));
        StatusTitleText.Text = trial ? "Modo de prueba activo" : active ? "Licencia activa" : "Licencia pendiente";
        StatusMessageText.Text = status.Message;
        UpdateCountdown();
        if (trial) _countdownTimer.Start(); else _countdownTimer.Stop();
    }

    private void UpdateCountdown()
    {
        var status = _currentStatus;
        if (status?.ExpiresAtUtc is null)
        {
            ExpirationText.Text = status?.IsActive == true ? "Vigencia: sin vencimiento." : string.Empty;
            return;
        }

        var remaining = status.ExpiresAtUtc.Value - DateTimeOffset.UtcNow;
        if (string.Equals(status.State, "trial", StringComparison.OrdinalIgnoreCase))
        {
            if (remaining <= TimeSpan.Zero)
            {
                _countdownTimer.Stop();
                ExpirationText.Text = "La prueba terminó. Carga una licencia para continuar.";
                return;
            }

            ExpirationText.Text = $"Tiempo restante: {FormatRemaining(remaining)}. Termina el {status.ExpiresAtUtc.Value.LocalDateTime:dd/MM/yyyy HH:mm:ss}.";
            return;
        }

        ExpirationText.Text = $"Vigencia hasta: {status.ExpiresAtUtc.Value.LocalDateTime:dd/MM/yyyy}.";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(1, (long)Math.Ceiling(remaining.TotalSeconds));
        var days = totalSeconds / 86400;
        var hours = totalSeconds % 86400 / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        if (days > 0) return $"{days} d {hours:00} h {minutes:00} min";
        if (hours > 0) return $"{hours} h {minutes:00} min {seconds:00} s";
        return $"{minutes} min {seconds:00} s";
    }

    private void SetError(string message)
    {
        StatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDEBEA"));
        StatusBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8BBB7"));
        StatusTitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A12F28"));
        StatusTitleText.Text = "No se pudo validar la licencia";
        StatusMessageText.Text = message;
        ExpirationText.Text = string.Empty;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ImportButton.IsEnabled = !busy;
    }

    private static string? ExtractMessage(string content)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.TryGetProperty("license", out var value) && value.GetArrayLength() > 0) return value[0].GetString();
            return document.RootElement.TryGetProperty("message", out var message) ? message.GetString() : null;
        }
        catch { return null; }
    }

    private sealed record LicenseStatus(bool IsActive, string State, string Message, string MachineFingerprint, string RequestCode, string? LicenseId, DateTimeOffset? ExpiresAtUtc, string? StoreName);
}
