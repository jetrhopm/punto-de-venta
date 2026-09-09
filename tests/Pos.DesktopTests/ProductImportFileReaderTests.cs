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
    public void ReadsAllEleventaColumnsAndKeepsThreeExamplesPerColumn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eleventa-columns-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path,
                "Código;Producto;P. Costo;P. Venta;P. Mayoreo;Departamento;Existencia;Inv. Mínimo;Inv. Máximo;Tipo de Venta;Proveedor\r\n" +
                "001;Arroz;10;15;14;Abarrotes;8;1;20;Pieza;Proveedor A\r\n" +
                "002;Frijol;12;18;16;Abarrotes;6;1;20;Pieza;Proveedor B\r\n" +
                "003;Azúcar;9;14;13;Abarrotes;5;1;20;Pieza;Proveedor C\r\n" +
                "004;Sal;4;7;6;Abarrotes;9;1;20;Pieza;Proveedor D\r\n", new UTF8Encoding(true));

            var source = ProductImportFileReader.ReadSource(path);
            var mapping = ProductImportFileReader.SuggestMapping(source);

            Assert.Equal(11, source.Columns.Count);
            Assert.Equal("Código", source.Columns[0].Header);
            Assert.Equal(new[] { "001", "002", "003" }, source.Columns[0].Examples);
            Assert.Equal(0, mapping.CodeColumn);
            Assert.Equal(1, mapping.DescriptionColumn);
            Assert.Equal(3, mapping.PriceColumn);
            Assert.Equal(10, mapping.SupplierColumn);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MapsUnknownAndDisorderedColumnsSelectedByUser()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mapped-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Productos");
                sheet.Cell(1, 1).Value = "Dato A";
                sheet.Cell(1, 2).Value = "Dato B";
                sheet.Cell(1, 3).Value = "Dato C";
                sheet.Cell(1, 4).Value = "Dato D";
                sheet.Cell(2, 1).Value = 27.50m;
                sheet.Cell(2, 2).Value = "Producto fuera de orden";
                sheet.Cell(2, 3).Value = "000045";
                sheet.Cell(2, 4).Value = 8m;
                workbook.SaveAs(path);
            }

            var source = ProductImportFileReader.ReadSource(path);
            var mapping = new ProductImportColumnMapping
            {
                PriceColumn = 0,
                DescriptionColumn = 1,
                CodeColumn = 2,
                StockColumn = 3
            };

            var row = Assert.Single(ProductImportFileReader.Map(source, mapping, 1m));

            Assert.Equal("000045", row.Code);
            Assert.Equal("Producto fuera de orden", row.Description);
            Assert.Equal(27.50m, row.Price);
            Assert.Equal(8m, row.Stock);
            Assert.True(row.IsSelected);
            Assert.Equal("Válido", row.Status);
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
    [InlineData("$1,000.00", 1000)]
    [InlineData("$1.000,50", 1000.50)]
    [InlineData("MXN 25,50", 25.50)]
    public void ParsesCommercialCurrencyFormats(string text, decimal expected)
    {
        Assert.True(ProductImportFileReader.TryParseFlexibleNumber(text, out var parsed));
        Assert.Equal(expected, parsed);

        var preview = new ProductImportPreviewRow { PriceText = text };
        Assert.Equal(expected, preview.Price);
    }

    [Fact]
    public void MapsWholesaleMinimumFromExportedColumn()
    {
        var source = new ProductImportSource(
        [
            new ProductImportSourceColumn(0, "Codigo", ["001"]),
            new ProductImportSourceColumn(1, "Descripcion", ["Producto"]),
            new ProductImportSourceColumn(2, "PrecioMayoreo", ["$90.00"]),
            new ProductImportSourceColumn(3, "MinimoMayoreo", ["6"])
        ],
        [new ProductImportSourceRow(2, ["001", "Producto", "$90.00", "6"])]);

        var row = Assert.Single(ProductImportFileReader.Map(source, ProductImportFileReader.SuggestMapping(source), 3m));

        Assert.Equal(90m, row.WholesalePrice);
        Assert.Equal(6m, row.WholesaleMinimumQuantity);
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
