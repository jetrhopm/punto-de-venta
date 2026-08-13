using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Pos.Desktop;

public partial class PrinterSettingsWindow : Window
{
    public PrinterSettingsWindow() { InitializeComponent(); Loaded += (_, _) => LoadPrinters(); }

    private void LoadPrinters()
    {
        try
        {
            using var server = new LocalPrintServer();
            var names = server.GetPrintQueues().Select(item => item.FullName).OrderBy(item => item).ToArray();
            PrinterBox.ItemsSource = names;
            PrinterBox.SelectedItem = names.FirstOrDefault(item => string.Equals(item, ApiClient.PrinterName, StringComparison.OrdinalIgnoreCase));
            if (PrinterBox.SelectedItem is null && names.Length > 0) PrinterBox.SelectedIndex = 0;
            StatusText.Text = names.Length == 0 ? "Windows no reportó impresoras instaladas." : $"{names.Length} impresora(s) encontrada(s).";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo consultar Windows: {exception.Message}"; }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadPrinters();

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (PrinterBox.SelectedItem is not string printer) { StatusText.Text = "Selecciona una impresora."; return; }
        ApiClient.SetPrinter(printer);
        StatusText.Text = $"Impresora guardada para esta caja: {printer}";
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new PrintDialog();
            if (PrinterBox.SelectedItem is string selected)
            {
                using var server = new LocalPrintServer();
                dialog.PrintQueue = server.GetPrintQueue(selected);
            }
            if (dialog.ShowDialog() != true) return;
            var document = new FlowDocument(new Paragraph(new Run("PUNTO DE VENTA\nPrueba de impresión\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")))) { FontFamily = new FontFamily("Consolas"), FontSize = 12, PagePadding = new Thickness(18), ColumnWidth = double.PositiveInfinity };
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Prueba Punto de Venta");
            StatusText.Text = "Página de prueba enviada a Windows.";
        }
        catch (Exception exception) { StatusText.Text = $"No se pudo imprimir: {exception.Message}"; }
    }
}
