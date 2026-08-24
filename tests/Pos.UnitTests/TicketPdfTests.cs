using Pos.Printing;
using System.Globalization;
using System.Text;

namespace Pos.UnitTests;

public sealed class TicketPdfTests
{
    [Fact]
    public void CreatesValidPdfTicketWithSaleDetails()
    {
        var pdf = TicketPdfWriter.Create(new TicketPdfData("Tienda de prueba", Guid.NewGuid(), DateTimeOffset.UtcNow, [new TicketPdfLine("Producto", 2m, 10m, 20m)], 20m, 20m, 0m));
        var text = Encoding.Latin1.GetString(pdf);

        Assert.True(pdf.Length > 500);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.Contains("/MediaBox [0 0 226.772", text);
        Assert.Contains("COMPROBANTE DE VENTA", text);
        Assert.Contains("TOTAL:", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public void PrintsConfiguredNumericFolioInsteadOfInternalSaleId()
    {
        var saleId = Guid.NewGuid();
        var pdf = TicketPdfWriter.Create(new TicketPdfData("Tienda de prueba", saleId, DateTimeOffset.UtcNow, [new TicketPdfLine("Producto", 1m, 10m, 10m)], 10m, 10m, 0m)
        {
            Folio = 1234
        });
        var text = Encoding.Latin1.GetString(pdf);

        Assert.Contains($"VENTA: {1234.ToString("N0", CultureInfo.CurrentCulture)}", text);
        Assert.DoesNotContain($"VENTA: {saleId.ToString("N")[..8].ToUpperInvariant()}", text);
    }

    [Fact]
    public void UsesThermalPaperWidthAndGrowsPageForLongTickets()
    {
        var saleId = Guid.NewGuid();
        var shortTicket = TicketPdfWriter.Create(CreateTicket(saleId, 58, 1));
        var longTicket = TicketPdfWriter.Create(CreateTicket(saleId, 58, 30));
        var shortText = Encoding.Latin1.GetString(shortTicket);
        var longText = Encoding.Latin1.GetString(longTicket);

        Assert.Contains("/MediaBox [0 0 164.409", shortText);
        Assert.True(ReadPageHeight(longText) > ReadPageHeight(shortText));
    }

    private static TicketPdfData CreateTicket(Guid saleId, int widthMm, int lineCount) => new(
        "Abarrotes de prueba",
        "Abarrotes de prueba, S.A. de C.V.",
        "XAXX010101000",
        "Calle Principal 123, Colonia Centro",
        "555 123 4567",
        "Venta al público en general",
        "Gracias por su compra",
        widthMm,
        saleId,
        Guid.NewGuid(),
        "Caja 1",
        "Administrador",
        DateTimeOffset.UtcNow,
        Enumerable.Range(1, lineCount).Select(index => new TicketPdfLine($"Producto de prueba número {index}", 1m, 10m, 10m)).ToArray(),
        [new TicketPdfPayment("Cash", lineCount * 10m, lineCount * 10m, 0m)],
        lineCount * 10m);

    private static decimal ReadPageHeight(string pdf)
    {
        const string marker = "/MediaBox [0 0 ";
        var start = pdf.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = pdf.IndexOf(']', start);
        return decimal.Parse(pdf[start..end].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], System.Globalization.CultureInfo.InvariantCulture);
    }
}
