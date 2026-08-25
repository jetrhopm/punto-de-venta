using CsvHelper;
using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Pos.Desktop;

public partial class ProductImportWindow : Window
{
    private const int PageSize = 1000;
    private List<ProductImportPreviewRow> _rows = [];
    private List<ProductImportPreviewRow> _sortedRows = [];
    private int _currentPage = 1;
    private string _sortMember = nameof(ProductImportPreviewRow.RowNumber);
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private bool _refreshScheduled;
    private ImportResult? _lastImportResult;

    public ProductImportWindow() => InitializeComponent();

    private void OnSelectFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Seleccionar exportacion de productos", Filter = "Excel o CSV (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|Excel (*.xlsx)|*.xlsx|CSV (*.csv;*.txt)|*.csv;*.txt" };
        if (dialog.ShowDialog() != true) return;
        if (!decimal.TryParse(WholesaleMinimumBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var minimum) || minimum <= 0) { StatusText.Text = "Escribe una cantidad minima de mayoreo mayor que cero."; return; }
        try
        {
            _rows = ProductImportFileReader.Read(dialog.FileName, minimum).ToList();
            _sortedRows = [.. _rows];
            _lastImportResult = null;
            SaveReportButton.IsEnabled = false;
            _currentPage = 1;
            FileBox.Text = dialog.FileName;
            ApplySort();
            UpdatePreviewPage();
            var errors = RefreshPreviewStatus(focusFirstInvalid: true);
            var wholesale = _rows.Count(item => item.WholesalePrice > 0);
            StatusText.Text = $"{_rows.Count} fila(s), {errors} error(es). Puedes corregir campos en la vista previa antes de importar. {wholesale} precio(s) de mayoreo usaran minimo {minimum:0.###}. El archivo original no se modificara.";
        }
        catch (Exception exception) { _rows = []; ImportButton.IsEnabled = false; StatusText.Text = $"No se pudo leer el archivo: {exception.Message}"; }
    }

