using ClosedXML.Excel;
using Pos.Desktop;
using System.Text;

namespace Pos.DesktopTests;

public sealed class ProductImportFileReaderTests
{
    [Fact]
    public void ReadsEleventaXlsxAndPreservesLeadingZeros()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eleventa-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                var headers = new[] { "Código", "Producto", "P. Costo", "P. Venta", "P. Mayoreo", "Departamento", "Existencia", "Inv. Mínimo", "Inv. Máximo", "Tipo de Venta", "Proveedor" };
                for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
                sheet.Cell(2, 1).Value = "00123"; sheet.Cell(2, 2).Value = "Producto prueba"; sheet.Cell(2, 3).Value = 12.50m; sheet.Cell(2, 4).Value = 20m; sheet.Cell(2, 5).Value = 18m; sheet.Cell(2, 6).Value = "Abarrotes"; sheet.Cell(2, 7).Value = 7.5m; sheet.Cell(2, 8).Value = 2m; sheet.Cell(2, 9).Value = 20m; sheet.Cell(2, 10).Value = "Pieza"; sheet.Cell(2, 11).Value = "Proveedor prueba";
                workbook.SaveAs(path);
            }
            var row = Assert.Single(ProductImportFileReader.Read(path, 3m));
            Assert.Equal("00123", row.Code); Assert.Equal(12.50m, row.Cost); Assert.Equal(20m, row.Price); Assert.Equal(18m, row.WholesalePrice); Assert.Equal(3m, row.WholesaleMinimumQuantity); Assert.Equal(7.5m, row.Stock); Assert.Equal("Abarrotes", row.Category); Assert.Equal(2m, row.MinimumStock); Assert.Equal(20m, row.MaximumStock); Assert.Equal("Pieza", row.UnitOfMeasure); Assert.Equal("Proveedor prueba", row.SupplierName); Assert.Equal("Válido", row.Status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadsSemicolonCsvWithQuotedFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eleventa-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "Código;Producto;P. Costo;P. Venta;Existencia\r\n\"0007\";\"Producto, con coma\";10.50;15.00;4\r\n", new UTF8Encoding(true));
            var row = Assert.Single(ProductImportFileReader.Read(path, 1m));
            Assert.Equal("0007", row.Code); Assert.Equal("Producto, con coma", row.Description); Assert.Equal(4m, row.Stock); Assert.Equal("Válido", row.Status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ConvertsNonNumericInventoryFieldsToZero()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eleventa-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Sheet1");
                var headers = new[] { "Codigo", "Producto", "P. Costo", "P. Venta", "Existencia", "Inv. Minimo", "Inv. Maximo" };
                for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
                sheet.Cell(2, 1).Value = "ABC1";
                sheet.Cell(2, 2).Value = "Producto con inventario texto";
                sheet.Cell(2, 3).Value = 10m;
                sheet.Cell(2, 4).Value = 15m;
                sheet.Cell(2, 5).Value = "-";
                sheet.Cell(2, 6).Value = "sin dato";
                sheet.Cell(2, 7).Value = "N/A";
                workbook.SaveAs(path);
            }

            var row = Assert.Single(ProductImportFileReader.Read(path, 1m));

            Assert.Equal(0m, row.Stock);
            Assert.Equal(0m, row.MinimumStock);
            Assert.Equal(0m, row.MaximumStock);
            Assert.Equal("Válido", row.Status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EditablePreviewConvertsInvalidInventoryTextToZero()
    {
        var row = new ProductImportPreviewRow
        {
            Code = "EDIT1",
            Description = "Producto editado",
            PriceText = "12.50",
            CostText = "8",
            StockText = "-",
            MinimumStockText = "sin dato",
            MaximumStockText = "N/A"
        };

        Assert.Equal(12.50m, row.Price);
        Assert.Equal(8m, row.Cost);
        Assert.Equal(0m, row.Stock);
        Assert.Equal(0m, row.MinimumStock);
        Assert.Equal(0m, row.MaximumStock);
    }

    [Fact]
    public void EditablePreviewMarksInvalidPriceText()
    {
        var row = new ProductImportPreviewRow
        {
            Code = "EDIT2",
            Description = "Producto con precio invalido",
            PriceText = "precio",
            CostText = "8"
        };

        Assert.Equal(decimal.MinValue, row.Price);
        Assert.Equal(8m, row.Cost);
    }

    [Theory]
    [InlineData("", "Pieza")]
    [InlineData("pieza", "Pieza")]
    [InlineData("kg", "Kilogramo")]
    [InlineData("kilo", "Kilogramo")]
    [InlineData("granel", "Kilogramo")]
    [InlineData("gramos", "Gramo")]
    [InlineData("litro", "Litro")]
    public void NormalizesImportedUnitOfMeasure(string source, string expected)
    {
        Assert.Equal(expected, ProductImportFileReader.NormalizeUnit(source));
    }
}
