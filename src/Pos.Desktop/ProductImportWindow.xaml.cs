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
    private ProductImportSource? _source;
    private ProductImportColumnMapping? _mapping;
    private decimal _defaultWholesaleMinimum = 1m;

    public ProductImportWindow() => InitializeComponent();

    private void OnSelectFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Seleccionar exportacion de productos", Filter = "Excel o CSV (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|Excel (*.xlsx)|*.xlsx|CSV (*.csv;*.txt)|*.csv;*.txt" };
        if (dialog.ShowDialog() != true) return;
        if (!TryGetWholesaleMinimum(out var minimum)) return;
        try
        {
            var source = ProductImportFileReader.ReadSource(dialog.FileName);
            var mappingWindow = new ProductImportMappingWindow(source, ProductImportFileReader.SuggestMapping(source)) { Owner = this };
            if (mappingWindow.ShowDialog() != true) return;
            _source = source;
            _mapping = mappingWindow.Mapping;
            _defaultWholesaleMinimum = minimum;
            FileBox.Text = dialog.FileName;
            AdjustColumnsButton.IsEnabled = true;
            ApplyMapping();
        }
        catch (Exception exception) { _rows = []; SetImportActionsEnabled(false); StatusText.Text = $"No se pudo leer el archivo: {exception.Message}"; }
    }

    private void OnAdjustColumnsClick(object sender, RoutedEventArgs e)
    {
        if (_source is null || _mapping is null) return;
        if (!TryGetWholesaleMinimum(out var minimum)) return;
        var mappingWindow = new ProductImportMappingWindow(_source, _mapping) { Owner = this };
        if (mappingWindow.ShowDialog() != true) return;
        _mapping = mappingWindow.Mapping;
        _defaultWholesaleMinimum = minimum;
        ApplyMapping();
    }

    private void ApplyMapping()
    {
        if (_source is null || _mapping is null) return;
        _rows = ProductImportFileReader.Map(_source, _mapping, _defaultWholesaleMinimum).ToList();
        _sortedRows = [.. _rows];
        _lastImportResult = null;
        SaveReportButton.IsEnabled = false;
        _currentPage = 1;
        ApplySort();
        UpdatePreviewPage();
        var errors = RefreshPreviewStatus(focusFirstInvalid: true);
        var wholesale = _rows.Count(item => item.WholesalePrice > 0);
        StatusText.Text = $"{_rows.Count} producto(s) seleccionados, {errors} error(es). Desmarca cualquier registro que quieras omitir o corrige sus datos. {wholesale} precio(s) de mayoreo usarán mínimo {_defaultWholesaleMinimum:0.###}.";
    }

    private bool TryGetWholesaleMinimum(out decimal minimum)
    {
        if (decimal.TryParse(WholesaleMinimumBox.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out minimum) && minimum > 0) return true;
        StatusText.Text = "Escribe una cantidad mínima de mayoreo mayor que cero.";
        return false;
    }

    private void OnPreviewCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        SchedulePreviewRefresh();
    }

    private void OnRowSelectionClick(object sender, RoutedEventArgs e)
    {
        // Run after WPF applies the new check state, so omitted products leave validation immediately.
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

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = true;
        var errors = RefreshPreviewStatus(focusFirstInvalid: false);
        StatusText.Text = $"Se seleccionaron {_rows.Count} producto(s). Corrige o desmarca los {errors} registro(s) con error antes de importar.";
    }

    private void OnOmitErrorsClick(object sender, RoutedEventArgs e)
    {
        RefreshPreviewStatus(focusFirstInvalid: false);
        var omitted = _rows.Where(row => row.IsSelected && row.Status.StartsWith("ERROR", StringComparison.Ordinal)).ToList();
        foreach (var row in omitted) row.IsSelected = false;
        RefreshPreviewStatus(focusFirstInvalid: false);
        StatusText.Text = omitted.Count == 0
            ? "No había productos con error para omitir."
            : $"Se omitieron {omitted.Count} producto(s) con error. Los demás están listos para importar.";
    }

    private async void OnImportNewClick(object sender, RoutedEventArgs e) => await ImportAsync("Skip");

    private async void OnUpdateExistingClick(object sender, RoutedEventArgs e) => await ImportAsync("Update");

    private async Task ImportAsync(string duplicateRule)
    {
        PreviewGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        PreviewGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selectedRows = _rows.Where(item => item.IsSelected).ToArray();
        if (selectedRows.Length == 0)
        {
            StatusText.Text = "Selecciona al menos un producto para importar.";
            return;
        }
        if (RefreshPreviewStatus(focusFirstInvalid: true) > 0) return;
        if (!ConfirmImport(duplicateRule, selectedRows.Length)) return;
        SetImportActionsEnabled(false);
        ImportProgressBar.Visibility = Visibility.Visible;
        StatusText.Text = duplicateRule.Equals("Update", StringComparison.OrdinalIgnoreCase)
            ? "Creando respaldo previo e importando productos nuevos y actualizando coincidencias..."
            : "Creando respaldo previo e importando solamente productos nuevos...";
        try
        {
            using var backup = await ApiClient.Client.PostAsync("api/maintenance/backups", null);
            if (!backup.IsSuccessStatusCode) { StatusText.Text = "No se importó nada porque no se pudo crear el respaldo previo: " + await backup.Content.ReadAsStringAsync(); return; }
            var operationId = Guid.NewGuid();
            using var response = await ApiClient.Client.PostAsJsonAsync("api/products/import", new { operationId, sourceFileName = Path.GetFileName(FileBox.Text), duplicateRule, rows = selectedRows.Select(item => new { item.RowNumber, item.Code, item.Description, item.Price, item.Cost, item.Stock, item.WholesalePrice, item.WholesaleMinimumQuantity, item.Category, item.MinimumStock, item.MaximumStock, item.UnitOfMeasure, item.SupplierName }).ToArray() });
            if (!response.IsSuccessStatusCode) { StatusText.Text = "La importación se revirtió: " + await response.Content.ReadAsStringAsync(); return; }
            var result = await response.Content.ReadFromJsonAsync<ImportResult>();
            var omittedByUser = _rows.Count - selectedRows.Length;
            StatusText.Text = result is null
                ? "Importación terminada."
                : duplicateRule.Equals("Update", StringComparison.OrdinalIgnoreCase)
                    ? $"Actualización terminada: {result.Created} nuevos, {result.Updated} existentes actualizados y {omittedByUser} omitidos por el usuario."
                    : $"Importación terminada: {result.Created} nuevos, {result.Skipped} existentes omitidos sin modificarse y {omittedByUser} omitidos por el usuario.";
            _lastImportResult = result;
            SaveReportButton.IsEnabled = true;
            StatusText.Text += " Puedes guardar el reporte cuando lo necesites.";
        }
        catch (Exception exception) { StatusText.Text = $"La importación no se completó: {exception.Message}"; }
        finally { ImportProgressBar.Visibility = Visibility.Collapsed; RefreshPreviewStatus(focusFirstInvalid: false); }
    }

    private bool ConfirmImport(string duplicateRule, int selectedCount)
    {
        var isUpdate = duplicateRule.Equals("Update", StringComparison.OrdinalIgnoreCase);
        var message = isUpdate
            ? $"Se importarán {selectedCount} producto(s) seleccionado(s). Los códigos que ya existan se actualizarán con la descripción, precios, mayoreo, departamento, límites, unidad y proveedor. La existencia quedará exactamente como aparece en el archivo. Cuando el control de inventario esté habilitado, el cambio también quedará registrado en movimientos. Los códigos nuevos se agregarán.\n\n¿Deseas continuar?"
            : $"Se importarán {selectedCount} producto(s) seleccionado(s). Los códigos que ya existan se omitirán y no se modificarán ni actualizarán. Solo se agregarán productos nuevos.\n\n¿Deseas continuar?";
        return MessageBox.Show(message, isUpdate ? "Actualizar inventario" : "Importar productos nuevos", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    private void SetImportActionsEnabled(bool enabled)
    {
        ImportNewButton.IsEnabled = enabled;
        UpdateExistingButton.IsEnabled = enabled;
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
        csv.WriteField("Resumen"); csv.WriteField(result is null ? "Importación terminada" : $"Creados {result.Created}; actualizados {result.Updated}; omitidos por duplicado {result.Skipped}; omitidos por el usuario {_rows.Count(item => !item.IsSelected)}"); csv.NextRecord();
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
                SetImportActionsEnabled(false);
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
            var duplicateCodes = _rows.Where(item => item.IsSelected && !string.IsNullOrWhiteSpace(item.Code)).GroupBy(item => item.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows) row.Status = row.IsSelected ? Validate(row, duplicateCodes) : "Omitido por el usuario";
            ApplySort();
            UpdatePreviewPage();
            var invalidRows = _rows.Where(item => item.IsSelected && item.Status.StartsWith("ERROR", StringComparison.Ordinal)).ToList();
            SetImportActionsEnabled(_rows.Any(item => item.IsSelected) && invalidRows.Count == 0);
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
            SetImportActionsEnabled(false);
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
        PageInfoText.Text = _sortedRows.Count == 0 ? "Sin filas cargadas." : $"Página {_currentPage} de {TotalPages}. Mostrando {first}-{last} de {_sortedRows.Count}; {_rows.Count(item => item.IsSelected)} seleccionados.";
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