    private void OnPreviewCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        SchedulePreviewRefresh();
    }

    private void OnPreviewSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var member = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(member)) return;
        var direction = _sortMember == member && _sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in PreviewGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        _sortMember = member;
        _sortDirection = direction;
        _currentPage = 1;
        ApplySort();
        UpdatePreviewPage();
    }

    private void OnPreviousPageClick(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1) return;
        _currentPage--;
        UpdatePreviewPage();
    }

    private void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        if (_currentPage >= TotalPages) return;
        _currentPage++;
        UpdatePreviewPage();
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        PreviewGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PreviewGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_rows.Count == 0 || RefreshPreviewStatus(focusFirstInvalid: true) > 0) return;
        ImportButton.IsEnabled = false;
        ImportProgressBar.Visibility = Visibility.Visible;
        StatusText.Text = "Creando respaldo previo e importando en una sola transaccion...";
        try
        {
            using var backup = await ApiClient.Client.PostAsync("api/maintenance/backups", null);
            if (!backup.IsSuccessStatusCode) { StatusText.Text = "No se importo nada porque no se pudo crear el respaldo previo: " + await backup.Content.ReadAsStringAsync(); return; }
            var duplicateRule = (DuplicateRuleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Skip";
            var operationId = Guid.NewGuid();
            using var response = await ApiClient.Client.PostAsJsonAsync("api/products/import", new { operationId, sourceFileName = Path.GetFileName(FileBox.Text), duplicateRule, rows = _rows.Select(item => new { item.RowNumber, item.Code, item.Description, item.Price, item.Cost, item.Stock, item.WholesalePrice, item.WholesaleMinimumQuantity, item.Category, item.MinimumStock, item.MaximumStock, item.UnitOfMeasure, item.SupplierName }).ToArray() });
            if (!response.IsSuccessStatusCode) { StatusText.Text = "La importacion se revirtio: " + await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<ImportResult>();
            StatusText.Text = result is null ? "Importacion terminada." : $"Importacion terminada: {result.Created} creados, {result.Updated} actualizados y {result.Skipped} omitidos.";
            _lastImportResult = result;
            SaveReportButton.IsEnabled = true;
            StatusText.Text += " Puedes guardar el reporte cuando lo necesites.";
        }
        catch (Exception exception) { StatusText.Text = $"La importacion no se completo: {exception.Message}"; }
        finally { ImportProgressBar.Visibility = Visibility.Collapsed; ImportButton.IsEnabled = true; }
    }

    private void OnSaveReportClick(object sender, RoutedEventArgs e) => SaveReport(_lastImportResult);

    private void SaveReport(ImportResult? result)
    {
        var dialog = new SaveFileDialog { Title = "Guardar reporte de importacion", Filter = "CSV (*.csv)|*.csv", FileName = $"resultado-importacion-{DateTime.Now:yyyyMMdd-HHmmss}.csv", AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteField("Fila"); csv.WriteField("Codigo"); csv.WriteField("Producto"); csv.WriteField("Estado"); csv.NextRecord();
        foreach (var row in _rows) { csv.WriteField(row.RowNumber); csv.WriteField(SafeForSpreadsheet(row.Code)); csv.WriteField(SafeForSpreadsheet(row.Description)); csv.WriteField(row.Status); csv.NextRecord(); }
        csv.WriteField("Resumen"); csv.WriteField(result is null ? "Importacion terminada" : $"Creados {result.Created}; actualizados {result.Updated}; omitidos {result.Skipped}"); csv.NextRecord();
    }

    private void SchedulePreviewRefresh()
    {
        if (_refreshScheduled) return;
        _refreshScheduled = true;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                RefreshPreviewStatus(focusFirstInvalid: false);
            }
            catch (Exception exception)
            {
                ImportButton.IsEnabled = false;
                ErrorDetailsText.Text = "No se pudo validar la vista previa. Revisa el ultimo dato editado: " + exception.Message;
            }
            finally
            {
                _refreshScheduled = false;
            }
        }, DispatcherPriority.ContextIdle);
    }

    private int RefreshPreviewStatus(bool focusFirstInvalid)
    {
        try
        {
            var duplicateCodes = _rows.Where(item => !string.IsNullOrWhiteSpace(item.Code)).GroupBy(item => item.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows) row.Status = Validate(row, duplicateCodes);
            ApplySort();
            UpdatePreviewPage();
            var invalidRows = _rows.Where(item => item.Status.StartsWith("ERROR", StringComparison.Ordinal)).ToList();
            ImportButton.IsEnabled = _rows.Count > 0 && invalidRows.Count == 0;
            ErrorDetailsText.Text = invalidRows.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, invalidRows.Take(8).Select(item => $"Fila {item.RowNumber}: {item.Status[7..]}")) + (invalidRows.Count > 8 ? $"{Environment.NewLine}... y {invalidRows.Count - 8} error(es) mas." : string.Empty);
            if (focusFirstInvalid && invalidRows.Count > 0)
            {
                var index = _sortedRows.IndexOf(invalidRows[0]);
                if (index >= 0)
                {
                    _currentPage = (index / PageSize) + 1;
                    UpdatePreviewPage();
                }
                PreviewGrid.SelectedItem = invalidRows[0];
                PreviewGrid.ScrollIntoView(invalidRows[0]);
            }
            return invalidRows.Count;
        }
        catch (Exception exception)
        {
            ImportButton.IsEnabled = false;
            ErrorDetailsText.Text = "No se pudo validar la vista previa. Revisa el ultimo dato editado: " + exception.Message;
            return 1;
        }
    }

    private static string Validate(ProductImportPreviewRow row, HashSet<string> duplicates)
    {
        if (string.IsNullOrWhiteSpace(row.Code)) return "ERROR: codigo vacio";
        if (string.IsNullOrWhiteSpace(row.Description)) return "ERROR: descripcion vacia";
        if (row.Price == decimal.MinValue || row.Cost == decimal.MinValue || row.WholesalePrice == decimal.MinValue) return "ERROR: numero invalido";
        if (row.Price < 0 || row.Cost < 0 || row.Stock < 0 || row.WholesalePrice < 0 || row.WholesaleMinimumQuantity < 0 || row.MinimumStock < 0 || row.MaximumStock < 0) return "ERROR: valor negativo";
        if (row.MaximumStock > 0 && row.MaximumStock < row.MinimumStock) return "ERROR: maximo menor al minimo";
        if (row.WholesalePrice > 0 && row.WholesaleMinimumQuantity <= 0) return "ERROR: mayoreo requiere minimo";
        if (duplicates.Contains(row.Code.Trim())) return "ERROR: codigo repetido";
        return "Valido";
    }

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_sortedRows.Count / (double)PageSize));

    private void UpdatePreviewPage()
    {
        if (_currentPage > TotalPages) _currentPage = TotalPages;
        if (_currentPage < 1) _currentPage = 1;
        PreviewGrid.ItemsSource = _sortedRows.Skip((_currentPage - 1) * PageSize).Take(PageSize).ToList();
        PreviousPageButton.IsEnabled = _currentPage > 1;
        NextPageButton.IsEnabled = _currentPage < TotalPages;
        var first = _sortedRows.Count == 0 ? 0 : ((_currentPage - 1) * PageSize) + 1;
        var last = Math.Min(_currentPage * PageSize, _sortedRows.Count);
        PageInfoText.Text = _sortedRows.Count == 0 ? "Sin filas cargadas." : $"Pagina {_currentPage} de {TotalPages}. Mostrando {first}-{last} de {_sortedRows.Count} productos.";
    }

    private void ApplySort()
    {
        Func<ProductImportPreviewRow, object> key = _sortMember switch
        {
            nameof(ProductImportPreviewRow.RowNumber) => row => row.RowNumber,
            nameof(ProductImportPreviewRow.Code) => row => row.Code,
            nameof(ProductImportPreviewRow.Description) => row => row.Description,
            nameof(ProductImportPreviewRow.Category) => row => row.Category,
            nameof(ProductImportPreviewRow.Cost) => row => row.Cost,
            nameof(ProductImportPreviewRow.Price) => row => row.Price,
            nameof(ProductImportPreviewRow.WholesalePrice) => row => row.WholesalePrice,
            nameof(ProductImportPreviewRow.WholesaleMinimumQuantity) => row => row.WholesaleMinimumQuantity,
            nameof(ProductImportPreviewRow.Stock) => row => row.Stock,
            nameof(ProductImportPreviewRow.MinimumStock) => row => row.MinimumStock,
            nameof(ProductImportPreviewRow.MaximumStock) => row => row.MaximumStock,
            nameof(ProductImportPreviewRow.UnitOfMeasure) => row => row.UnitOfMeasure,
            nameof(ProductImportPreviewRow.SupplierName) => row => row.SupplierName,
            nameof(ProductImportPreviewRow.Status) => row => row.Status,
            _ => row => row.RowNumber
        };
        _sortedRows = (_sortDirection == ListSortDirection.Ascending ? _rows.OrderBy(key) : _rows.OrderByDescending(key)).ToList();
    }

    private static string SafeForSpreadsheet(string value) => value.Length > 0 && "=+-@\t\r".Contains(value[0]) ? "'" + value : value;
    private sealed record ImportResult(Guid ImportId, int Created, int Updated, int Skipped, bool ExistingResult);
}
