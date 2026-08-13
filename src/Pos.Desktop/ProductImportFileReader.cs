using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.IO;
using System.Text;

namespace Pos.Desktop;

public sealed record ProductImportPreviewRow(int RowNumber, string Code, string Description, decimal Price, decimal Cost, decimal Stock, decimal WholesalePrice, decimal WholesaleMinimumQuantity, string Category, decimal MinimumStock, decimal MaximumStock, string UnitOfMeasure, string SupplierName, string Status);

public static class ProductImportFileReader
{
    public static IReadOnlyList<ProductImportPreviewRow> Read(string path, decimal defaultWholesaleMinimum)
    {
        var extension = Path.GetExtension(path);
        var rows = extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(path, defaultWholesaleMinimum) : ReadCsv(path, defaultWholesaleMinimum);
        var duplicateCodes = rows.Where(item => !string.IsNullOrWhiteSpace(item.Code)).GroupBy(item => item.Code.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rows.Select(item => item with { Status = Validate(item, duplicateCodes) }).ToArray();
    }

    private static IReadOnlyList<ProductImportPreviewRow> ReadXlsx(string path, decimal defaultWholesaleMinimum)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed() ?? throw new InvalidDataException("La hoja está vacía.");
        var headerRow = used.FirstRow();
        var headers = headerRow.Cells(1, used.ColumnCount()).Select((cell, index) => (Name: NormalizeHeader(cell.GetString()), Column: index + 1)).ToDictionary(item => item.Name, item => item.Column, StringComparer.OrdinalIgnoreCase);
        var result = new List<ProductImportPreviewRow>();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            var code = Text(row, headers, "codigo", "codigodebarras", "clave");
            var description = Text(row, headers, "producto", "descripcion", "nombre");
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(description)) continue;
            var wholesale = Number(row, headers, "pmayoreo", "preciomayoreo", "mayoreo");
            result.Add(new(row.RowNumber(), code, description, Number(row, headers, "pventa", "precioventa", "precio"), Number(row, headers, "pcosto", "costo", "preciocosto"), Number(row, headers, "existencia", "stock", "inventario"), wholesale, wholesale > 0 ? defaultWholesaleMinimum : 0m, Text(row, headers, "departamento", "categoria"), Number(row, headers, "invminimo", "inventariominimo", "minimo"), Number(row, headers, "invmaximo", "inventariomaximo", "maximo"), Text(row, headers, "tipodeventa", "unidad", "unidaddemedida"), Text(row, headers, "proveedor", "proveedorprincipal"), string.Empty));
        }
        return result;
    }

    private static IReadOnlyList<ProductImportPreviewRow> ReadCsv(string path, decimal defaultWholesaleMinimum)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(bytes); }
        var configuration = new CsvConfiguration(CultureInfo.GetCultureInfo("es-MX")) { DetectDelimiter = true, BadDataFound = null, MissingFieldFound = null, TrimOptions = TrimOptions.Trim };
        using var csv = new CsvReader(new StringReader(text), configuration);
        if (!csv.Read() || !csv.ReadHeader()) throw new InvalidDataException("El CSV no contiene encabezados.");
        var headers = (csv.HeaderRecord ?? []).Select((name, index) => (Name: NormalizeHeader(name), Column: index)).ToDictionary(item => item.Name, item => item.Column, StringComparer.OrdinalIgnoreCase);
        var result = new List<ProductImportPreviewRow>();
        while (csv.Read())
        {
            var code = Text(csv, headers, "codigo", "codigodebarras", "clave");
            var description = Text(csv, headers, "producto", "descripcion", "nombre");
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(description)) continue;
            var wholesale = Number(csv, headers, "pmayoreo", "preciomayoreo", "mayoreo");
            result.Add(new(csv.Parser.Row, code, description, Number(csv, headers, "pventa", "precioventa", "precio"), Number(csv, headers, "pcosto", "costo", "preciocosto"), Number(csv, headers, "existencia", "stock", "inventario"), wholesale, wholesale > 0 ? defaultWholesaleMinimum : 0m, Text(csv, headers, "departamento", "categoria"), Number(csv, headers, "invminimo", "inventariominimo", "minimo"), Number(csv, headers, "invmaximo", "inventariomaximo", "maximo"), Text(csv, headers, "tipodeventa", "unidad", "unidaddemedida"), Text(csv, headers, "proveedor", "proveedorprincipal"), string.Empty));
        }
        return result;
    }

    private static string Text(IXLRangeRow row, Dictionary<string, int> headers, params string[] aliases) => Find(headers, aliases) is int column ? row.Cell(column).GetFormattedString().Trim() : string.Empty;
    private static decimal Number(IXLRangeRow row, Dictionary<string, int> headers, params string[] aliases) => Find(headers, aliases) is int column ? ParseNumber(row.Cell(column).GetFormattedString()) : 0m;
    private static string Text(CsvReader csv, Dictionary<string, int> headers, params string[] aliases) => Find(headers, aliases) is int column ? csv.GetField(column)?.Trim() ?? string.Empty : string.Empty;
    private static decimal Number(CsvReader csv, Dictionary<string, int> headers, params string[] aliases) => ParseNumber(Text(csv, headers, aliases));
    private static int? Find(Dictionary<string, int> headers, params string[] aliases) { foreach (var alias in aliases) if (headers.TryGetValue(alias, out var column)) return column; return null; }

    private static decimal ParseNumber(string value)
    {
        var cleaned = value.Trim().Replace("$", string.Empty).Replace(" ", string.Empty);
        if (string.IsNullOrEmpty(cleaned)) return 0m;
        if (decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.GetCultureInfo("es-MX"), out var mexican)) return mexican;
        if (decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out var invariant)) return invariant;
        return decimal.MinValue;
    }

    private static string Validate(ProductImportPreviewRow row, HashSet<string> duplicates)
    {
        if (string.IsNullOrWhiteSpace(row.Code)) return "ERROR: código vacío";
        if (string.IsNullOrWhiteSpace(row.Description)) return "ERROR: descripción vacía";
        if (row.Price == decimal.MinValue || row.Cost == decimal.MinValue || row.Stock == decimal.MinValue || row.WholesalePrice == decimal.MinValue || row.MinimumStock == decimal.MinValue || row.MaximumStock == decimal.MinValue) return "ERROR: número inválido";
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
}
