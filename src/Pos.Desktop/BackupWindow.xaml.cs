using Microsoft.Win32;
using System.Net.Http.Json;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;

namespace Pos.Desktop;

public partial class BackupWindow : Window
{
    public BackupWindow() { InitializeComponent(); Loaded += async (_, _) => await LoadAsync(); }

    private async Task LoadAsync()
    {
        try { BackupsGrid.ItemsSource = (await ApiClient.Client.GetFromJsonAsync<List<BackupRow>>("api/maintenance/backups") ?? []).Select(item => item with { SizeDisplay = FormatSize(item.SizeBytes) }).ToList(); }
        catch (Exception exception) { StatusText.Text = $"No se pudieron consultar los respaldos: {exception.Message}"; }
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Creando respaldo y calculando checksum...";
        try
        {
            using var response = await ApiClient.Client.PostAsync("api/maintenance/backups", null);
            StatusText.Text = response.IsSuccessStatusCode ? "Respaldo creado y verificado correctamente." : await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) await LoadAsync();
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo crear el respaldo: {exception.Message}"; }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (BackupsGrid.SelectedItem is not BackupRow backup) { StatusText.Text = "Selecciona un respaldo."; return; }
        var dialog = new SaveFileDialog { Title = "Guardar copia del respaldo", Filter = "Respaldo PostgreSQL (*.dump)|*.dump", FileName = backup.FileName, AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var response = await ApiClient.Client.GetAsync($"api/maintenance/backups/{Uri.EscapeDataString(backup.FileName)}");
            response.EnsureSuccessStatusCode();
            await File.WriteAllBytesAsync(dialog.FileName, await response.Content.ReadAsByteArrayAsync());
            await File.WriteAllTextAsync(dialog.FileName + ".sha256", backup.Sha256 + Environment.NewLine);
            StatusText.Text = "Copia externa y comprobante SHA-256 guardados correctamente.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar la copia: {exception.Message}"; }
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Seleccionar respaldo de JetVenta",
            Filter = "Respaldo PostgreSQL (*.dump)|*.dump",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        var backup = dialog.FileName;
        var checksum = backup + ".sha256";
        if (!File.Exists(checksum))
        {
            MessageBox.Show("No se encontró el archivo de verificación .dump.sha256 junto al respaldo. Selecciona ambos archivos en la misma carpeta.", "Respaldo incompleto", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusText.Text = "Verificando el respaldo antes de cargarlo...";
            var expected = (await File.ReadAllTextAsync(checksum)).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
            await using var stream = File.OpenRead(backup);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToUpperInvariant();
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "El respaldo no coincide con su comprobante SHA-256.";
                MessageBox.Show("El archivo está dañado o fue modificado. No se restauró ningún dato.", "Verificación fallida", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var confirm = MessageBox.Show(
                "JetVenta creará una copia preventiva y reemplazará la base actual con este respaldo. El programa se cerrará durante el proceso. ¿Deseas continuar?",
                "Confirmar restauración",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var script = FindRestoreScript();
            if (script is null)
            {
                MessageBox.Show("No se encontró el componente de restauración de JetVenta. Repara o actualiza la instalación.", "Restauración no disponible", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(script) ?? AppContext.BaseDirectory
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("-BackupFile");
            start.ArgumentList.Add(backup);
            start.ArgumentList.Add("-Approve");

            StatusText.Text = "Restaurando la tienda. No cierres Windows...";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar la restauración.");
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                MessageBox.Show("La tienda se restauró correctamente. JetVenta se cerrará para iniciar sesión con la información restaurada.", "Restauración terminada", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                StatusText.Text = "La restauración no terminó. No se eliminó el respaldo seleccionado.";
                MessageBox.Show("No se pudo completar la restauración. Revisa el diagnóstico de JetVenta o solicita ayuda al administrador.", "Restauración no completada", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "No se pudo iniciar la restauración.";
            MessageBox.Show($"No se pudo cargar el respaldo. Detalle: {exception.Message}", "Error de restauración", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? FindRestoreScript()
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "restore-production-backup.ps1") };
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "restore-production-backup.ps1"));
            candidates.Add(Path.Combine(directory.FullName, "scripts", "restore-production-backup.ps1"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string FormatSize(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576d:0.0} MB" : $"{bytes / 1024d:0.0} KB";
    private sealed record BackupRow(string FileName, long SizeBytes, string Sha256, DateTimeOffset CreatedAtUtc) { public string SizeDisplay { get; init; } = string.Empty; }
}
