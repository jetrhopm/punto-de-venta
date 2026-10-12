using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

// Conectar Google Drive, ver el último respaldo, activar la contraseña opcional y descargar
// un respaldo para restaurarlo con el proceso de restauración que ya existe.
public partial class GoogleDriveBackupWindow : Window
{
    private GoogleDriveStatusDto? _status;
    private CancellationTokenSource? _connectPolling;

    public bool DownloadedBackup { get; private set; }

    public GoogleDriveBackupWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync(loadFiles: true);
    }

    private async Task LoadAsync(bool loadFiles)
    {
        try
        {
            _status = await ApiClient.Client.GetFromJsonAsync<GoogleDriveStatusDto>("api/backups/google-drive/status");
            if (_status is null) return;
            Render(_status);
            if (loadFiles && _status.Connected && _status.CanManage) await LoadFilesAsync();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo consultar Google Drive"); }
    }

    private void Render(GoogleDriveStatusDto status)
    {
        var manageable = status.IsPrincipal && status.CanManage;
        ConnectButton.Visibility = Visibility.Visible;
        ConnectButton.IsEnabled = manageable && status.Configured;
        DisconnectButton.Visibility = status.Connected && status.CanManage ? Visibility.Visible : Visibility.Collapsed;
        BackupPanel.Visibility = status.Connected ? Visibility.Visible : Visibility.Collapsed;
        RestorePanel.Visibility = status.Connected && status.CanManage ? Visibility.Visible : Visibility.Collapsed;
        UploadNowButton.IsEnabled = manageable;
        PasswordCheck.IsEnabled = status.CanManage;
        DownloadButton.IsEnabled = manageable;

        if (!status.Configured)
        {
            AccountTitle.Text = "Google Drive no está disponible todavía";
            AccountDetail.Text = "Esta versión de JetVenta aún no tiene configurada la conexión con Google. Contacta a soporte de JetVenta.";
        }
        else if (!status.Connected)
        {
            AccountTitle.Text = "Guarda tus respaldos en Google Drive";
            AccountDetail.Text = !status.IsPrincipal
                ? "Los respaldos en la nube se configuran desde la caja principal (la computadora donde está la base de datos)."
                : "Inicia sesión con tu cuenta de Google y pulsa Permitir. Google puede mostrar que la app no está verificada; es normal en esta etapa.";
            ConnectButton.Content = "Conectar Google Drive";
        }
        else
        {
            AccountTitle.Text = status.NeedsReconnect ? "Vuelve a conectar Google Drive" : "✓ Conectado a Google Drive";
            AccountDetail.Text = status.NeedsReconnect
                ? "Google ya no acepta el permiso de JetVenta. Pulsa Volver a conectar con la misma cuenta."
                : string.IsNullOrWhiteSpace(status.Email) ? "Tus respaldos se guardan en la carpeta JetVenta Respaldos." : $"{status.Email} · carpeta JetVenta Respaldos";
            ConnectButton.Content = status.NeedsReconnect ? "Volver a conectar" : "Cambiar de cuenta";
        }

        LastUploadText.Text = status.LastUpload is { } last
            ? $"Último respaldo en la nube: {last.UploadedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} · {FormatSize(last.SizeBytes)}{(last.Encrypted ? " · con contraseña" : string.Empty)} · {(last.Verified ? "verificado" : "sin verificar")}"
            : "Todavía no se ha subido ningún respaldo. Se subirá al cerrar JetVenta.";
        PendingText.Text = status.PendingCount > 0
            ? $"{status.PendingCount} respaldo(s) pendiente(s) de subir. {status.LastError}"
            : string.IsNullOrWhiteSpace(status.LastError) ? "Se guarda uno cada vez que cierras JetVenta; de días anteriores se conserva el último de cada día durante 7 días." : status.LastError;

        PasswordCheck.IsChecked = status.PasswordEnabled;
        PasswordPanel.Visibility = Visibility.Collapsed;
    }

    // ---------- Conectar ----------

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        _connectPolling?.Cancel();
        _connectPolling = new CancellationTokenSource();
        var cancellationToken = _connectPolling.Token;
        ConnectButton.IsEnabled = false;
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/backups/google-drive/connect", null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo abrir Google.");
                OperationFeedback.Show(this, "Google Drive", StatusText.Text, OperationResultKind.Error);
                return;
            }
            var start = await response.Content.ReadFromJsonAsync<ConnectStart>(cancellationToken);
            if (start is null) return;
            Process.Start(new ProcessStartInfo(start.Url) { UseShellExecute = true });
            StatusText.Text = "Se abrió Google en tu navegador. Elige tu cuenta y pulsa Permitir.";

            for (var attempt = 0; attempt < 300; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                var status = await ApiClient.Client.GetFromJsonAsync<GoogleDriveStatusDto>("api/backups/google-drive/status", cancellationToken);
                if (status is null || status.ConnectState == "Waiting") continue;
                if (status.ConnectState == "Completed")
                {
                    StatusText.Text = "Google Drive conectado. El respaldo se guardará cada vez que cierres JetVenta.";
                    await LoadAsync(loadFiles: true);
                    OperationFeedback.Show(this, "Google Drive conectado", StatusText.Text, OperationResultKind.Success);
                }
                else
                {
                    StatusText.Text = string.IsNullOrWhiteSpace(status.ConnectMessage) ? "La conexión no se completó." : status.ConnectMessage;
                    OperationFeedback.Show(this, "Google Drive", StatusText.Text, OperationResultKind.Warning);
                }
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo conectar Google Drive");
            OperationFeedback.Show(this, "Google Drive", StatusText.Text, OperationResultKind.Error);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && _status is not null) Render(_status);
        }
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "JetVenta dejará de guardar respaldos en tu Google Drive.\n\nLos respaldos que ya están en tu Drive no se borran. ¿Deseas desconectar?",
            "Desconectar Google Drive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/backups/google-drive/disconnect", null);
            StatusText.Text = response.IsSuccessStatusCode ? "Google Drive desconectado." : await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo desconectar.");
            FilesGrid.ItemsSource = null;
            await LoadAsync(loadFiles: false);
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo desconectar"); }
    }

    // ---------- Subir y contraseña ----------

    private async void OnUploadNowClick(object sender, RoutedEventArgs e)
    {
        UploadNowButton.IsEnabled = false;
        StatusText.Text = "Creando y subiendo respaldo...";
        try
        {
            using var client = GoogleDriveBackupHooks.CreateLongRunningClient(TimeSpan.FromMinutes(5));
            using var response = await client.PostAsync("api/backups/google-drive/upload-now", null);
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo subir el respaldo.");
                OperationFeedback.Show(this, "Google Drive", StatusText.Text, OperationResultKind.Error);
            }
            else
            {
                var result = await response.Content.ReadFromJsonAsync<GoogleDriveCloseDto>();
                StatusText.Text = result?.Message ?? "Respaldo procesado.";
                OperationFeedback.Show(this, "Google Drive", StatusText.Text, result?.Uploaded == true ? OperationResultKind.Success : OperationResultKind.Warning);
            }
            await LoadAsync(loadFiles: true);
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo subir el respaldo"); }
        finally { UploadNowButton.IsEnabled = _status?.IsPrincipal == true && _status.CanManage; }
    }

    private async void OnPasswordCheckClick(object sender, RoutedEventArgs e)
    {
        if (PasswordCheck.IsChecked == true)
        {
            // Se activa al guardar la contraseña, no al marcar la casilla.
            PasswordPanel.Visibility = Visibility.Visible;
            PasswordBox.Focus();
            return;
        }

        PasswordPanel.Visibility = Visibility.Collapsed;
        if (_status?.PasswordEnabled != true) return;
        var confirmation = MessageBox.Show(
            "Los próximos respaldos se subirán sin contraseña. Los respaldos anteriores siguen necesitando la contraseña con que se crearon.\n\n¿Quitar la contraseña?",
            "Quitar contraseña", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) { PasswordCheck.IsChecked = true; return; }
        await SavePasswordAsync(enabled: false, password: null);
    }

    private async void OnSavePasswordClick(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Password.Length < 8) { StatusText.Text = "La contraseña debe tener al menos 8 caracteres."; return; }
        if (PasswordBox.Password != PasswordRepeatBox.Password) { StatusText.Text = "Las contraseñas no coinciden."; return; }
        var confirmation = MessageBox.Show(
            "Si pierdes esta contraseña no podrás abrir tus respaldos de Google Drive. Nadie, ni JetVenta, puede recuperarla.\n\nGuárdala en un lugar seguro. ¿Entiendes y deseas activarla?",
            "Proteger respaldos con contraseña", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        await SavePasswordAsync(enabled: true, password: PasswordBox.Password);
    }

    private async Task SavePasswordAsync(bool enabled, string? password)
    {
        try
        {
            using var response = await ApiClient.Client.PutAsJsonAsync("api/backups/google-drive/password", new { enabled, password });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo guardar la contraseña.");
                return;
            }
            PasswordBox.Clear();
            PasswordRepeatBox.Clear();
            StatusText.Text = enabled ? "Los próximos respaldos se subirán protegidos con contraseña." : "Los próximos respaldos se subirán sin contraseña.";
            await LoadAsync(loadFiles: false);
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar la contraseña"); }
    }

    // ---------- Restaurar ----------

    private async void OnRefreshFilesClick(object sender, RoutedEventArgs e) => await LoadFilesAsync();

    private async Task LoadFilesAsync()
    {
        try
        {
            using var client = GoogleDriveBackupHooks.CreateLongRunningClient(TimeSpan.FromSeconds(60));
            using var response = await client.GetAsync("api/backups/google-drive/files");
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo consultar Google Drive.");
                return;
            }
            FilesGrid.ItemsSource = await response.Content.ReadFromJsonAsync<List<GoogleDriveFileDto>>() ?? [];
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo consultar Google Drive"); }
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not GoogleDriveFileDto file) { StatusText.Text = "Elige un respaldo de la lista."; return; }
        if (file.Encrypted && string.IsNullOrEmpty(RestorePasswordBox.Password) && _status?.PasswordEnabled != true)
        {
            StatusText.Text = "Este respaldo tiene contraseña. Escríbela para descargarlo.";
            return;
        }
        DownloadButton.IsEnabled = false;
        StatusText.Text = "Descargando y verificando respaldo...";
        try
        {
            using var client = GoogleDriveBackupHooks.CreateLongRunningClient(TimeSpan.FromMinutes(10));
            using var response = await client.PostAsJsonAsync("api/backups/google-drive/restore", new { fileId = file.Id, password = string.IsNullOrEmpty(RestorePasswordBox.Password) ? null : RestorePasswordBox.Password });
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo descargar el respaldo.");
                OperationFeedback.Show(this, "Restaurar desde Google Drive", StatusText.Text, OperationResultKind.Error);
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<GoogleDriveRestoreDto>();
            RestorePasswordBox.Clear();
            DownloadedBackup = true;
            StatusText.Text = $"Listo: el respaldo del {file.DateDisplay} ya está en esta computadora como {result?.FileName}.";
            OperationFeedback.Show(this, "Respaldo descargado", StatusText.Text + " Cierra esta ventana, selecciónalo en la lista de Respaldos y pulsa Cargar respaldo.", OperationResultKind.Success);
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo descargar el respaldo"); }
        finally { DownloadButton.IsEnabled = _status?.IsPrincipal == true && _status.CanManage; }
    }

    // ---------- General ----------

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _connectPolling?.Cancel();
        _connectPolling?.Dispose();
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private sealed record ConnectStart(string Url);
}
