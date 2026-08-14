using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class TicketSettingsWindow : Window
{
    private static HttpClient Client => ApiClient.Client;

    public TicketSettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SessionContext.AccessToken);
        try
        {
            var settings = await Client.GetFromJsonAsync<TicketSettings>("/api/ticket-settings");
            if (settings is not null)
            {
                HeaderBox.Text = settings.TicketHeader;
                FooterBox.Text = settings.TicketFooter;
                WidthBox.SelectedIndex = settings.TicketWidthMm == 58 ? 0 : 1;
            }
            UpdatePreview();
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo cargar la configuracion: {exception.Message}"; }
    }

    private void OnPreviewChanged(object sender, EventArgs e) => UpdatePreview();

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var width = (WidthBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!int.TryParse(width, out var widthMm)) { StatusText.Text = "Selecciona un ancho valido."; return; }
        try
        {
            using var response = await Client.PutAsJsonAsync("/api/ticket-settings", new { header = HeaderBox.Text, footer = FooterBox.Text, widthMm });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            StatusText.Text = "Formato y vista previa guardados correctamente.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar: {exception.Message}"; }
    }

    private void UpdatePreview()
    {
        if (PreviewText is null || TicketPaper is null) return;
        var width = (WidthBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() == "80" ? 80 : 58;
        TicketPaper.Width = width == 80 ? 300 : 240;
        var builder = new StringBuilder();
        builder.AppendLine(string.IsNullOrWhiteSpace(HeaderBox?.Text) ? "MI TIENDA" : HeaderBox.Text.Trim());
        builder.AppendLine(new string('-', width == 80 ? 36 : 28));
        builder.AppendLine("Folio: 000124");
        builder.AppendLine("14/08/2026  10:30");
        builder.AppendLine("Caja 1  Cajero: Administrador");
        builder.AppendLine(new string('-', width == 80 ? 36 : 28));
        builder.AppendLine("2  Producto de ejemplo   $40.00");
        builder.AppendLine("1  Segundo producto      $25.50");
        builder.AppendLine(new string('-', width == 80 ? 36 : 28));
        builder.AppendLine("TOTAL                 $65.50");
        builder.AppendLine("EFECTIVO             $100.00");
        builder.AppendLine("CAMBIO                $34.50");
        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(FooterBox?.Text) ? "Gracias por su compra" : FooterBox.Text.Trim());
        PreviewText.Text = builder.ToString();
    }

    private sealed record TicketSettings(string TicketHeader, string TicketFooter, int TicketWidthMm);
}