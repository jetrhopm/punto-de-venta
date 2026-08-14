using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class PrinterSettingsWindow : Window
{
    private bool _loaded;

    public PrinterSettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FontBox.ItemsSource = TicketWindowsPrinter.GetInstalledFonts();
        FontBox.Text = ApiClient.PrinterFontFamily;
        FontSizeBox.Text = ApiClient.PrinterFontSize.ToString("0.#", CultureInfo.CurrentCulture);
        NormalTotalsCheck.IsChecked = ApiClient.UseNormalTotals;
        Width58Button.IsChecked = ApiClient.PrinterTicketWidthMm == 58;
        Width80Button.IsChecked = ApiClient.PrinterTicketWidthMm != 58;
        LoadPrinters();
        _loaded = true;
        UpdatePreview();
    }

    private void LoadPrinters()
    {
        try
        {
            var names = TicketWindowsPrinter.GetInstalledPrinters();
            PrinterBox.ItemsSource = names;
            PrinterBox.SelectedItem = names.FirstOrDefault(item => string.Equals(item, ApiClient.PrinterName, StringComparison.OrdinalIgnoreCase));
            if (PrinterBox.SelectedItem is null && names.Length > 0) PrinterBox.SelectedIndex = 0;
            StatusText.Text = names.Length == 0 ? "Windows no reportó impresoras disponibles." : $"Windows reportó {names.Length} impresora(s) disponible(s).";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudieron consultar las impresoras de Windows: {exception.Message}"; }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadPrinters();

    private void OnPreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_loaded) UpdatePreview();
    }

    private void OnPreviewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded) UpdatePreview();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadProfile(out var printer, out var profile)) return;
        ApiClient.SetPrinterProfile(printer, profile.FontFamily, profile.FontSize, profile.UseNormalTotals, profile.WidthMm);
        ProfileSummaryText.Text = $"{profile.WidthMm} mm · {profile.FontFamily} {profile.FontSize:0.#} pt";
        StatusText.Text = $"Configuración guardada para esta caja: {printer}.";
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadProfile(out var printer, out var profile)) return;
        try
        {
            TicketWindowsPrinter.Print(printer, TicketWindowsPrinter.CreateSample(profile.WidthMm), profile, "Prueba de ticket JetVenta");
            StatusText.Text = $"Ticket de prueba enviado a {printer}.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo imprimir la prueba: {exception.Message}"; }
    }

    private void UpdatePreview()
    {
        var profile = ReadProfileForPreview();
        PreviewWidthText.Text = $"{profile.WidthMm} mm";
        TicketPreviewHost.Content = TicketWindowsPrinter.CreateTicketVisual(TicketWindowsPrinter.CreateSample(profile.WidthMm), profile);
        ProfileSummaryText.Text = $"{profile.WidthMm} mm · {profile.FontFamily} {profile.FontSize:0.#} pt";
    }

    private bool TryReadProfile(out string printer, out TicketPrintProfile profile)
    {
        printer = PrinterBox.SelectedItem as string ?? PrinterBox.Text;
        profile = ReadProfileForPreview();
        if (string.IsNullOrWhiteSpace(printer)) { StatusText.Text = "Selecciona una impresora instalada en Windows."; return false; }
        if (!double.TryParse(FontSizeBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var size) || size is < 6d or > 24d)
        {
            StatusText.Text = "El tamaño de fuente debe estar entre 6 y 24 puntos.";
            return false;
        }
        profile = profile with { FontSize = size };
        return true;
    }

    private TicketPrintProfile ReadProfileForPreview()
    {
        var family = string.IsNullOrWhiteSpace(FontBox.Text) ? "Consolas" : FontBox.Text.Trim();
        var size = double.TryParse(FontSizeBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) && parsed is >= 6d and <= 24d ? parsed : 9d;
        return new TicketPrintProfile(family, size, NormalTotalsCheck.IsChecked == true, Width58Button.IsChecked == true ? 58 : 80);
    }
}
