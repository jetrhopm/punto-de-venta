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
        PrintingEnabledCheck.IsChecked = ApiClient.PrintingEnabled;
        Width58Button.IsChecked = ApiClient.PrinterTicketWidthMm == 58;
        Width80Button.IsChecked = ApiClient.PrinterTicketWidthMm != 58;
        LoadPrinters();
        _loaded = true;
        PrinterBox.IsEnabled = PrintingEnabledCheck.IsChecked == true;
        UpdatePreview();
    }

    private void LoadPrinters(bool showResult = false)
    {
        try
        {
            var names = TicketWindowsPrinter.GetInstalledPrinters();
            PrinterBox.ItemsSource = names;
            PrinterBox.SelectedItem = names.FirstOrDefault(item => string.Equals(item, ApiClient.PrinterName, StringComparison.OrdinalIgnoreCase));
            if (PrinterBox.SelectedItem is null && names.Length > 0) PrinterBox.SelectedIndex = 0;
            var status = names.Length == 0 ? "Windows no reportó impresoras disponibles." : $"Windows reportó {names.Length} impresora(s) disponible(s).";
            StatusText.Text = status;
            if (showResult) ShowResult(names.Length == 0 ? "No se detectaron impresoras" : "Impresoras detectadas", status, names.Length == 0 ? OperationResultKind.Warning : OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            var message = $"No se pudieron consultar las impresoras de Windows. {exception.Message}";
            StatusText.Text = message;
            if (showResult) ShowResult("No se pudieron detectar impresoras", message, OperationResultKind.Error);
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadPrinters(showResult: true);

    private void OnPreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_loaded) UpdatePreview();
    }

    private void OnPreviewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded) UpdatePreview();
    }

    private void OnPrintingEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var enabled = PrintingEnabledCheck.IsChecked == true;
        PrinterBox.IsEnabled = enabled;
        UpdatePreview();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadProfile(out var printer, out var profile)) return;
        var printingEnabled = PrintingEnabledCheck.IsChecked == true;
        ApiClient.SetPrinterProfile(printer, ApiClient.PrinterFontFamily, ApiClient.PrinterFontSize, ApiClient.UseNormalTotals, profile.WidthMm, printingEnabled);
        ProfileSummaryText.Text = $"{profile.WidthMm} mm";
        var status = printingEnabled
            ? $"Configuración guardada para esta caja: {printer}."
            : "La impresión de tickets quedó desactivada para esta caja.";
        StatusText.Text = status;
        ConfigurationFeedback.ShowSavedAndClose(this, "Impresora", status);
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (PrintingEnabledCheck.IsChecked != true)
        {
            const string message = "Activa el uso de impresora antes de enviar una prueba.";
            StatusText.Text = message;
            ShowResult("Impresora desactivada", message, OperationResultKind.Warning);
            return;
        }
        if (!TryReadProfile(out var printer, out var profile)) return;
        try
        {
            TicketWindowsPrinter.Print(printer, TicketWindowsPrinter.CreateSample(profile.WidthMm), profile, "Prueba de ticket JetVenta");
            var status = $"Ticket de prueba enviado a {printer}.";
            StatusText.Text = status;
            ShowResult("Prueba enviada", status, OperationResultKind.Success);
        }
        catch (Exception exception)
        {
            var message = $"No se pudo imprimir la prueba. {exception.Message}";
            StatusText.Text = message;
            ShowResult("Prueba no impresa", message, OperationResultKind.Error);
        }
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
        if (PrintingEnabledCheck.IsChecked != true)
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(printer))
        {
            const string message = "Selecciona una impresora instalada en Windows.";
            StatusText.Text = message;
            ShowResult("Selecciona una impresora", message, OperationResultKind.Warning);
            return false;
        }
        return true;
    }

    private TicketPrintProfile ReadProfileForPreview()
    {
        return new TicketPrintProfile(ApiClient.PrinterFontFamily, ApiClient.PrinterFontSize, ApiClient.UseNormalTotals, Width58Button.IsChecked == true ? 58 : 80);
    }

    private void ShowResult(string title, string message, OperationResultKind kind) => new OperationResultWindow(title, message, kind) { Owner = this }.ShowDialog();
}
