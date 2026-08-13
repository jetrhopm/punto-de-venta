using Microsoft.Win32;
using System.Net.Http.Json;
using System.IO;
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
            StatusText.Text = "Copia externa guardada. Conserva también el SHA-256 mostrado en la lista.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar la copia: {exception.Message}"; }
    }

    private static string FormatSize(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576d:0.0} MB" : $"{bytes / 1024d:0.0} KB";
    private sealed record BackupRow(string FileName, long SizeBytes, string Sha256, DateTimeOffset CreatedAtUtc) { public string SizeDisplay { get; init; } = string.Empty; }
}
