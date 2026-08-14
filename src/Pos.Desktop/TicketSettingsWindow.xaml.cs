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
        var columns = width == 80 ? 40 : 32;
        var rule = new string('-', columns);
        var builder = new StringBuilder();
        builder.AppendLine(Center("MI TIENDA", columns));
        builder.AppendLine(Center("RFC: XAXX010101000", columns));
        if (!string.IsNullOrWhiteSpace(HeaderBox?.Text)) builder.AppendLine(Center(HeaderBox.Text.Trim().ReplaceLineEndings(" "), columns));
        builder.AppendLine(rule);
        builder.AppendLine(Center("COMPROBANTE DE VENTA", columns));
        builder.AppendLine("FECHA: 14/08/2026 10:30:00");
        builder.AppendLine("CAJA: CAJA PRINCIPAL");
        builder.AppendLine("CAJERO: ADMINISTRADOR");
        builder.AppendLine("TURNO: 12AB34CD");
        builder.AppendLine("VENTA: 00000124");
        builder.AppendLine(rule);
        builder.AppendLine(width == 80 ? "CANT DESCRIPCION       PRECIO IMPORTE" : "CANT DESCRIP. PRECIO IMPORTE");
        builder.AppendLine(width == 80 ? "   2 PRODUCTO EJEMPLO  $20.00  $40.00" : "    2 PRODUCTO $20.00  $40.00");
        builder.AppendLine(width == 80 ? "   1 SEGUNDO PRODUCTO  $25.50  $25.50" : "    1 SEGUNDO  $25.50  $25.50");
        builder.AppendLine(rule);
        builder.AppendLine("ARTICULOS: 3");
        builder.AppendLine(AmountLine("SUBTOTAL", 65.50m, columns));
        builder.AppendLine(AmountLine("TOTAL", 65.50m, columns));
        builder.AppendLine(AmountLine("EFECTIVO", 65.50m, columns));
        builder.AppendLine(AmountLine("RECIBIDO", 100m, columns));
        builder.AppendLine(AmountLine("CAMBIO", 34.50m, columns));
        builder.AppendLine(rule);
        builder.AppendLine();
        builder.AppendLine(Center(string.IsNullOrWhiteSpace(FooterBox?.Text) ? "GRACIAS POR SU COMPRA" : FooterBox.Text.Trim().ReplaceLineEndings(" "), columns));
        builder.AppendLine(Center("CONSERVE ESTE COMPROBANTE", columns));
        PreviewText.Text = builder.ToString();
    }

    private static string Center(string value, int width) => value.Length >= width ? value[..width] : value.PadLeft(value.Length + ((width - value.Length) / 2));
    private static string AmountLine(string label, decimal amount, int width)
    {
        var value = $"${amount:0.00}";
        return label + new string(' ', Math.Max(1, width - label.Length - value.Length)) + value;
    }

    private sealed record TicketSettings(string TicketHeader, string TicketFooter, int TicketWidthMm);
}
