using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pos.Desktop;

public sealed class ProductImportPreviewRow
{
    public bool IsSelected { get; set; } = true;
    public int RowNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PriceText { get; set; } = "0";
    public string CostText { get; set; } = "0";
    public string StockText { get; set; } = "0";
    public string WholesalePriceText { get; set; } = "0";
    public string WholesaleMinimumQuantityText { get; set; } = "0";
    public decimal Price { get => ParseEditableNumber(PriceText); set => PriceText = FormatNumber(value); }
    public decimal Cost { get => ParseEditableNumber(CostText); set => CostText = FormatNumber(value); }
    public decimal Stock { get => ParseEditableInventoryNumber(StockText); set => StockText = FormatNumber(value); }
    public decimal WholesalePrice { get => ParseEditableNumber(WholesalePriceText); set => WholesalePriceText = FormatNumber(value); }
    public decimal WholesaleMinimumQuantity { get => ParseEditableInventoryNumber(WholesaleMinimumQuantityText); set => WholesaleMinimumQuantityText = FormatNumber(value); }
    public string Category { get; set; } = string.Empty;
    public string MinimumStockText { get; set; } = "0";
    public string MaximumStockText { get; set; } = "0";
    public decimal MinimumStock { get => ParseEditableInventoryNumber(MinimumStockText); set => MinimumStockText = FormatNumber(value); }
    public decimal MaximumStock { get => ParseEditableInventoryNumber(MaximumStockText); set => MaximumStockText = FormatNumber(value); }
    public string UnitOfMeasure { get; set; } = "Pieza";
    public string SupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    private static decimal ParseEditableNumber(string value)
    {
        var parsed = ParseDecimal(value);
        return parsed ?? decimal.MinValue;
    }

    private static decimal ParseEditableInventoryNumber(string value)
    {
        var parsed = ParseDecimal(value);
        return parsed is null || parsed < 0 ? 0m : parsed.Value;
    }

    private static decimal? ParseDecimal(string value)
    {
        return ProductImportFileReader.TryParseFlexibleNumber(value, out var parsed) ? parsed : null;
    }

    private static string FormatNumber(decimal value) => value == decimal.MinValue ? string.Empty : value.ToString("0.###", CultureInfo.GetCultureInfo("es-MX"));
}

public sealed record ProductImportSourceColumn(int Index, string Header, IReadOnlyList<string> Examples)
{
    public string Sample => Examples.FirstOrDefault() ?? string.Empty;
    public string ExampleText => Examples.Count == 0 ? string.Empty : string.Join(" | ", Examples);
    public string DisplayText => string.IsNullOrWhiteSpace(ExampleText) ? Header : $"{Header}  |  Ejemplos: {ExampleText}";
}

public sealed record ProductImportSourceRow(int RowNumber, IReadOnlyList<string> Values);

public sealed record ProductImportSource(IReadOnlyList<ProductImportSourceColumn> Columns, IReadOnlyList<ProductImportSourceRow> Rows);

public sealed class ProductImportColumnMapping
{
    public int? CodeColumn { get; set; }
    public int? DescriptionColumn { get; set; }
    public int? CostColumn { get; set; }
    public int? PriceColumn { get; set; }
    public int? WholesalePriceColumn { get; set; }
    public int? WholesaleMinimumQuantityColumn { get; set; }
    public int? StockColumn { get; set; }
    public int? CategoryColumn { get; set; }
    public int? MinimumStockColumn { get; set; }
    public int? MaximumStockColumn { get; set; }
    public int? UnitOfMeasureColumn { get; set; }
    public int? SupplierColumn { get; set; }

    public ProductImportColumnMapping Clone() => new()
    {
        CodeColumn = CodeColumn,
        DescriptionColumn = DescriptionColumn,
        CostColumn = CostColumn,
        PriceColumn = PriceColumn,
        WholesalePriceColumn = WholesalePriceColumn,
        WholesaleMinimumQuantityColumn = WholesaleMinimumQuantityColumn,
        StockColumn = StockColumn,
        CategoryColumn = CategoryColumn,
        MinimumStockColumn = MinimumStockColumn,
        MaximumStockColumn = MaximumStockColumn,
        UnitOfMeasureColumn = UnitOfMeasureColumn,
        SupplierColumn = SupplierColumn
    };
}

public static class ProductImportFileReader
{
    public static IReadOnlyList<ProductImportPreviewRow> Read(string path, decimal defaultWholesaleMinimum)
    {
        var source = ReadSource(path);
        return Map(source, SuggestMapping(source), defaultWholesaleMinimum);
    }

    public static ProductImportSource ReadSource(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsxSource(path) : ReadCsvSource(path);
    }

