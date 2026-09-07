using System.Reflection;
using System.Runtime.InteropServices;
using System.Net.Http.Json;
using System.Text;
using System.Windows;

namespace Pos.Desktop;

public partial class DiagnosticWindow : Window
{
    private List<DiagnosticCheck> _checks = [];
    private string _reportText = string.Empty;

    public DiagnosticWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnRepairApiClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(ConnectionHelp.LocalRepairConfirmation, "Levantar API", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RepairApiButton.IsEnabled = false;
        StatusText.Text = "Levantando y comprobando los servicios de JetVenta...";
        try
        {
            var startup = new StartupWindow();
            var repaired = await startup.RepairServicesAndWaitAsync();
            StatusText.Text = repaired
                ? "La API respondió correctamente. Actualizando diagnóstico..."
                : "No se pudo levantar la API. Revisa los detalles y vuelve a intentarlo.";
            await RefreshAsync();
            if (repaired)
            {
                MessageBox.Show("La API volvió a responder y JetVenta actualizó el diagnóstico. Puedes regresar a operar; revisa cualquier fila marcada como Problema.", "Servicios listos", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("No se pudo recuperar la API. Revisa las filas de Diagnóstico y, si el servidor es otra computadora, confirma que esté encendido, conectado y con JetVenta instalado.", "No se pudo levantar la API", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo levantar la API. Ve a Configuración > Diagnóstico y pulsa Levantar API");
        }
        finally { RepairApiButton.IsEnabled = true; }
    }

    private async void OnClearPrintQueueClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Se cancelarán únicamente las solicitudes de impresión que siguen pendientes. Las ventas, los tickets y el historial no se borrarán; podrás reimprimir cualquier venta desde Historial. ¿Deseas continuar?", "Limpiar cola de impresión", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        ClearPrintQueueButton.IsEnabled = false;
        try
        {
            using var response = await ApiClient.Client.DeleteAsync("api/diagnostics/print-queue");
            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show("No se pudo limpiar la cola de impresión. No se modificó ninguna venta.", "Cola sin cambios", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<PrintQueueClearResult>();
            await RefreshAsync();
            MessageBox.Show($"Se cancelaron {result?.Cancelled ?? 0} solicitud(es) de impresión. Las ventas y los tickets permanecen disponibles para reimprimir.", "Cola actualizada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(ConnectionHelp.FromException(exception, "No se pudo limpiar la cola de impresión"), "Cola sin cambios", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { ClearPrintQueueButton.IsEnabled = _checks.Any(item => item.Name == "Cola de impresión" && item.Status == "Aviso"); }
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "Consultando servicios y datos de JetVenta...";
        try
        {
            var report = await ApiClient.Client.GetFromJsonAsync<DiagnosticApiReport>("api/diagnostics");
            if (report is null) throw new InvalidOperationException("El servidor no devolvió información.");

            _checks = report.Checks.ToList();
            AddLocalChecks();
            ChecksGrid.ItemsSource = _checks;
            ProductsText.Text = report.ProductCount.ToString("N0");
            SalesText.Text = report.CompletedSaleCount.ToString("N0");
            BackupText.Text = report.LatestBackup ?? "Sin respaldo";
            CheckedText.Text = report.CheckedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            _reportText = BuildReportText(report, _checks);
            ClearPrintQueueButton.IsEnabled = report.PendingPrintJobCount > 0;
            StatusText.Text = _checks.Any(item => item.Status == "Problema")
                ? "Se encontraron problemas. Atiende primero las filas marcadas como Problema."
                : "Diagnóstico terminado. Revisa también los avisos antes de operar.";
        }
        catch (Exception exception)
        {
            _checks = [new("Conexión con JetVenta", "Problema", ConnectionHelp.ApiUnavailableRetry, "Pulsa Levantar API en esta ventana.")];
            ChecksGrid.ItemsSource = _checks;
            ProductsText.Text = "-";
            SalesText.Text = "-";
            BackupText.Text = "-";
            CheckedText.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            _reportText = $"JETVENTA - DIAGNÓSTICO{Environment.NewLine}{exception.Message}";
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
            ClearPrintQueueButton.IsEnabled = false;
        }
    }

    private void AddLocalChecks()
    {
        var printers = TicketWindowsPrinter.GetInstalledPrinters();
        _checks.Add(printers.Length == 0
            ? new("Impresoras de Windows", "Aviso", "Windows no reportó impresoras instaladas.", "Instala o conecta una impresora si deseas imprimir tickets.")
            : new("Impresoras de Windows", "Correcto", $"Windows reportó {printers.Length} impresora(s) instalada(s).", ""));

        if (string.IsNullOrWhiteSpace(ApiClient.PrinterName))
        {
            _checks.Add(new("Impresora de tickets", "Pendiente", "No hay una impresora seleccionada para esta caja.", "Configúrala en Configuración > Impresora de tickets."));
        }
        else if (printers.Contains(ApiClient.PrinterName, StringComparer.OrdinalIgnoreCase))
        {
            _checks.Add(new("Impresora de tickets", "Correcto", $"Configurada: {ApiClient.PrinterName}.", ""));
        }
        else
        {
            _checks.Add(new("Impresora de tickets", "Problema", $"La impresora guardada no aparece en Windows: {ApiClient.PrinterName}.", "Selecciona una impresora disponible en Configuración."));
        }

        _checks.Add(new("Recargas, pagos y terminales", "Pendiente", "Estas integraciones todavía no están habilitadas.", "Se implementarán después de definir proveedor, credenciales y pruebas reales."));
    }

    private static string BuildReportText(DiagnosticApiReport report, IEnumerable<DiagnosticCheck> checks)
    {
        var builder = new StringBuilder();
        builder.AppendLine("JETVENTA - REPORTE DE DIAGNÓSTICO");
        builder.AppendLine("=================================");
        builder.AppendLine($"Versión API: {report.ApiVersion}");
        builder.AppendLine($"Versión cliente: {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "No disponible"}");
        builder.AppendLine($"Equipo: {Environment.MachineName}");
        builder.AppendLine($"Sistema operativo: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Arquitectura: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Revisado: {report.CheckedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        builder.AppendLine($"Productos activos: {report.ProductCount:N0}");
        builder.AppendLine($"Ventas confirmadas: {report.CompletedSaleCount:N0}");
        builder.AppendLine($"Clientes activos: {report.CustomerCount:N0}");
        builder.AppendLine($"Proveedores: {report.SupplierCount:N0}");
        builder.AppendLine($"Usuarios activos: {report.UserCount:N0}");
        builder.AppendLine($"Tickets abiertos: {report.OpenTicketCount:N0}");
        builder.AppendLine($"Solicitudes de impresión pendientes: {report.PendingPrintJobCount:N0}");
        builder.AppendLine($"Respaldo más reciente: {report.LatestBackup ?? "Sin respaldo"}");
        builder.AppendLine($"Checksum del respaldo: {report.LatestBackupSha256 ?? "No disponible"}");
        if (report.FreeBytes is not null) builder.AppendLine($"Espacio libre del servidor: {report.FreeBytes.Value / 1024d / 1024d / 1024d:0.##} GB");
        builder.AppendLine();
        foreach (var check in checks) builder.AppendLine($"[{check.Status}] {check.Name}: {check.Detail} {check.Action}");
        return builder.ToString();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetDataObject(_reportText, true); StatusText.Text = "Reporte copiado al portapapeles."; }
        catch (Exception exception) { StatusText.Text = $"No se pudo copiar el reporte: {exception.Message}"; }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed record DiagnosticCheck(string Name, string Status, string Detail, string Action);
    private sealed record PrintQueueClearResult(int Cancelled);
    private sealed record DiagnosticApiReport(DateTimeOffset CheckedAtUtc, string ApiVersion, List<DiagnosticCheck> Checks, int ProductCount, int UserCount, int CustomerCount, int SupplierCount, int CompletedSaleCount, int OpenTicketCount, int PendingPrintJobCount, int BackupCount, string? LatestBackup, string? LatestBackupSha256, long? FreeBytes);
}
