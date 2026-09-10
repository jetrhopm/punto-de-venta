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
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo levantar la API. Ve a Configuración > Diagnóstico y pulsa Levantar API");
        }
        finally { RepairApiButton.IsEnabled = true; }
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
            PrintQueueInfoText.Text = BuildPrintQueueHelp(report.PendingPrintJobCount, report.ResidualPrintDocumentCount);
            DiscardPendingDocumentsButton.IsEnabled = report.PendingPrintJobCount > 0;
            ClearTechnicalDocumentsButton.IsEnabled = report.ResidualPrintDocumentCount > 0;
            _reportText = BuildReportText(report, _checks);
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
            PrintQueueInfoText.Text = "No se pudo consultar la cola de impresión mientras JetVenta no responda.";
            DiscardPendingDocumentsButton.IsEnabled = false;
            ClearTechnicalDocumentsButton.IsEnabled = false;
            _reportText = $"JETVENTA - DIAGNÓSTICO{Environment.NewLine}{exception.Message}";
            StatusText.Text = ConnectionHelp.ApiUnavailableRetry;
        }
    }

    private async void OnClearTechnicalDocumentsClick(object sender, RoutedEventArgs e)
    {
        var confirmation = new OperationConfirmationWindow(
            "Limpiar historial técnico",
            "Se eliminará únicamente el historial técnico de documentos ya generados o impresos. No se eliminarán ventas, tickets, inventario ni trabajos pendientes de impresión.",
            OperationResultKind.Warning,
            MessageBoxButton.YesNo) { Owner = this };
        if (confirmation.ShowDialog() != true || confirmation.Result != MessageBoxResult.Yes) return;

        ClearTechnicalDocumentsButton.IsEnabled = false;
        StatusText.Text = "Limpiando el historial técnico de impresión...";
        var cleaned = false;
        try
        {
            using var response = await ApiClient.Client.DeleteAsync("api/diagnostics/technical-print-documents");
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo limpiar el historial técnico de impresión.");
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<ClearTechnicalDocumentsResult>();
            var message = result is null || result.DeletedCount == 0
                ? "No había documentos técnicos por limpiar."
                : $"Se limpiaron {result.DeletedCount:N0} documento(s) técnico(s). Las ventas y tickets no se modificaron.";
            cleaned = true;
            await RefreshAsync();
            StatusText.Text = message;
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo limpiar el historial técnico de impresión");
        }
        finally
        {
            if (!cleaned) ClearTechnicalDocumentsButton.IsEnabled = true;
        }
    }

    private async void OnDiscardPendingDocumentsClick(object sender, RoutedEventArgs e)
    {
        var confirmation = new OperationConfirmationWindow(
            "Descartar cola pendiente",
            "Se descartarán únicamente los intentos de impresión pendientes. Las ventas ya cobradas, tickets, pagos, inventario y cortes no se eliminarán. Usa esta opción sólo si ya no necesitas imprimir esos tickets.",
            OperationResultKind.Warning,
            MessageBoxButton.YesNo) { Owner = this };
        if (confirmation.ShowDialog() != true || confirmation.Result != MessageBoxResult.Yes) return;

        DiscardPendingDocumentsButton.IsEnabled = false;
        StatusText.Text = "Descartando la cola técnica de impresión...";
        var discarded = false;
        try
        {
            using var response = await ApiClient.Client.DeleteAsync("api/diagnostics/pending-print-documents");
            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = await ConfigurationFeedback.ReadErrorAsync(response, "No se pudo descartar la cola pendiente.");
                return;
            }
            var result = await response.Content.ReadFromJsonAsync<ClearTechnicalDocumentsResult>();
            var message = result is null || result.DeletedCount == 0
                ? "No había trabajos de impresión pendientes."
                : $"Se descartaron {result.DeletedCount:N0} trabajo(s) de impresión pendiente(s). Las ventas no se modificaron.";
            discarded = true;
            await RefreshAsync();
            StatusText.Text = message;
        }
        catch (Exception exception)
        {
            StatusText.Text = ConnectionHelp.FromException(exception, "No se pudo descartar la cola pendiente");
        }
        finally
        {
            if (!discarded) DiscardPendingDocumentsButton.IsEnabled = true;
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
        builder.AppendLine($"Trabajos de impresión pendientes: {report.PendingPrintJobCount:N0}");
        builder.AppendLine($"Respaldo más reciente: {report.LatestBackup ?? "Sin respaldo"}");
        builder.AppendLine($"Checksum del respaldo: {report.LatestBackupSha256 ?? "No disponible"}");
        if (report.FreeBytes is not null) builder.AppendLine($"Espacio libre del servidor: {report.FreeBytes.Value / 1024d / 1024d / 1024d:0.##} GB");
        builder.AppendLine();
        foreach (var check in checks) builder.AppendLine($"[{check.Status}] {check.Name}: {check.Detail} {check.Action}");
        return builder.ToString();
    }

    private static string BuildPrintQueueHelp(int pending, int residual)
    {
        if (pending == 0 && residual == 0) return "No hay trabajos pendientes ni historial técnico de impresión.";
        var messages = new List<string>();
        if (pending > 0) messages.Add($"Hay {pending:N0} trabajo(s) pendiente(s): las ventas ya están confirmadas. Revisa Configuración > Impresora; si ya no debes imprimirlos, usa Descartar cola pendiente.");
        if (residual > 0) messages.Add($"Hay {residual:N0} documento(s) ya generado(s) o impreso(s): Limpiar historial técnico sólo borra ese registro técnico, sin afectar ventas, tickets ni inventario.");
        return string.Join(" ", messages);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetDataObject(_reportText, true); StatusText.Text = "Reporte copiado al portapapeles."; }
        catch (Exception exception) { StatusText.Text = $"No se pudo copiar el reporte: {exception.Message}"; }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed record DiagnosticCheck(string Name, string Status, string Detail, string Action);
    private sealed record DiagnosticApiReport(DateTimeOffset CheckedAtUtc, string ApiVersion, List<DiagnosticCheck> Checks, int ProductCount, int UserCount, int CustomerCount, int SupplierCount, int CompletedSaleCount, int OpenTicketCount, int PendingPrintJobCount, int ResidualPrintDocumentCount, int BackupCount, string? LatestBackup, string? LatestBackupSha256, long? FreeBytes);
    private sealed record ClearTechnicalDocumentsResult(int DeletedCount);
}