    public static ProductImportColumnMapping SuggestMapping(ProductImportSource source) => new()
    {
        // These aliases match the export headers used by Eleventa and common variants from spreadsheet edits.
        CodeColumn = Find(source, "codigo", "codigodebarras", "codigoproducto", "codigodeproducto", "clave"),
        DescriptionColumn = Find(source, "producto", "descripcion", "descripcionproducto", "nombre", "nombreproducto"),
        CostColumn = Find(source, "pcosto", "costo", "preciocosto", "preciodecosto", "preciocompra"),
        PriceColumn = Find(source, "pventa", "precioventa", "preciodeventa", "precio", "preciopublico"),
        WholesalePriceColumn = Find(source, "pmayoreo", "preciomayoreo", "preciodemayoreo", "mayoreo"),
        WholesaleMinimumQuantityColumn = Find(source, "minimomayoreo", "cantidadminimamayoreo", "minimoarticulosmayoreo", "minimodearticulos"),
        StockColumn = Find(source, "existencia", "existenciaactual", "stock", "inventario"),
        CategoryColumn = Find(source, "departamento", "departamentoprincipal", "categoria"),
        MinimumStockColumn = Find(source, "invminimo", "inventariominimo", "minimo", "stockminimo"),
        MaximumStockColumn = Find(source, "invmaximo", "inventariomaximo", "maximo", "stockmaximo"),
        UnitOfMeasureColumn = Find(source, "tipodeventa", "unidad", "unidaddemedida", "unidadmedida"),
        SupplierColumn = Find(source, "proveedor", "proveedorprincipal")
    };

    public static IReadOnlyList<ProductImportPreviewRow> Map(ProductImportSource source, ProductImportColumnMapping mapping, decimal defaultWholesaleMinimum)
    {
        var rows = new List<ProductImportPreviewRow>();
        foreach (var sourceRow in source.Rows)
        {
            var wholesale = Number(sourceRow, mapping.WholesalePriceColumn);
            var wholesaleMinimum = InventoryNumber(sourceRow, mapping.WholesaleMinimumQuantityColumn);
            rows.Add(new ProductImportPreviewRow
            {
                RowNumber = sourceRow.RowNumber,
                Code = Text(sourceRow, mapping.CodeColumn),
                Description = Text(sourceRow, mapping.DescriptionColumn),
                Cost = Number(sourceRow, mapping.CostColumn),
                Price = Number(sourceRow, mapping.PriceColumn),
                WholesalePrice = wholesale,
                WholesaleMinimumQuantity = wholesale > 0 ? (wholesaleMinimum > 0 ? wholesaleMinimum : defaultWholesaleMinimum) : 0m,
                Stock = InventoryNumber(sourceRow, mapping.StockColumn),
                Category = Text(sourceRow, mapping.CategoryColumn),
                MinimumStock = InventoryNumber(sourceRow, mapping.MinimumStockColumn),
                MaximumStock = InventoryNumber(sourceRow, mapping.MaximumStockColumn),
                UnitOfMeasure = NormalizeUnit(Text(sourceRow, mapping.UnitOfMeasureColumn)),
                SupplierName = Text(sourceRow, mapping.SupplierColumn)
            });
        }

        var duplicateCodes = rows.Where(item => !string.IsNullOrWhiteSpace(item.Code)).GroupBy(item => item.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) row.Status = Validate(row, duplicateCodes);
        return rows;
    }

