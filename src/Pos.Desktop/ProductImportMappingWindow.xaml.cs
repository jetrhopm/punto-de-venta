using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Pos.Desktop;

public partial class ProductImportMappingWindow : Window
{
    private readonly ProductImportSource _source;

    public IReadOnlyList<ColumnOption> ColumnOptions { get; }
    public IReadOnlyList<MappingRow> MappingRows { get; }
    public ProductImportColumnMapping Mapping { get; private set; }

    public ProductImportMappingWindow(ProductImportSource source, ProductImportColumnMapping mapping)
    {
        InitializeComponent();
        _source = source;
        Mapping = mapping.Clone();
        ColumnOptions = [new(null, "No importar"), .. source.Columns.Select(column => new ColumnOption(column.Index, column.DisplayText))];
        MappingRows = CreateRows(Mapping);
        DataContext = this;
        BuildSourcePreview();
    }

    private IReadOnlyList<MappingRow> CreateRows(ProductImportColumnMapping mapping) =>
    [
        new("Código de barras o clave", "Obligatorio", mapping.CodeColumn, _source),
        new("Nombre o descripción", "Obligatorio", mapping.DescriptionColumn, _source),
        new("Costo", "Opcional", mapping.CostColumn, _source),
        new("Precio de venta", "Opcional", mapping.PriceColumn, _source),
        new("Precio de mayoreo", "Opcional", mapping.WholesalePriceColumn, _source),
        new("Existencia", "Opcional", mapping.StockColumn, _source),
        new("Departamento", "Opcional", mapping.CategoryColumn, _source),
        new("Inventario mínimo", "Opcional", mapping.MinimumStockColumn, _source),
        new("Inventario máximo", "Opcional", mapping.MaximumStockColumn, _source),
        new("Unidad de venta", "Opcional", mapping.UnitOfMeasureColumn, _source),
        new("Proveedor", "Opcional", mapping.SupplierColumn, _source)
    ];

    private void OnAutoDetectClick(object sender, RoutedEventArgs e)
    {
        ApplyToRows(ProductImportFileReader.SuggestMapping(_source));
        StatusText.Text = "Se restauró la propuesta automática. Revisa los ejemplos antes de continuar.";
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        MappingGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        MappingGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        if (MappingRows[0].SelectedColumnIndex is null || MappingRows[1].SelectedColumnIndex is null)
        {
            StatusText.Text = "Selecciona las columnas para Código y Nombre o descripción.";
            return;
        }

        var repeated = MappingRows.Where(row => row.SelectedColumnIndex.HasValue)
            .GroupBy(row => row.SelectedColumnIndex!.Value)
            .FirstOrDefault(group => group.Count() > 1);
        if (repeated is not null)
        {
            var header = _source.Columns.First(column => column.Index == repeated.Key).Header;
            StatusText.Text = $"La columna '{header}' está asignada a más de un dato. Elige una columna diferente en cada renglón.";
            return;
        }

        Mapping = BuildMapping();
        DialogResult = true;
    }

    private ProductImportColumnMapping BuildMapping() => new()
    {
        CodeColumn = MappingRows[0].SelectedColumnIndex,
        DescriptionColumn = MappingRows[1].SelectedColumnIndex,
        CostColumn = MappingRows[2].SelectedColumnIndex,
        PriceColumn = MappingRows[3].SelectedColumnIndex,
        WholesalePriceColumn = MappingRows[4].SelectedColumnIndex,
        StockColumn = MappingRows[5].SelectedColumnIndex,
        CategoryColumn = MappingRows[6].SelectedColumnIndex,
        MinimumStockColumn = MappingRows[7].SelectedColumnIndex,
        MaximumStockColumn = MappingRows[8].SelectedColumnIndex,
        UnitOfMeasureColumn = MappingRows[9].SelectedColumnIndex,
        SupplierColumn = MappingRows[10].SelectedColumnIndex
    };

    private void ApplyToRows(ProductImportColumnMapping mapping)
    {
        var values = new int?[]
        {
            mapping.CodeColumn, mapping.DescriptionColumn, mapping.CostColumn, mapping.PriceColumn,
            mapping.WholesalePriceColumn, mapping.StockColumn, mapping.CategoryColumn,
            mapping.MinimumStockColumn, mapping.MaximumStockColumn, mapping.UnitOfMeasureColumn,
            mapping.SupplierColumn
        };
        for (var index = 0; index < MappingRows.Count; index++) MappingRows[index].SelectedColumnIndex = values[index];
    }

    private void BuildSourcePreview()
    {
        SourcePreviewGrid.Columns.Clear();
        SourcePreviewGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Fila",
            Binding = new Binding(nameof(ProductImportSourceRow.RowNumber)),
            Width = 58
        });
        foreach (var column in _source.Columns)
        {
            SourcePreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding($"Values[{column.Index}]"),
                Width = new DataGridLength(145)
            });
        }
        SourcePreviewGrid.ItemsSource = _source.Rows.Take(3).ToArray();
    }

    public sealed record ColumnOption(int? Index, string DisplayText);

    public sealed class MappingRow(string targetName, string requirement, int? selectedColumnIndex, ProductImportSource source) : INotifyPropertyChanged
    {
        private int? _selectedColumnIndex = selectedColumnIndex;
        public string TargetName { get; } = targetName;
        public string Requirement { get; } = requirement;
        public int? SelectedColumnIndex
        {
            get => _selectedColumnIndex;
            set
            {
                if (_selectedColumnIndex == value) return;
                _selectedColumnIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExampleText));
            }
        }
        public string ExampleText => SelectedColumnIndex is int index
            ? source.Columns.FirstOrDefault(column => column.Index == index)?.ExampleText ?? string.Empty
            : string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
