using Pos.Printing;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;

namespace Pos.Desktop;

public partial class TicketSettingsWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private bool _loaded;

    public TicketSettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await Client.GetFromJsonAsync<TicketSettings>("/api/ticket-settings");
            if (settings is not null)
            {
                StoreNameBox.Text = settings.Name;
                LegalNameBox.Text = settings.LegalName;
                TaxIdBox.Text = settings.TaxId;
                AddressBox.Text = settings.Address;
                PhoneBox.Text = settings.Phone;
                HeaderBox.Text = settings.TicketHeader;
                FooterBox.Text = settings.TicketFooter;
                Width58Button.IsChecked = settings.TicketWidthMm == 58;
                Width80Button.IsChecked = settings.TicketWidthMm != 58;
            }
            _loaded = true;
            UpdatePreview();
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo cargar la configuración: {exception.Message}"; }
    }

    private void OnPreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_loaded) UpdatePreview();
    }

    private void OnPreviewChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loaded) UpdatePreview();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StoreNameBox.Text)) { StatusText.Text = "Escribe el nombre comercial que aparecerá en el ticket."; StoreNameBox.Focus(); return; }
        var widthMm = SelectedWidth;
        try
        {
            using var response = await Client.PutAsJsonAsync("/api/ticket-settings", new
            {
                header = HeaderBox.Text,
                footer = FooterBox.Text,
                widthMm,
                storeName = StoreNameBox.Text,
                legalName = LegalNameBox.Text,
                taxId = TaxIdBox.Text,
                address = AddressBox.Text,
                phone = PhoneBox.Text
            });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await response.Content.ReadAsStringAsync(); return; }
            ApiClient.SetPrinterTicketWidth(widthMm);
            StatusText.Text = "Diseño del ticket guardado. Las próximas ventas usarán estos datos.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo guardar: {exception.Message}"; }
    }

    private void OnPrintSampleClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiClient.PrinterName)) { StatusText.Text = "Primero selecciona una impresora en Configuración > Impresora."; return; }
        try
        {
            var profile = TicketWindowsPrinter.CurrentProfile with { WidthMm = SelectedWidth };
            TicketWindowsPrinter.Print(ApiClient.PrinterName, CreatePreviewData(), profile, "Muestra de ticket JetVenta");
            StatusText.Text = $"Ticket muestra enviado a {ApiClient.PrinterName}.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo imprimir la muestra: {exception.Message}"; }
    }

    private void UpdatePreview()
    {
        var width = SelectedWidth;
        PreviewWidthText.Text = $"{width} mm";
        var profile = TicketWindowsPrinter.CurrentProfile with { WidthMm = width };
        TicketPreviewHost.Content = TicketWindowsPrinter.CreateTicketVisual(CreatePreviewData(), profile);
    }

    private TicketPdfData CreatePreviewData()
    {
        var sample = TicketWindowsPrinter.CreateSample(SelectedWidth);
        return sample with
        {
            StoreName = string.IsNullOrWhiteSpace(StoreNameBox.Text) ? "MI TIENDA" : StoreNameBox.Text.Trim(),
            LegalName = LegalNameBox.Text.Trim(),
            TaxId = TaxIdBox.Text.Trim(),
            Address = AddressBox.Text.Trim(),
            Phone = PhoneBox.Text.Trim(),
            Header = HeaderBox.Text.Trim(),
            Footer = FooterBox.Text.Trim(),
            WidthMm = SelectedWidth
        };
    }

    private int SelectedWidth => Width58Button.IsChecked == true ? 58 : 80;

    private sealed record TicketSettings(string Name, string LegalName, string TaxId, string Address, string Phone, string TicketHeader, string TicketFooter, int TicketWidthMm);
}
