using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

// Se muestra al cerrar JetVenta en la caja principal mientras se crea y sube el respaldo.
// "Omitir por hoy" corta la espera: el respaldo se sube la próxima vez que se abra JetVenta.
public partial class CloudBackupProgressWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private bool _done;

    public CloudBackupProgressWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        string title;
        string detail;
        try
        {
            using var client = GoogleDriveBackupHooks.CreateLongRunningClient(TimeSpan.FromSeconds(75));
            using var response = await client.PostAsync("api/backups/google-drive/on-close", null, _cancellation.Token);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GoogleDriveCloseDto>(_cancellation.Token);
                title = result?.Uploaded == true ? "Respaldo guardado en Google Drive" : "Respaldo pendiente";
                detail = result?.Message ?? "El respaldo se subirá la próxima vez que abras JetVenta.";
            }
            else
            {
                title = "Respaldo pendiente";
                detail = await ConfigurationFeedback.ReadErrorAsync(response, "El respaldo se subirá la próxima vez que abras JetVenta.");
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException)
        {
            title = "Respaldo pendiente";
            detail = "El respaldo se subirá la próxima vez que abras JetVenta.";
        }

        _done = true;
        Progress.IsIndeterminate = false;
        Progress.Value = 100;
        TitleText.Text = title;
        DetailText.Text = detail;
        SkipButton.IsEnabled = false;
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Close();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        SkipButton.IsEnabled = false;
        DetailText.Text = "Omitiendo... el respaldo se subirá la próxima vez que abras JetVenta.";
        _cancellation.Cancel();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_done)
        {
            // Cerrar con la X equivale a omitir.
            _cancellation.Cancel();
            e.Cancel = true;
        }
    }
}