    private static ProductImportSource ReadXlsxSource(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed() ?? throw new InvalidDataException("La hoja está vacía.");
        var firstColumn = used.FirstColumn().ColumnNumber();
        var lastColumn = used.LastColumn().ColumnNumber();
        // Eleventa exports the column names in row 1. Keep them even when the used range starts later.
        var headerRow = sheet.Row(1);
        var headers = headerRow.Cells(firstColumn, lastColumn).Select((cell, index) => HeaderOrDefault(cell.GetString(), index)).ToArray();
        var rows = new List<ProductImportSourceRow>();
        foreach (var row in used.RowsUsed().Where(row => row.RowNumber() > 1))
        {
            var values = row.Cells(firstColumn, lastColumn).Select(cell => cell.GetFormattedString().Trim()).ToArray();
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new ProductImportSourceRow(row.RowNumber(), values));
        }
        return BuildSource(headers, rows);
    }

    private static ProductImportSource ReadCsvSource(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(bytes); }
        var configuration = new CsvConfiguration(CultureInfo.GetCultureInfo("es-MX")) { DetectDelimiter = true, BadDataFound = null, MissingFieldFound = null, TrimOptions = TrimOptions.Trim };
        using var csv = new CsvReader(new StringReader(text), configuration);
        if (!csv.Read() || !csv.ReadHeader()) throw new InvalidDataException("El CSV no contiene encabezados.");
        var headers = (csv.HeaderRecord ?? []).Select(HeaderOrDefault).ToArray();
        var rows = new List<ProductImportSourceRow>();
        while (csv.Read())
        {
            var values = Enumerable.Range(0, headers.Length).Select(index => csv.GetField(index)?.Trim() ?? string.Empty).ToArray();
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new ProductImportSourceRow(csv.Parser.Row, values));
        }
        return BuildSource(headers, rows);
    }

    private static ProductImportSource BuildSource(IReadOnlyList<string> headers, IReadOnlyList<ProductImportSourceRow> rows)
    {
        var columns = headers.Select((header, index) => new ProductImportSourceColumn(
            index,
            header,
            rows.Select(row => Text(row, index))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray())).ToArray();
        return new ProductImportSource(columns, rows);
    }

    private static string HeaderOrDefault(string? value, int index)
    {
        var header = value?.Trim().TrimStart('\uFEFF') ?? string.Empty;
        return string.IsNullOrWhiteSpace(header) ? $"Columna {index + 1}" : header;
    }
    private static string Text(ProductImportSourceRow row, int? column) => column is >= 0 && column < row.Values.Count ? row.Values[column.Value].Trim() : string.Empty;
    private static decimal Number(ProductImportSourceRow row, int? column) => ParseNumber(Text(row, column));
    private static decimal InventoryNumber(ProductImportSourceRow row, int? column) => ParseInventoryNumber(Text(row, column));
    private static int? Find(ProductImportSource source, params string[] aliases)
    {
        var normalizedAliases = aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return source.Columns.FirstOrDefault(column => normalizedAliases.Contains(NormalizeHeader(column.Header)))?.Index;
    }

    private static decimal ParseNumber(string value)
    {
        return TryParseFlexibleNumber(value, out var parsed) ? parsed : decimal.MinValue;
    }

    private static decimal ParseInventoryNumber(string value)
    {
        var parsed = ParseNumber(value);
        return parsed == decimal.MinValue || parsed < 0 ? 0m : parsed;
    }

    private static string Validate(ProductImportPreviewRow row, HashSet<string> duplicates)
    {
        if (string.IsNullOrWhiteSpace(row.Code)) return "ERROR: código vacío";
        if (string.IsNullOrWhiteSpace(row.Description)) return "ERROR: descripción vacía";
        if (row.Price == decimal.MinValue || row.Cost == decimal.MinValue || row.WholesalePrice == decimal.MinValue) return "ERROR: número inválido";
        if (row.Price < 0 || row.Cost < 0 || row.Stock < 0 || row.WholesalePrice < 0 || row.MinimumStock < 0 || row.MaximumStock < 0) return "ERROR: valor negativo";
        if (row.MaximumStock > 0 && row.MaximumStock < row.MinimumStock) return "ERROR: máximo menor al mínimo";
        if (duplicates.Contains(row.Code.Trim())) return "ERROR: código repetido en archivo";
        return "Válido";
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character)).ToArray());
    }

    public static bool TryParseFlexibleNumber(string? value, out decimal result)
    {
        var cleaned = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned == "-")
        {
            result = 0m;
            return true;
        }

        cleaned = cleaned
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace("MXN", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("USD", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        var comma = cleaned.LastIndexOf(',');
        var point = cleaned.LastIndexOf('.');
        if (comma >= 0 && point >= 0)
        {
            // The rightmost separator is decimal: $1,000.00 and 1.000,00.
            cleaned = comma > point
                ? cleaned.Replace(".", string.Empty).Replace(',', '.')
                : cleaned.Replace(",", string.Empty);
        }
        else if (comma >= 0)
        {
            cleaned = IsThousandsSeparator(cleaned, comma) ? cleaned.Replace(",", string.Empty) : cleaned.Replace(',', '.');
        }
        else if (point >= 0 && IsThousandsSeparator(cleaned, point))
        {
            cleaned = cleaned.Replace(".", string.Empty);
        }

        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
    }

    private static bool IsThousandsSeparator(string value, int separatorIndex)
    {
        var decimals = value.Length - separatorIndex - 1;
        return decimals == 3 && value.Count(character => character == value[separatorIndex]) == 1;
    }

    public static string NormalizeUnit(string value)
    {
        var normalized = NormalizeHeader(value);
        return normalized switch
        {
            "" => "Pieza",
            "pieza" or "pza" or "pz" or "piezas" or "unidad" or "unidades" => "Pieza",
            "kg" or "kilo" or "kilos" or "kilogramo" or "kilogramos" or "peso" or "granel" => "Kilogramo",
            "g" or "gr" or "gramo" or "gramos" => "Gramo",
            "l" or "lt" or "lts" or "litro" or "litros" => "Litro",
            "ml" or "mililitro" or "mililitros" => "Mililitro",
            "m" or "metro" or "metros" => "Metro",
            "servicio" or "servicios" => "Servicio",
            _ => value.Trim().Length == 0 ? "Pieza" : value.Trim()
        };
    }
}
