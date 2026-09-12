using Pos.Printing;
using System.Net.Http;
using System.Net.Http.Json;
using System.Globalization;
using System.Windows;

namespace Pos.Desktop;

public partial class TicketSettingsWindow : Window
{
    private static HttpClient Client => ApiClient.Client;
    private bool _loaded;
    private int _storedTicketWidthMm = 80;

    public TicketSettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            FontBox.ItemsSource = TicketWindowsPrinter.GetInstalledFonts();
            FontBox.Text = ApiClient.PrinterFontFamily;
            FontSizeBox.Text = ApiClient.PrinterFontSize.ToString("0.#", CultureInfo.CurrentCulture);
            NormalTotalsCheck.IsChecked = ApiClient.UseNormalTotals;
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
                _storedTicketWidthMm = settings.TicketWidthMm == 58 ? 58 : 80;
            }
            _loaded = true;
            UpdatePreview();
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo cargar la configuración"); }
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
        if (!TryReadProfile(out var profile)) return;
        try
        {
            using var response = await Client.PutAsJsonAsync("/api/ticket-settings", new
            {
                header = HeaderBox.Text,
                footer = FooterBox.Text,
                widthMm = _storedTicketWidthMm,
                storeName = StoreNameBox.Text,
                legalName = LegalNameBox.Text,
                taxId = TaxIdBox.Text,
                address = AddressBox.Text,
                phone = PhoneBox.Text
            });
            if (!response.IsSuccessStatusCode) { StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo guardar el diseño del ticket."); return; }
            ApiClient.SetPrinterProfile(ApiClient.PrinterName, profile.FontFamily, profile.FontSize, profile.UseNormalTotals, ApiClient.PrinterTicketWidthMm, ApiClient.PrintingEnabled);
            ConfigurationFeedback.ShowSavedAndClose(this, "Diseño del ticket", $"Las próximas ventas usarán los datos configurados. El ancho local de esta caja es {SelectedWidth} mm.");
        }
        catch (Exception exception) { StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo guardar"); }
    }

    private void OnPrintSampleClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiClient.PrinterName)) { StatusText.Text = "Primero selecciona una impresora en Configuración > Impresora."; return; }
        try
        {
            var profile = CurrentProfile();
            TicketWindowsPrinter.Print(ApiClient.PrinterName, CreatePreviewData(), profile, "Muestra de ticket JetVenta");
            StatusText.Text = $"Ticket muestra enviado a {ApiClient.PrinterName}.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo imprimir la muestra: {exception.Message}"; }
    }

    private void UpdatePreview()
    {
        var width = SelectedWidth;
        PreviewWidthText.Text = $"{width} mm";
        var profile = CurrentProfile();
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

    private int SelectedWidth => ApiClient.PrinterTicketWidthMm == 58 ? 58 : 80;

    private TicketPrintProfile CurrentProfile() => TryReadProfile(out var profile)
        ? profile
        : new TicketPrintProfile(ApiClient.PrinterFontFamily, ApiClient.PrinterFontSize, ApiClient.UseNormalTotals, SelectedWidth);

    private bool TryReadProfile(out TicketPrintProfile profile)
    {
        var family = string.IsNullOrWhiteSpace(FontBox.Text) ? "Consolas" : FontBox.Text.Trim();
        if (!double.TryParse(FontSizeBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var size) || size is < 6d or > 24d)
        {
            StatusText.Text = "El tamaño de fuente debe estar entre 6 y 24 puntos.";
            profile = default!;
            return false;
        }
        profile = new TicketPrintProfile(family, size, NormalTotalsCheck.IsChecked == true, SelectedWidth);
        return true;
    }

    private sealed record TicketSettings(string Name, string LegalName, string TaxId, string Address, string Phone, string TicketHeader, string TicketFooter, int TicketWidthMm);
}
