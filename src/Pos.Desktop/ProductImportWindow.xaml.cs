using CsvHelper;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Desktop;

public partial class ProductImportWindow : Window
{
    private IReadOnlyList<ProductImportPreviewRow> _rows = [];

    public ProductImportWindow() => InitializeComponent();

    private void OnSelectFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Seleccionar exportación de productos", Filter = "Excel o CSV (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|Excel (*.xlsx)|*.xlsx|CSV (*.csv;*.txt)|*.csv;*.txt" };
        if (dialog.ShowDialog() != true) return;
        if (!decimal.TryParse(WholesaleMinimumBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var minimum) || minimum <= 0) { StatusText.Text = "Escribe una cantidad mínima de mayoreo mayor que cero."; return; }
        try
        {
            _rows = ProductImportFileReader.Read(dialog.FileName, minimum);
            FileBox.Text = dialog.FileName;
            PreviewGrid.ItemsSource = _rows.Take(1000).ToArray();
            var errors = _rows.Count(item => item.Status.StartsWith("ERROR", StringComparison.Ordinal));
            var wholesale = _rows.Count(item => item.WholesalePrice > 0);
            StatusText.Text = $"{_rows.Count} fila(s), {errors} error(es). Vista previa limitada a 1000. {wholesale} precio(s) de mayoreo usarán mínimo {minimum:0.###}. El archivo original no se modificará.";
            ImportButton.IsEnabled = _rows.Count > 0 && errors == 0;
        }
        catch (Exception exception) { _rows = []; ImportButton.IsEnabled = false; StatusText.Text = $"No se pudo leer el archivo: {exception.Message}"; }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0 || _rows.Any(item => item.Status.StartsWith("ERROR", StringComparison.Ordinal))) return;
        ImportButton.IsEnabled = false;
        StatusText.Text = "Creando respaldo previo e importando en una sola transacción...";
        try
        {
            using var backup = await ApiClient.Client.PostAsync("api/maintenance/backups", null);
            if (!backup.IsSuccessStatusCode) { StatusText.Text = "No se importó nada porque no se pudo crear el respaldo previo: " + await backup.Content.ReadAsStringAsync(); return; }
            var duplicateRule = (DuplicateRuleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Skip";
            var operationId = Guid.NewGuid();
            using var response = await ApiClient.Client.PostAsJsonAsync("api/products/import", new { operationId, sourceFileName = Path.GetFileName(FileBox.Text), duplicateRule, rows = _rows.Select(item => new { item.RowNumber, item.Code, item.Description, item.Price, item.Cost, item.Stock, item.WholesalePrice, item.WholesaleMinimumQuantity, item.Category, item.MinimumStock, item.MaximumStock, item.UnitOfMeasure, item.SupplierName }).ToArray() });
            if (!response.IsSuccessStatusCode) { StatusText.Text = "La importación se revirtió: " + await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<ImportResult>();
            StatusText.Text = result is null ? "Importación terminada." : $"Importación terminada: {result.Created} creados, {result.Updated} actualizados y {result.Skipped} omitidos.";
            SaveReport(result);
        }
        catch (Exception exception) { StatusText.Text = $"La importación no se completó: {exception.Message}"; }
        finally { ImportButton.IsEnabled = true; }
    }

    private void SaveReport(ImportResult? result)
    {
        var dialog = new SaveFileDialog { Title = "Guardar reporte de importación", Filter = "CSV (*.csv)|*.csv", FileName = $"resultado-importacion-{DateTime.Now:yyyyMMdd-HHmmss}.csv", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteField("Fila"); csv.WriteField("Código"); csv.WriteField("Producto"); csv.WriteField("Estado"); csv.NextRecord();
        foreach (var row in _rows) { csv.WriteField(row.RowNumber); csv.WriteField(SafeForSpreadsheet(row.Code)); csv.WriteField(SafeForSpreadsheet(row.Description)); csv.WriteField(row.Status); csv.NextRecord(); }
        csv.WriteField("Resumen"); csv.WriteField(result is null ? "Importación terminada" : $"Creados {result.Created}; actualizados {result.Updated}; omitidos {result.Skipped}"); csv.NextRecord();
    }

    private static string SafeForSpreadsheet(string value) => value.Length > 0 && "=+-@\t\r".Contains(value[0]) ? "'" + value : value;
    private sealed record ImportResult(Guid ImportId, int Created, int Updated, int Skipped, bool ExistingResult);
}
