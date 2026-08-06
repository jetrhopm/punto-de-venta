using Pos.Printing;
using System.Text;

namespace Pos.UnitTests;

public sealed class TicketPdfTests
{
    [Fact]
    public void CreatesValidPdfTicketWithSaleDetails()
    {
        var pdf = TicketPdfWriter.Create(new TicketPdfData("Tienda de prueba", Guid.NewGuid(), DateTimeOffset.UtcNow, [new TicketPdfLine("Producto", 2m, 10m, 20m)], 20m, 20m, 0m));

        Assert.True(pdf.Length > 100);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.EndsWith("%%EOF\r\n", Encoding.ASCII.GetString(pdf));
    }
}
